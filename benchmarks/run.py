"""Measure restored Release builds and equivalent Aspire application models."""
import argparse
import json
import math
import os
from pathlib import Path
import platform
import random
import statistics
import subprocess
import time

ROOT = Path(__file__).resolve().parent.parent
NAMES = ["NativeCSharp", "NativeFSharp", "DslFSharp"]
ENV = dict(os.environ, BENCH_SERVICE_PROJECT=str(ROOT / "prototypes/Service/Service.fsproj"))


def command(args, **kwargs):
    result = subprocess.run(args, cwd=ROOT, env=ENV, capture_output=True, text=True, timeout=120, **kwargs)
    if result.returncode:
        raise RuntimeError(result.stdout + result.stderr)
    return result.stdout


def summary(values):
    ordered = sorted(values)
    return {
        "n": len(values),
        "median": statistics.median(values),
        "p95": ordered[math.ceil(len(values) * 0.95) - 1],
        "min": ordered[0],
        "max": ordered[-1],
    }


def model(name, count, iterations):
    dll = ROOT / f"benchmarks/{name}/bin/Release/net10.0/{name}.dll"
    started = time.perf_counter()
    report = json.loads(command(["dotnet", str(dll), str(count), str(iterations)]))
    return report, (time.perf_counter() - started) * 1000


def build(name, rebuild=False):
    extension = "csproj" if name == "NativeCSharp" else "fsproj"
    project = ROOT / f"benchmarks/{name}/{name}.{extension}"
    args = ["dotnet", "build", str(project), "-c", "Release", "--no-restore", "--verbosity", "quiet"]
    if rebuild:
        args.append("--no-incremental")
    started = time.perf_counter()
    command(args)
    return (time.perf_counter() - started) * 1000


def dll_stamps():
    return {
        str(path.relative_to(ROOT)): path.stat().st_mtime_ns
        for base in [ROOT / "src", ROOT / "benchmarks"]
        for path in base.rglob("bin/Release/net10.0/*.dll")
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--iterations", type=int, default=15)
    parser.add_argument("--rounds", type=int, default=3)
    parser.add_argument("--fresh-processes", type=int, default=5)
    parser.add_argument("--build-samples", type=int, default=5)
    parser.add_argument("--output", type=Path, default=ROOT / "benchmarks/results/local.json")
    options = parser.parse_args()
    if min(options.iterations, options.rounds, options.fresh_processes, options.build_samples) < 1:
        parser.error("sample counts must be positive")

    rng = random.Random(42)
    results = {
        "environment": {
            "platform": platform.platform(),
            "machine": platform.machine(),
            "processor": command(["sysctl", "-n", "machdep.cpu.brand_string"]).strip() if platform.system() == "Darwin" else platform.processor(),
            "logicalCpus": os.cpu_count(),
            "sdk": command(["dotnet", "--version"]).strip(),
            "dotnetInfo": command(["dotnet", "--info"]),
            "aspire": "13.6.4",
            "gitRevision": command(["git", "rev-parse", "HEAD"]).strip(),
            "gitStatus": command(["git", "status", "--short"]),
            "utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
            "configuration": "Release",
            "cache": "restored packages, existing build outputs; OS caches not cleared",
            "arguments": vars(options) | {"output": str(options.output)},
        },
        "models": {},
        "builds": {},
    }

    for count in [5, 25, 100]:
        samples = {name: [] for name in NAMES}
        cold = {name: [] for name in NAMES}
        fingerprints = set()
        for _ in range(options.rounds):
            order = NAMES.copy()
            rng.shuffle(order)
            for name in order:
                report, _ = model(name, count, options.iterations)
                fingerprints.add(report["fingerprint"])
                samples[name].extend(report["samples"])
        for _ in range(options.fresh_processes):
            order = NAMES.copy()
            rng.shuffle(order)
            for name in order:
                report, elapsed = model(name, count, 1)
                fingerprints.add(report["fingerprint"])
                cold[name].append(elapsed)
        if len(fingerprints) != 1:
            raise RuntimeError(f"Model fingerprints differ at {count} declared resources")
        results["models"][count] = {
            name: {
                "constructionMs": summary([sample["constructionMs"] for sample in samples[name]]),
                "constructionBytes": summary([sample["constructionBytes"] for sample in samples[name]]),
                "hostBuildMs": summary([sample["buildMs"] for sample in samples[name]]),
                "freshProcessTotalMs": summary(cold[name]),
                "raw": samples[name],
                "rawFreshProcessTotalMs": cold[name],
            }
            for name in NAMES
        }
        print(f"PASS: equivalent models measured at {count} declared resources", flush=True)

    # Builds are measured after model runs, and edits are restored even on failure.
    for name in NAMES:
        build(name)
    raw = {name: {"noChangeMs": [], "appHostEditMs": [], "rebuildGraphMs": []} for name in NAMES}
    no_change_outputs = {name: [] for name in NAMES}
    for _ in range(options.build_samples):
        order = NAMES.copy()
        rng.shuffle(order)
        for name in order:
            before = dll_stamps()
            raw[name]["noChangeMs"].append(build(name))
            after = dll_stamps()
            no_change_outputs[name].append([path for path, stamp in before.items() if after.get(path) != stamp])
    print("Measured no-change builds and checked rewritten DLLs", flush=True)
    for trial in range(options.build_samples):
        order = NAMES.copy()
        rng.shuffle(order)
        for name in order:
            source = ROOT / f"benchmarks/{name}/Program.{ 'cs' if name == 'NativeCSharp' else 'fs' }"
            original = source.read_bytes()
            try:
                source.write_bytes(original + f"\n// Benchmark edit {trial}\n".encode())
                raw[name]["appHostEditMs"].append(build(name))
            finally:
                source.write_bytes(original)
    print("Measured AppHost-only edits and restored source files", flush=True)
    for _ in range(options.build_samples):
        order = NAMES.copy()
        rng.shuffle(order)
        for name in order:
            raw[name]["rebuildGraphMs"].append(build(name, rebuild=True))
    print("Measured full source-graph rebuilds", flush=True)
    for name in NAMES:
        results["builds"][name] = {key: summary(values) for key, values in raw[name].items()}
        results["builds"][name]["raw"] = raw[name]
        results["builds"][name]["noChangeOutputChanges"] = no_change_outputs[name]
    options.output.parent.mkdir(parents=True, exist_ok=True)
    options.output.write_text(json.dumps(results, indent=2) + "\n")
    print(f"Saved {options.output}", flush=True)


if __name__ == "__main__":
    main()
