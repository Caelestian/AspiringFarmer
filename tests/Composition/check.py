"""Publish the composition sample and check its Docker Compose startup paths."""

import json
import os
from pathlib import Path
import secrets
import subprocess
import sys
import tempfile
import time
from urllib.error import URLError
from urllib.request import ProxyHandler, build_opener

ROOT = Path(__file__).resolve().parents[2]
PROTOTYPES = ROOT / "prototypes"
API_PORT = "8080"
HTTP = build_opener(ProxyHandler({}))


def run(*args, cwd=ROOT, env=None, expected_exit=0, input_text=None, timeout=180):
    result = subprocess.run(
        args, cwd=cwd, env=env, input=input_text, capture_output=True,
        text=True, timeout=timeout,
    )
    if result.returncode != expected_exit:
        output = (result.stdout + result.stderr).strip()
        if env:
            for key in ("BLUE_PASSWORD", "GREEN_PASSWORD"):
                output = output.replace(env[key], "[redacted]")
        raise RuntimeError(
            f"{' '.join(map(str, args))} exited {result.returncode}, "
            f"expected {expected_exit}\n{output}"
        )
    return result.stdout.strip()


def build_image(output, project, base, image):
    context = output / project
    print(f"Building {project} image", flush=True)
    run("dotnet", "publish", PROTOTYPES / project, "-c", "Release", "-o", context, timeout=300)
    dockerfile = f'FROM {base}\nWORKDIR /app\nCOPY . .\nENTRYPOINT ["dotnet", "{project}.dll"]\n'
    run("docker", "build", "-t", image, "-f", "-", context, input_text=dockerfile, timeout=300)


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def check_health(address):
    deadline = time.monotonic() + 20
    while True:
        try:
            with HTTP.open(f"http://{address}/health", timeout=2) as response:
                require(response.status == 200 and response.read() == b'"healthy"',
                        f"{address}/health did not return HTTP 200 with the expected body")
                return
        except (URLError, TimeoutError) as error:
            if time.monotonic() >= deadline:
                raise RuntimeError(f"{address}/health did not become ready") from error
            time.sleep(0.5)


def check_scenario(output, project, env, fail_blue):
    files = ["-f", "docker-compose.yaml", "-f", "ports.yaml"]
    if fail_blue:
        files += ["-f", "failure.yaml"]

    def compose(*args, expected_exit=0):
        return run("docker", "compose", "-p", project, *files, *args,
                   cwd=output, env=env, expected_exit=expected_exit)

    def state(service):
        container = compose("ps", "-aq", service)
        require(container, f"{project}: missing {service} container")
        return json.loads(run("docker", "inspect", "--format", "{{json .State}}", container))

    print(f"Starting {project}", flush=True)
    try:
        compose("up", "-d", expected_exit=1 if fail_blue else 0)
        for branch in ("blue", "green"):
            database = f"{branch}-postgres"
            migration = f"{branch}-migration"
            api = f"{branch}-api"
            failed = fail_blue and branch == "blue"
            db_state = state(database)
            migration_state = state(migration)
            api_state = state(api)
            require(db_state.get("Health", {}).get("Status") == "healthy", f"{database} is unhealthy")
            require(migration_state["Status"] == "exited" and migration_state["ExitCode"] == int(failed),
                    f"{migration} is {migration_state['Status']} with exit code {migration_state['ExitCode']}")
            table_exists = compose(
                "exec", "-T", database, "sh", "-ec",
                'PGPASSWORD="$POSTGRES_PASSWORD" psql -h 127.0.0.1 -U "$POSTGRES_USER" '
                '-d "$1" -Atc "select to_regclass(\'public.migration_example\') is not null"',
                "sh", f"{branch}-db",
            )
            require(table_exists == ("f" if failed else "t"), f"Unexpected migration table state in {branch}-db")
            if failed:
                require("SQLSTATE 42P01" in compose("logs", migration), f"{migration} did not fail as expected")
                require(api_state["Status"] == "created" and api_state["StartedAt"].startswith("0001-"),
                        f"{api} started after its migration failed")
                print(f"PASS: {migration} failed and {api} never started", flush=True)
            else:
                # Docker emits UTC timestamps with up to nine fractional digits.
                started = api_state["StartedAt"].removesuffix("Z")
                finished = migration_state["FinishedAt"].removesuffix("Z")
                require(api_state["Running"] and started >= finished,
                        f"{api} did not start after {migration} completed")
                check_health(compose("port", api, API_PORT))
                print(f"PASS: {migration} created its table and {api} returned HTTP 200", flush=True)
    finally:
        compose("down", "-v", "--remove-orphans")


def main():
    run("docker", "info", "--format", "{{.ServerVersion}}")
    run("docker", "compose", "version")
    with tempfile.TemporaryDirectory(prefix="aspiringfarmer-compose-") as directory:
        output = Path(directory)
        project = output.name.lower().replace("_", "-")
        api_image = f"{project}-api:smoke"
        migration_image = f"{project}-migration:smoke"
        images = []
        try:
            apphost = PROTOTYPES / "Composition" / "Composition.fsproj"
            print("Building the AppHost and publishing Docker Compose", flush=True)
            run("dotnet", "build", apphost, timeout=300)
            run("aspire", "publish", "--apphost", apphost, "--no-build",
                "--output-path", output, "--", "--compose", timeout=300)
            build_image(output, "Service", "mcr.microsoft.com/dotnet/aspnet:10.0", api_image)
            images.append(api_image)
            build_image(output, "PostgreSQL.Migration", "mcr.microsoft.com/dotnet/runtime:10.0", migration_image)
            images.append(migration_image)

            (output / "ports.yaml").write_text(
                f'services:\n  blue-api:\n    ports: ["127.0.0.1::{API_PORT}"]\n'
                f'  green-api:\n    ports: ["127.0.0.1::{API_PORT}"]\n', encoding="utf-8",
            )
            (output / "failure.yaml").write_text(
                'services:\n  blue-migration:\n    command: ["--fail"]\n', encoding="utf-8",
            )
            env = os.environ.copy()
            for branch in ("BLUE", "GREEN"):
                env[f"{branch}_API_IMAGE"] = api_image
                env[f"{branch}_MIGRATION_IMAGE"] = migration_image
                env[f"{branch}_API_PORT"] = API_PORT
                env[f"{branch}_PASSWORD"] = secrets.token_hex(24)
            check_scenario(output, f"{project}-success", env, fail_blue=False)
            check_scenario(output, f"{project}-failure", env, fail_blue=True)
        finally:
            if images:
                run("docker", "image", "rm", *images)


if __name__ == "__main__":
    try:
        main()
    except (OSError, RuntimeError, subprocess.TimeoutExpired) as error:
        sys.exit(f"FAIL: {error}")
