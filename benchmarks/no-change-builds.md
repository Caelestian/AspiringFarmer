# Improving unchanged AppHost workflows

The DSL's measured no-change builds rewrite no DLLs. Remaining time includes the .NET CLI, MSBuild evaluation, project traversal, and build targets.
The source-project graph took about 1.25 seconds in the initial baseline.

## Immediate workflow choices

Build only the AppHost project when the application does not require a full solution build.
After restoring dependencies, use `dotnet build --no-restore` for repeated builds. The existing benchmark already uses that option.

When both the AppHost and its required outputs are current, use `dotnet run --no-build` to avoid the build phase.
Rebuild after changing source, project configuration, or dependencies. Do not make skipped builds the unconditional development default.

Package references remove the library source projects from the consumer's build graph.
The [local package experiment](package-consumers-2026-11-06.md) reduced unchanged-build medians from 1.167 s to 1.081 s, with both consumers rewriting no DLLs.
That modest local result supports measuring package consumers separately; it does not guarantee the same saving on another machine.

## Follow-up build-option experiment

A local follow-up compared 5 interleaved samples per mode after warming each mode, using the DSL Release benchmark project and .NET SDK 10.0.400.

| Option | Median |
| --- | ---: |
| `dotnet build --no-restore` | 1,233 ms |
| Same command with `-graphBuild` | 1,378 ms |
| Same command with `--no-dependencies` | 998 ms |

This small live-workstation run did not show a benefit from static-graph builds. Do not enable them as a performance default from this evidence.
Skipping dependency builds reduced the median by about 235 ms, which suggests project traversal contributes to the total.
It does not isolate traversal cost, and `--no-dependencies` can leave changed dependencies stale.
Use it only in a workflow that separately guarantees current dependency outputs.

## Native F# metadata generation

The native F# baseline recompiles because generated `_AppHost.ProjectMetadata.g.fs` is newer than its intermediate assembly.
That behavior is recorded in the [baseline](baseline-2026-11-06.md).
The DSL library excludes compatibility-package build assets and the sample uses project paths, so this metadata generation is not required there.

Hosts using generated `Projects.*` metadata still need the corresponding build targets.
Investigate stable generator outputs upstream rather than removing generation from those hosts.
Do not edit the shared NuGet cache to apply a fix.

Runtime model construction is not the priority for no-change build performance.
Focus first on the consumer project graph and unnecessary generation or compilation.
