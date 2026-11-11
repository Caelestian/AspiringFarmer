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
    for enabled in (None, "false", "true", "invalid"):
        label = enabled if enabled is not None else "default"
        manifest = Path(directory) / f"{label}.json"
        env = dict(os.environ, Identity__Scopes__0="email", Mail__Transport="disabled")
        env.pop("Identity__Enabled", None)
        env.pop("Identity:Enabled", None)
        if enabled is not None:
            env["Identity__Enabled"] = enabled
        env[f"Parameters__{PREFIX}-client-secret"] = CANARY
        result = subprocess.run(
            ["dotnet", str(APPHOST), "--publisher", "manifest", "--output-path", str(manifest)],
            cwd=ROOT, env=env, capture_output=True, text=True, timeout=60,
        )
        if enabled == "invalid":
            assert result.returncode != 0
            assert "Configuration 'Identity:Enabled' must be true or false." in result.stderr
            assert not manifest.exists()
            print("PASS: malformed Identity:Enabled is rejected")
            continue
        if result.returncode:
            raise RuntimeError(result.stdout + result.stderr)
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
        print(f"PASS: Storefront OIDC enabled={label} and the secret stays deferred")
