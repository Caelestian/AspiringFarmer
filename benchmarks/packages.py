"""Compare source-reference and local-package consumers of the same DSL AppHost."""
import argparse
import datetime
import hashlib
import json
from pathlib import Path
import random
import subprocess
import zipfile

import run as external

ROOT = external.ROOT
ARTIFACTS = ROOT / "artifacts/package-consumer"
NAMES = ["DslFSharp", "DslPackagedFSharp"]


def invoke(args):
    result = subprocess.run(args, cwd=ROOT, env=external.ENV, text=True, capture_output=True, timeout=180)
    if result.returncode:
        raise RuntimeError(result.stdout + result.stderr)
    return result.stdout


def verify_package_binaries(version):
    checks = {}
    for name in ["AspiringFarmer", "AspiringFarmer.Redis"]:
        with zipfile.ZipFile(ARTIFACTS / "feed" / f"{name}.{version}.nupkg") as package:
            packed = package.read(f"lib/net10.0/{name}.dll")
        built = (ROOT / f"src/{name}/bin/Release/net10.0/{name}.dll").read_bytes()
        if packed != built:
            raise RuntimeError(f"Packed and source binaries differ: {name}")
        checks[name] = hashlib.sha256(packed).hexdigest()
    return checks


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--samples", type=int, default=7)
    parser.add_argument("--output", type=Path, default=ROOT / "benchmarks/results/package-consumer-local.json")
    options = parser.parse_args()
    if options.samples < 1:
        parser.error("samples must be positive")
    ARTIFACTS.mkdir(parents=True, exist_ok=True)
    feed = ARTIFACTS / "feed"
    feed.mkdir(exist_ok=True)
    version = "0.0.0-benchmark." + datetime.datetime.now(datetime.timezone.utc).strftime("%Y%m%d%H%M%S%f")
    # Change NuGet identity without changing assembly identities in shared source outputs.
    invoke(["dotnet", "build", "benchmarks/DslFSharp/DslFSharp.fsproj", "-c", "Release", "--no-incremental"])
    for name in ["AspiringFarmer", "AspiringFarmer.Redis"]:
        invoke(["dotnet", "pack", f"src/{name}/{name}.fsproj", "-c", "Release", "--no-build", "--no-restore", "-p:PackageVersion=" + version, "--output", str(feed)])
    (ARTIFACTS / "Packages.props").write_text(
        f"<Project><PropertyGroup><BenchmarkPackageVersion>{version}</BenchmarkPackageVersion></PropertyGroup></Project>\n"
    )
    binary_checks = verify_package_binaries(version)
    print(f"Packed isolated local version {version}", flush=True)
    for name in NAMES:
        invoke(["dotnet", "restore", f"benchmarks/{name}/{name}.fsproj"])
        external.build(name)

    for count in [5, 25, 100]:
        fingerprints = {external.model(name, count)[0]["fingerprint"] for name in NAMES}
        if len(fingerprints) != 1:
            raise RuntimeError(f"Models differ at {count} resources")
    print("PASS: source and packaged consumers produce equivalent models at 5, 25, and 100 resources", flush=True)

    results = {
        "environment": {
            "sdk": invoke(["dotnet", "--version"]).strip(),
            "dotnetInfo": invoke(["dotnet", "--info"]),
            "revision": invoke(["git", "rev-parse", "HEAD"]).strip(),
            "status": invoke(["git", "status", "--short"]),
            "utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
            "packageVersion": version,
            "samples": options.samples,
            "configuration": "Release",
            "cache": "restored dependencies and warm outputs; package creation/restore excluded",
        },
        "builds": {name: {"noChangeMs": [], "appHostEditMs": [], "rebuildGraphMs": [], "noChangeOutputChanges": []} for name in NAMES},
        "binaryIdentityChecks": binary_checks,
    }
    rng = random.Random(42)
    source = ROOT / "benchmarks/DslFSharp/Program.fs"
    original = source.read_bytes()
    try:
        for scenario in ["noChangeMs", "appHostEditMs", "rebuildGraphMs"]:
            for trial in range(options.samples):
                order = NAMES.copy()
                rng.shuffle(order)
                for name in order:
                    if scenario == "appHostEditMs":
                        source.write_bytes(original + f"\n// Package-consumer edit {trial}\n".encode())
                    before = external.dll_stamps() if scenario == "noChangeMs" else None
                    elapsed = external.build(name, rebuild=scenario == "rebuildGraphMs")
                    results["builds"][name][scenario].append(elapsed)
                    if before is not None:
                        after = external.dll_stamps()
                        results["builds"][name]["noChangeOutputChanges"].append([path for path, stamp in before.items() if after.get(path) != stamp])
                    if scenario == "appHostEditMs":
                        source.write_bytes(original)
            print(f"Measured {scenario}", flush=True)
    finally:
        source.write_bytes(original)
        # Keep restored content newer than outputs after edit trials; never hide a required rebuild.

    for name in NAMES:
        raw = results["builds"][name]
        results["builds"][name] = {
            **{scenario: external.summary(raw[scenario]) for scenario in ["noChangeMs", "appHostEditMs", "rebuildGraphMs"]},
            "raw": raw,
        }

    # Profile a real AppHost-only edit after ensuring dependency outputs are current.
    for name in NAMES:
        external.build(name)
        try:
            source.write_bytes(original + b"\n// Binary-log profiling edit\n")
            log = invoke(["dotnet", "build", f"benchmarks/{name}/{name}.fsproj", "-c", "Release", "--no-restore", "-v:diagnostic", f"-bl:{ARTIFACTS / (name + '.binlog')}"])
            (ARTIFACTS / (name + ".build.log")).write_text(log)
        finally:
            source.write_bytes(original)
    options.output.parent.mkdir(parents=True, exist_ok=True)
    options.output.write_text(json.dumps(results, indent=2) + "\n")
    print(f"Saved {options.output}; binary logs are in {ARTIFACTS}", flush=True)


if __name__ == "__main__":
    main()
