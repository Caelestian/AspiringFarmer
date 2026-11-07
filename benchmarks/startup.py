"""Measure the native sample through Aspire's CLI without rebuilding the AppHost."""
import argparse
import json
import os
from pathlib import Path
import queue
import random
import re
import signal
import ssl
import statistics
import subprocess
import threading
import time
from urllib.parse import urlsplit
from urllib.request import urlopen

ROOT = Path(__file__).resolve().parents[1]
RESOURCES = {"cache", "api", "web"}
ANSI = re.compile(r"\x1b\[[0-?]*[ -/]*[@-~]")
READY = re.compile(r"Resource '(cache|api|web)' is ready\.")
TLS = ssl._create_unverified_context()  # Local development dashboard only.


def measure(mode):
    args = ["dotnet", "run", "--project", "prototypes/NativeAspire", "-c", "Debug", "--no-build"]
    if mode == "native":
        args += ["--", "--native"]
    lines = queue.Queue()
    start = time.monotonic()
    process = subprocess.Popen(args, cwd=ROOT, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                               text=True, start_new_session=True)

    def read_output():
        for line in process.stdout:
            lines.put(ANSI.sub("", line))

    reader = threading.Thread(target=read_output, daemon=True)
    reader.start()
    dashboard = log_path = log = dashboard_ms = None
    ready = {}
    container = None
    prebuilt_debug = False
    try:
        while time.monotonic() - start < 90:
            if process.poll() is not None:
                raise RuntimeError(f"{mode} exited before readiness ({process.returncode}); log: {log_path}")
            while True:
                try:
                    line = lines.get_nowait()
                except queue.Empty:
                    break
                match = re.search(r"Dashboard:\s+(https?://[^\s]+)", line)
                if match:
                    url = urlsplit(match[1])
                    if url.hostname not in {"localhost", "127.0.0.1", "::1"}:
                        raise RuntimeError("Expected a loopback dashboard URL")
                    dashboard = f"{url.scheme}://{url.netloc}/"
                match = re.search(r"Logs:\s+(.+?\.log)\s*$", line)
                if match:
                    log_path = Path(match[1])
            if dashboard and dashboard_ms is None:
                try:
                    with urlopen(dashboard, context=TLS, timeout=0.25) as response:
                        if response.status == 200:
                            dashboard_ms = (time.monotonic() - start) * 1000
                except OSError:
                    pass
            if log is None and log_path and log_path.exists():
                log = log_path.open()
            if log:
                for line in log:
                    if match := re.search(r"Resource cache/(cache-[a-z0-9]+) changed state", line):
                        container = match[1]
                    if "bin/Debug/net10.0/NativeAspire(" in line and " started in " in line:
                        prebuilt_debug = True
                    if match := READY.search(line):
                        ready.setdefault(match[1], (time.monotonic() - start) * 1000)
            if dashboard_ms is not None and RESOURCES == ready.keys():
                if not prebuilt_debug or container is None:
                    raise RuntimeError(f"Could not verify AppHost configuration or Redis identity in {log_path}")
                return {"dashboardMs": dashboard_ms, "resourcesMs": ready,
                        "allHealthyMs": max(ready.values())}
            time.sleep(0.05)
        raise TimeoutError(f"{mode} readiness timed out; log: {log_path}; observed: {sorted(ready)}")
    finally:
        if log:
            log.close()
        if process.poll() is None:
            os.killpg(process.pid, signal.SIGINT)
            try:
                process.wait(timeout=20)
            except subprocess.TimeoutExpired:
                os.killpg(process.pid, signal.SIGKILL)
                process.wait()
                raise RuntimeError(f"{mode} failed to shut down cleanly")
        reader.join(timeout=1)
        process.stdout.close()
        if container:
            deadline = time.monotonic() + 30
            while time.monotonic() < deadline:
                running = subprocess.check_output(
                    ["docker", "ps", "--filter", f"name={container}", "--format", "{{.Names}}"],
                    text=True, timeout=5).strip()
                if container not in running.splitlines():
                    break
                time.sleep(0.2)
            else:
                raise RuntimeError(f"Redis container {container} still running after shutdown")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--pairs", type=int, default=5)
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/startup.json")
    options = parser.parse_args()
    if options.pairs < 1:
        parser.error("pairs must be positive")
    subprocess.run(["docker", "image", "inspect", "redis:8.6", "--format", "{{.Id}}"],
                   check=True, stdout=subprocess.DEVNULL)
    samples = {"dsl": [], "native": []}
    rng = random.Random(42)
    for pair in range(options.pairs):
        order = list(samples)
        rng.shuffle(order)
        for mode in order:
            sample = measure(mode)
            samples[mode].append(sample)
            print(f"{pair + 1} {mode}: dashboard {sample['dashboardMs']:.0f} ms, healthy {sample['allHealthyMs']:.0f} ms", flush=True)
    report = {"samples": samples, "medianMs": {
        mode: {key: statistics.median(sample[key] for sample in runs)
               for key in ["dashboardMs", "allHealthyMs"]}
        for mode, runs in samples.items()}}
    options.output.parent.mkdir(parents=True, exist_ok=True)
    options.output.write_text(json.dumps(report, indent=2) + "\n")


if __name__ == "__main__":
    main()
