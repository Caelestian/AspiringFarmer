# Package-consumer comparison

This AppHost compiles the same linked `Program.fs` as the source-reference DSL benchmark.
It references a locally packed AspiringFarmer.Redis package instead of the core and Redis source projects.
Both consumers retain the same benchmark-support project reference.

From the repository root:

```sh
python3 benchmarks/packages.py --samples 7 --output benchmarks/results/package-consumer-local.json
```

The runner builds the source libraries, packs unpublished local packages, and restores the consumer.
It gives each run a unique prerelease package version while preserving assembly identities.
The local feed and generated version properties live under ignored `artifacts/package-consumer`.
Nothing is pushed to a remote package feed.

The runner verifies equivalent models at 5, 25, and 100 resources, then interleaves builds of both consumers.
It measures unchanged builds, AppHost-only edits, and full source-graph rebuilds.
Restoring and creating packages are excluded from the timed build samples.
DLL timestamp checks detect unexpected output rewrites during unchanged builds.

AppHost-edit trials modify the shared benchmark source, then restore its original bytes.
Run this experiment separately from other repository builds and runtime benchmarks.
Diagnostic text logs and MSBuild binary logs capture a separate AppHost-edit build for each consumer under `artifacts/package-consumer`.
Profiling builds are excluded from timing samples. They support investigation, not direct CPU-time attribution.

This project is intentionally outside the default solution because its local packages must be created first.
Its SDK and hosting package versions must remain aligned with the other benchmark AppHosts.
It is a benchmark consumer, not a shipping application template.
