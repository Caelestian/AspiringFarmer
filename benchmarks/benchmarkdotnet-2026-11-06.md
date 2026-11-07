# Warmed BenchmarkDotNet comparison

The DSL's model-construction time is close to native F# in this run.
At 100 declared resources, the average difference was 1.66 ms, approximately 2.1%.
The reported error ranges overlap, so this run does not establish a consistent difference of that size.

## Measurement

Measured with BenchmarkDotNet 0.15.8, .NET SDK 10.0.400, .NET runtime 10.0.11, and an Apple M2 on macOS arm64.
The default warmed job completed all 18 cases in approximately 3 minutes.
Iteration setup and cleanup enforce one invocation per iteration because models and hosts have ownership and disposal requirements.
MemoryDiagnoser was enabled. No dashboard or application resources were started.

The [statistics and allocation report](results/2026-11-06-benchmarkdotnet.json) preserves the BenchmarkDotNet statistics, samples, outliers, and environment information.
Times in that JSON are nanoseconds. This run used base commit `8389d78` plus the uncommitted BenchmarkDotNet migration.
Full generated reports remain under the ignored `BenchmarkDotNet.Artifacts/comparison-2026-11-06` directory.

## Model construction

All times are BenchmarkDotNet means in milliseconds. Construction includes creating the native Aspire builder and registering the resource graph.

| Declared resources | Native C# | Native F# | DSL | DSL minus native F# |
| --- | ---: | ---: | ---: | ---: |
| 5 | 94.20 | 74.49 | 75.45 | +0.96 |
| 25 | 74.42 | 73.21 | 74.04 | +0.83 |
| 100 | 80.31 | 79.95 | 81.61 | +1.66 |

The 5-resource C# case had substantial variability, with a standard deviation of 16.68 ms.
Do not interpret that case as evidence that C# is slower than F#.
Case ordering and workstation activity can affect comparisons; another quiet-machine run is needed before setting regression thresholds.

At 100 resources, the 99.9% error half-width was 1.52 ms for native F# and 2.30 ms for the DSL.
Their medians were 79.21 ms and 79.89 ms respectively, a difference of 0.68 ms.

MemoryDiagnoser reported approximately 2,272.95 KiB per construction for native F# and 2,346.78 KiB for the DSL at 100 resources.
The additional allocation was approximately 73.83 KiB, or 3.2%.
Allocation counts do not establish retained memory or native memory use.

## Native host construction

The native `Build()` phase excludes resource registration and disposal.

| Declared resources | Native C# mean | Native F# mean | DSL mean |
| --- | ---: | ---: | ---: |
| 5 | 0.994 ms | 0.937 ms | 0.960 ms |
| 25 | 1.315 ms | 1.016 ms | 1.284 ms |
| 100 | 0.930 ms | 0.915 ms | 0.995 ms |

These short single-invocation iterations triggered BenchmarkDotNet's minimum-iteration-time warnings.
The DSL and native F# medians at 100 resources were 0.904 ms and 0.911 ms, while the means were 0.995 ms and 0.915 ms.
Do not claim a speed advantage or a precise small overhead from these noisy distributions.

## Practical implication

This run supports keeping runtime optimization below build-workflow investigation in priority.
It does not measure full AppHost startup, dashboard readiness, or application readiness.

The [earlier external build baseline](baseline-2026-11-06.md) recorded a 3.18-second DSL AppHost-edit build versus 2.54 seconds for native F#.
That comparison includes library source-project and tooling differences. Package-consumer measurements are the next step for isolating those costs.
