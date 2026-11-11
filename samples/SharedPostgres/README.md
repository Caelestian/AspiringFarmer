# Shared PostgreSQL AppHost

The [AppHost](AppHost/Program.fs) declares one PostgreSQL server and five databases. Each database has one F# console project that applies its schema and seeds one row in a transaction. Each web app waits for its three schema jobs to exit successfully.

| Web app | Databases and schema jobs |
| --- | --- |
| Storefront | Identity, Catalog, Orders |
| Back Office | Identity, Inventory, Reporting |

Both apps share the Identity database and wait on the same Identity schema job. Each app's `/health` endpoint queries the seeded row in all three of its databases.

The schema jobs read their connection string from `ConnectionStrings__appdb`. The normal AppHost uses a data volume and saves the generated PostgreSQL password in .NET user secrets. The schema and seed SQL can run again against an existing volume.

## Run the sample

Use the .NET 10 SDK, the Aspire CLI, and a running Docker engine. Run these commands from the repository root.

```sh
dotnet build samples/SharedPostgres/SharedPostgres.slnx
dotnet run --project samples/SharedPostgres/AppHost --no-build
```

Open the Aspire dashboard from the terminal output. The five schema jobs finish and the two web apps become healthy. Use the dashboard's endpoint links to open the web apps.

The sample was verified with two successive launches against the same PostgreSQL volume. Both apps passed their health checks on the second launch.

## Benchmark

Run the benchmark with Python 3.9 or later and a running Docker engine.

```sh
python3 samples/SharedPostgres/benchmark.py --runs 5
```

The benchmark restores packages and performs one untimed build and startup. Each measured run cleans the solution, times a Debug build, then times an immediate no-change build. Both build timings exclude package restore.

The benchmark launches the compiled AppHost in the Development environment and measures wall-clock time from process launch. AppHost startup ends when Aspire's `StartAsync` returns. Resource readiness ends when Aspire confirms the following conditions.

- PostgreSQL and its five database resources are ready.
- All five schema jobs exited with code 0.
- Both web apps passed their database-backed HTTP health checks.

Each startup uses fresh PostgreSQL storage and a cached image. The AppHost stops after the readiness check. The benchmark writes individual measurements, resource timings, and medians to `artifacts/shared-postgres.json`.

### Measured results

Five runs on November 10, 2026 used an Apple M2 with 16 GB of memory, macOS 27.2, and .NET SDK 10.0.400. The AppHost used Aspire 13.6.4 and the local Aspire 13.5.0 CLI bundle. OrbStack ran the cached arm64 `postgres:18.3` image.

| Milestone | Median | Range |
| --- | ---: | ---: |
| Clean solution build | 5.31 s | 4.94–6.97 s |
| No-change solution build | 0.82 s | 0.79–0.93 s |
| AppHost started | 9.80 s | 9.60–10.10 s |
| All 13 application resources ready | 12.22 s | 11.72–12.32 s |

Both runtime timings start at the same process launch. See the [original five runs with native configuration callbacks](../../benchmarks/results/2026-11-10-shared-postgres.json).

After both apps switched to `http_endpoint` and `http_health_check`, a second five-run pass recorded medians of 9.78 s for AppHost startup and 12.65 s for resource readiness. All 13 resources reached readiness in every run. See the [DSL run data and individual resource timings](../../benchmarks/results/2026-11-10-shared-postgres-http-dsl.json).
