"""Time the sample solution build and prebuilt AppHost startup on local Docker."""

import argparse
import json
import os
from pathlib import Path
import platform
import re
import signal
import statistics
import subprocess
import threading
import time

SAMPLE = Path(__file__).resolve().parent
APPHOST = SAMPLE / "AppHost"
APPHOST_DLL = APPHOST / "bin/Debug/net10.0/AppHost.dll"
SOLUTION = "SharedPostgres.slnx"
DATABASES = {"identity", "catalog", "orders", "inventory", "reporting"}
RESOURCES = DATABASES | {name + "-schema" for name in DATABASES} | {"postgres", "storefront", "back-office"}
METRICS = ("cleanBuildMs", "noChangeBuildMs", "appHostStartedMs", "allResourcesReadyMs")


def run(*args):
    result = subprocess.run(args, cwd=SAMPLE, text=True, stdout=subprocess.PIPE,
                            stderr=subprocess.STDOUT, timeout=300)
    if result.returncode != 0:
        raise RuntimeError(f"{' '.join(args)} exited {result.returncode}\n{result.stdout}")
    return result.stdout.strip()


def build():
    start = time.monotonic()
    run("dotnet", "build", SOLUTION, "--no-restore", "--nologo", "-v:q")
    return (time.monotonic() - start) * 1000


def measure_startup():
    env = dict(os.environ, DOTNET_ENVIRONMENT="Development", ASPNETCORE_URLS="http://127.0.0.1:0",
               ASPIRE_ALLOW_UNSECURED_TRANSPORT="true", Mail__Transport="disabled")
    output = []
    start = time.monotonic()
    process = subprocess.Popen(["dotnet", str(APPHOST_DLL), "--benchmark"], cwd=APPHOST,
                               env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                               text=True, start_new_session=True)

    def collect():
        for line in process.stdout:
            output.append(((time.monotonic() - start) * 1000, line.strip()))

    reader = threading.Thread(target=collect, daemon=True)
    reader.start()
    try:
        process.wait(timeout=150)
    finally:
        if process.poll() is None:
            os.killpg(process.pid, signal.SIGINT)
            try:
                process.wait(timeout=20)
            except subprocess.TimeoutExpired:
                os.killpg(process.pid, signal.SIGKILL)
                process.wait()
        reader.join(timeout=5)
        process.stdout.close()

    markers = {line.removeprefix("sample:"): elapsed for elapsed, line in output if line.startswith("sample:")}
    ready = {key.removeprefix("ready:"): elapsed for key, elapsed in markers.items() if key.startswith("ready:")}
    if process.returncode != 0 or ready.keys() != RESOURCES or not {"host-started", "all-ready"} <= markers.keys():
        log = "\n".join(line for _, line in output)
        log = re.sub(r"([?&]t=)[^\s&]+", r"\1[redacted]", log)
        raise RuntimeError(f"AppHost exited {process.returncode}. Missing resources: {sorted(RESOURCES - ready.keys())}\n{log}")

    return {
        "appHostStartedMs": markers["host-started"],
        "allResourcesReadyMs": markers["all-ready"],
        "resourceReadyMs": ready,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--runs", type=int, default=5)
    parser.add_argument("--output", type=Path, default=SAMPLE / "../../artifacts/shared-postgres.json")
    options = parser.parse_args()
    if options.runs < 1:
        parser.error("--runs must be positive")

    environment = {
        "platform": platform.platform(),
        "dotnetSdk": run("dotnet", "--version"),
        "dockerServer": run("docker", "version", "--format", "{{.Server.Version}}"),
    }
    print("Restoring, building, and running one untimed startup to warm the local environment.", flush=True)
    run("dotnet", "restore", SOLUTION, "--nologo", "-v:q")
    build()
    measure_startup()

    samples = []
    for index in range(options.runs):
        run("dotnet", "clean", SOLUTION, "--nologo", "-v:q")
        clean = build()
        no_change = build()
        sample = {"cleanBuildMs": clean, "noChangeBuildMs": no_change, **measure_startup()}
        samples.append(sample)
        print(f"Run {index + 1}: " + ", ".join(f"{key} {sample[key]:.0f} ms" for key in METRICS), flush=True)

    median = {key: statistics.median(sample[key] for sample in samples) for key in METRICS}
    print("Median: " + ", ".join(f"{key} {median[key]:.0f} ms" for key in METRICS), flush=True)
    options.output.parent.mkdir(parents=True, exist_ok=True)
    options.output.write_text(json.dumps({"environment": environment, "medianMs": median, "samples": samples},
                                         indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
