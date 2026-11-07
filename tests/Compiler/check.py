"""Compile real consumer fixtures and require capability-specific diagnostics."""
from pathlib import Path
import subprocess
import sys

project = Path(__file__).with_name("Compiler.fsproj")
cases = {
    "Valid": None,
    # F# reports a missing CE translation member for an unknown operation.
    "InvalidVolume": "error FS0708",
    "InvalidWait": "IResourceWithWaitSupport",
    "InvalidReference": "IResourceWithConnectionString",
    "InvalidDatabaseParent": "PostgresServerResource",
    "InvalidSqlServerParent": "SqlServerServerResource",
    "InvalidCompletionWait": "ProjectResource",
    "InvalidCallback": "ProjectResource",
}

for case, expected in cases.items():
    result = subprocess.run(
        ["dotnet", "build", str(project), "--no-restore", f"-p:Case={case}", "--verbosity", "quiet"],
        capture_output=True,
        text=True,
        timeout=120,
    )
    output = result.stdout + result.stderr
    if expected is None:
        passed = result.returncode == 0
    else:
        passed = result.returncode != 0 and "error FS" in output and expected in output
    if not passed:
        print(output)
        sys.exit(f"FAIL: {case}")
    print(f"PASS: {case}")
