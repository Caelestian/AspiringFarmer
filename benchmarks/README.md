# Performance baselines

These benchmarks compare equivalent native C#, native F#, and AspiringFarmer AppHosts.
They measure model construction and restored Release builds without starting the dashboard or any application resources.

## Run

From the repository root:

```sh
dotnet build AspiringFarmer.slnx -c Release
python3 benchmarks/run.py
```

The runner requires Python 3 and the stable .NET 10 SDK selected by the repository.
Docker is not required. Run without other builds or compiler checks in this repository.
The runner temporarily appends comments to benchmark `Program` files to force AppHost recompilation, then restores their original contents.

The default run measures 5, 25, and 100 declared resources.
Each topology contains one shared Redis resource and project resources that reference and wait for Redis.
Aspire also creates project rebuilder resources. Those generated resources are included in every implementation's model.

Models must have identical fingerprints across implementations before results are accepted.
Fingerprints compare resource names and types, annotation kinds, endpoints, reference relationships, and waits.
They do not inspect callback bodies or replace the native prototype's manifest comparison.

## Measurements

| Metric | Boundary |
| --- | --- |
| `constructionMs` | Create the Aspire application builder, describe the topology, and register all declared resources |
| `constructionBytes` | Managed allocations on the measuring thread during that construction interval |
| `hostBuildMs` | Native `IDistributedApplicationBuilder.Build()` after resources are registered |
| `freshProcessTotalMs` | Start an already compiled benchmark with `dotnet`, construct and build one model, dispose it, print its report, and exit |
| `noChangeMs` | Restored Release build with existing outputs and unchanged source |
| `appHostEditMs` | Restored Release build after editing only the benchmark's AppHost source |
| `rebuildGraphMs` | Restored Release build with `--no-incremental`, forcing compilation of the source dependency graph |

Full application startup and readiness are not measured here.
`freshProcessTotalMs` includes runtime startup, first-use initialization, model construction, host build, fingerprinting, serialization, and disposal.
It is not a dashboard readiness measurement.

Rebuilds retain restored packages and compiler/OS caches. They are not cold builds or restore measurements.
The DSL benchmark uses source project references to the core and Redis libraries.
The native baselines do not compile those libraries. This captures repository development costs; it does not represent published-package consumption.

Each benchmark uses a loop-based declaration with a runtime resource count.
Build measurements therefore do not measure compiler inference scaling across hundreds of separately written resource expressions.

## Sampling and interpretation

The default model run uses 3 independent process rounds per implementation and resource count.
Each process performs 3 unrecorded warmups followed by 15 recorded iterations, for 45 warm samples.
Each resource count also gets 5 fresh processes per implementation, with no warmup.
Each build scenario gets 5 samples per implementation.

The runner shuffles execution order using seed 42 and records raw samples, medians, and nearest-rank p95 values.
Future reports also record DLL timestamps around no-change samples so unintended recompilation remains visible.
With 5 samples, p95 is simply the largest observation. It is not a reliable tail-latency estimate.
Warm samples within a process are correlated, and a live workstation can introduce substantial scheduling and thermal noise.
Repeat a run before treating a small difference as a regression.

Reports include hardware, SDK/runtime information, package version, source revision, dirty working-tree status, and cache assumptions.
Do not compare runs from different machines as though their absolute times shared the same baseline.

The initial recorded run is summarized in [baseline-2026-11-06.md](baseline-2026-11-06.md).
