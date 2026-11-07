# Performance benchmarks

BenchmarkDotNet measures runtime model construction, host construction, and managed allocations.
Python measures external build commands and fresh-process totals.
Both use the same native C#, native F#, and DSL model factories.

## Runtime benchmarks

From the repository root:

```sh
dotnet build benchmarks/Runtime/Runtime.csproj -c Release
dotnet benchmarks/Runtime/bin/Release/net10.0/Runtime.dll --verify
dotnet benchmarks/Runtime/bin/Release/net10.0/Runtime.dll --filter '*'
```

Use `--job Short` for a shorter exploratory run. Use `--job Dry` only to check that every case executes successfully.
Dry results have too few measurements for performance conclusions.
BenchmarkDotNet writes reports under `BenchmarkDotNet.Artifacts`, which Git ignores.

The suite uses BenchmarkDotNet 0.15.8 with `MemoryDiagnoser` at 5, 25, and 100 declared resources.
Each topology contains one Redis resource and projects that reference and wait for it.
Aspire-generated project rebuilder resources are included in every implementation.
The `--verify` command compares fingerprints across all factories before measurement.

`ConstructionBenchmarks` times Aspire builder creation and resource registration.
Its iteration cleanup builds and disposes the host outside the timed interval, preventing accumulation of unowned application builders.
`HostBuildBenchmarks` creates a fresh model in iteration setup, times only native `Build()`, and disposes the result in iteration cleanup.
Neither benchmark starts application resources or the dashboard.

Iteration lifecycle hooks force one invocation per iteration. These are lifecycle benchmarks, not throughput loops over reusable objects.
Short host-build iterations may trigger minimum-iteration-time warnings. Do not suppress those warnings or infer precision from short runs.
The allocation diagnoser excludes iteration setup and cleanup, and does not measure retained memory or native allocations.
See BenchmarkDotNet's [lifecycle documentation](https://benchmarkdotnet.org/articles/features/setup-and-cleanup.html) and [engine implementation](https://github.com/dotnet/BenchmarkDotNet/blob/v0.15.8/src/BenchmarkDotNet/Engines/Engine.cs).

## External workflow benchmarks

Build the solution, then run the external runner separately:

```sh
dotnet build AspiringFarmer.slnx -c Release
python3 benchmarks/run.py
```

The runner requires Python 3 and the stable .NET 10 SDK selected by the repository.
Docker is not required. Avoid other builds or compiler checks in this repository during measurement.
The runner temporarily appends comments to benchmark `Program` files, forces AppHost recompilation, and restores their original contents.

| Metric | Boundary |
| --- | --- |
| `freshProcessTotalMs` | Start an already compiled application with `dotnet`, create and build one model, report its fingerprint, dispose the host, and exit |
| `noChangeMs` | Restored Release build with existing outputs and unchanged source |
| `appHostEditMs` | Restored Release build after editing only the AppHost source |
| `rebuildGraphMs` | Restored Release build with `--no-incremental`, forcing compilation of the source dependency graph |

The runner records fresh-process totals at 5, 25, and 100 declared resources.
Fingerprints must match across implementations. Fingerprints inspect resource types, annotation kinds, endpoints, references, and waits; they do not inspect callback bodies.

Fresh-process totals include runtime initialization, fingerprinting, serialization, and disposal. They do not measure dashboard or application readiness.
Rebuilds retain restored packages and OS/compiler caches. They are not cold builds or restore measurements.
The main DSL benchmark uses source references to its libraries. The [package-consumer comparison](DslPackagedFSharp/README.md) measures the same source against locally packed dependencies separately.
The source uses a resource-count loop, so builds do not measure inference scaling across hundreds of separately written expressions.

The default run collects 5 fresh processes per implementation and topology size, and 5 samples per build scenario.
Execution order uses seed 42. Reports include raw samples, environment information, medians, and nearest-rank p95 values.
With 5 samples, p95 is the maximum observation and does not establish tail latency.
No-change reports also record rewritten DLLs, exposing unintended recompilation.

## Recorded findings

The [startup comparison](startup-2026-11-07.md) measures dashboard and resource readiness for the runnable sample.

The [warmed BenchmarkDotNet comparison](benchmarkdotnet-2026-11-06.md) records the first complete runtime suite with allocation diagnostics.
The [package-consumer results](package-consumers-2026-11-06.md) isolate library source-project costs in consumer builds.

The [initial baseline](baseline-2026-11-06.md) and its raw JSON remain historical exploratory results from the custom runtime harness.
They are not BenchmarkDotNet results. New runtime measurements should use BenchmarkDotNet; new external reports contain only process and build measurements.
See the [no-change investigation](no-change-builds.md) for build recommendations and caveats.
