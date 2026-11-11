"""Check mail transport requests and AppHost publish configuration."""
import json
import os
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[2]
APPHOST = ROOT / "samples/SharedPostgres/AppHost/bin/Debug/net10.0/AppHost.dll"


def run(*args, env=None):
    result = subprocess.run(args, cwd=ROOT, env=env, text=True, capture_output=True, timeout=180)
    if result.returncode:
        raise RuntimeError(result.stdout + result.stderr)
    print(result.stdout.strip(), flush=True)


run("dotnet", "build", "samples/SharedPostgres/SharedPostgres.slnx", "--nologo", "-v:q")
run("dotnet", "run", "--project", "tests/Mail/Mail.fsproj", "-v:q")
with tempfile.TemporaryDirectory(prefix="aspiringfarmer-mail-") as directory:
    for transport in ("smtp", "exchange-smtp", "graph", "mailpit"):
        manifest = Path(directory) / f"{transport}.json"
        env = dict(os.environ, Mail__Transport=transport, DOTNET_ENVIRONMENT="Development")
        env["Parameters__mail-smtp-password"] = "smtp-secret-canary"
        env["Parameters__mail-client-secret"] = "entra-secret-canary"
        env["Mail__Smtp__Port"] = "465"
        env["Mail__Smtp__Tls"] = "ssl-on-connect"
        run("dotnet", str(APPHOST), "--publisher", "manifest", "--output-path", str(manifest), env=env)
        text = manifest.read_text()
        resources = json.loads(text)["resources"]
        assert "mailpit" not in resources, transport
        assert "smtp-secret-canary" not in text and "entra-secret-canary" not in text
        environment = resources["storefront"]["env"]
        if transport == "mailpit":
            assert "Mail__Transport" not in environment
        else:
            assert environment["Mail__Transport"] == transport
            assert environment["Mail__From"] == "{mail-from.value}"
            secret = "mail-smtp-password" if transport == "smtp" else "mail-client-secret"
            assert resources[secret]["inputs"]["value"]["secret"] is True
            assert "{" + secret + ".value}" in environment.values()
            if transport == "smtp":
                assert environment["Mail__Smtp__Host"] == "{mail-smtp-host.value}"
                assert environment["Mail__Smtp__Username"] == "{mail-smtp-username.value}"
                assert environment["Mail__Smtp__Port"] == "465"
                assert environment["Mail__Smtp__Tls"] == "ssl-on-connect"
        print(f"PASS: {transport} manifest excludes Mailpit and keeps secrets deferred", flush=True)
