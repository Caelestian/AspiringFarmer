"""Verify Storefront's opt-in OIDC configuration in the published manifest."""
import json
import os
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[2]
APPHOST = ROOT / "samples/SharedPostgres/AppHost/bin/Debug/net10.0/AppHost.dll"
PREFIX = "storefront-auth"
CANARY = "oidc-client-secret-canary"

subprocess.run(
    ["dotnet", "build", "samples/SharedPostgres/SharedPostgres.slnx", "--nologo", "-v:q"],
    cwd=ROOT, check=True, timeout=180,
)

with tempfile.TemporaryDirectory(prefix="aspiringfarmer-oidc-") as directory:
    for enabled in ("false", "true"):
        manifest = Path(directory) / f"{enabled}.json"
        env = dict(os.environ, Identity__Enabled=enabled, Identity__Scopes__0="email", Mail__Transport="disabled")
        env[f"Parameters__{PREFIX}-client-secret"] = CANARY
        subprocess.run(
            ["dotnet", str(APPHOST), "--publisher", "manifest", "--output-path", str(manifest)],
            cwd=ROOT, env=env, check=True, timeout=60,
        )
        text = manifest.read_text()
        resources = json.loads(text)["resources"]
        storefront = resources["storefront"].get("env", {})
        assert CANARY not in text
        if enabled == "true":
            assert storefront["Identity__Authority"] == f"{{{PREFIX}-authority.value}}"
            assert storefront["Identity__ClientId"] == f"{{{PREFIX}-client-id.value}}"
            assert storefront["Identity__ClientSecret"] == f"{{{PREFIX}-client-secret.value}}"
            assert storefront["Identity__Scopes__0"] == "email"
            assert resources[f"{PREFIX}-client-secret"]["inputs"]["value"]["secret"] is True
        else:
            assert not any(key.startswith("Identity__") for key in storefront)
            assert not any(name.startswith(PREFIX) for name in resources)
        assert not any(key.startswith("Identity__") for key in resources["back-office"].get("env", {}))
        print(f"PASS: Storefront OIDC enabled={enabled} and the secret stays deferred")
