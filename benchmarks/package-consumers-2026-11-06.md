# Package-consumer build comparison

Using local NuGet packages reduced the measured consumer build times, with the largest saving during full graph rebuilds.
The unchanged-build improvement was modest. It does not eliminate the approximately one-second build-command cost on this machine.

## Results

Seven interleaved samples per scenario, Release configuration, .NET SDK 10.0.400, on the same Apple M2 workstation as the earlier runs.
Dependencies and outputs were warm. Package creation and restore were excluded from timing.

| Scenario | Library source references | Local package references | Median reduction |
| --- | ---: | ---: | ---: |
| No changes | 1.167 s | 1.081 s | 86 ms, 7.4% |
| AppHost-only edit | 2.649 s | 2.308 s | 341 ms, 12.9% |
| Rebuild consumer graph | 8.871 s | 4.136 s | 4.736 s, 53.4% |

These are median observations from a live workstation. Incremental build ranges overlapped, and the package AppHost-edit sample was slower in 3 of the 7 paired trials.
Repeat the run before enforcing a small incremental-build performance threshold.
The full graph rebuild ranges did not overlap in this run.

The [raw samples](results/2026-11-06-package-consumers.json) record environment, version, cache assumptions, and rewritten DLL checks.
Both consumers rewrote no DLLs in all 7 unchanged-build trials.

## What changed

Both consumers compile the same linked `Program.fs`, with the same Aspire SDK and hosting version.
Both retain the benchmark-support source project.
The package consumer removes only the core and Redis source projects from its graph.
Its graph therefore contains the AppHost and support project instead of those two projects plus the core and Redis projects.

The packages are local and unpublished. Unique `PackageVersion` values avoid reusing a stale package-cache entry without changing assembly identity.
SHA-256 checks confirmed that packaged DLLs matched the source-output DLLs byte for byte.
The resulting native Aspire models matched at 5, 25, and 100 resources.

## Profile findings

Separate diagnostic AppHost-edit builds produced these task and target observations:

| Work | Source references | Package references |
| --- | ---: | ---: |
| F# compiler task | 1 call, 1,836 ms | 1 call, 1,800 ms |
| Resolve project references | 4 calls, 1,017 ms | 2 calls, 235 ms |
| Resolve assembly references | 4 calls, 258 ms | 2 calls, 90 ms |

The F# compiler took similar time in both profiles. Fewer project and assembly resolution calls support investigating graph overhead before changing DSL syntax.
Diagnostic logging changes build cost, and nested durations overlap. Do not add these values together or equate their differences with quiet-build wall-time savings.

The [profile summary and binary checks](results/2026-11-06-package-profiles.json) preserve the extracted observations.
Full binary and text logs remain under ignored `artifacts/package-consumer`.

## Recommended workflow

Use package references for normal application consumers once packages are available. Keep source references when developing AspiringFarmer itself.
Build the AppHost project with `--no-restore` after dependencies are restored.
When outputs are current and no rebuild is required, restarting with `--no-build` removes the build phase entirely.

This experiment does not measure service builds, dashboard readiness, or full application startup.
It also does not establish a package-consumer comparison against native F#; that needs its own paired baseline.

Reproduce the experiment with [the package-consumer runner](DslPackagedFSharp/README.md).
