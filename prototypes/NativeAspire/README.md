# Native Aspire prototype

This prototype connects implicit resource yields to native Aspire builders.
It uses Aspire 13.6.4 and FSharp.Aspire.Hosting.AppHost 13.0.0 for F# compatibility overloads.
The DSL implementation now lives in `src/AspiringFarmer` and `src/AspiringFarmer.Redis`.
The prototype consumes those libraries through a project reference marked `IsAspireProjectResource="false"`.

## Run

Start a Docker-compatible container engine, then run from the repository root:

```sh
dotnet run --project prototypes/NativeAspire
```

Aspire opens its CLI dashboard session. Stop the application with Ctrl+C.
The model contains one Redis container and two instances of the same small ASP.NET Core project, named `api` and `web`.
Both projects expose `/` and `/health`. This is a topology experiment, not a complete frontend and backend implementation.

The DSL example is in `Program.fs`. The sample yields only `web`; its references include `api` and the shared Redis description transitively.
Each project receives a Redis connection string, and `web` receives API service-discovery configuration.
The project configuration callback adds the endpoint, an environment value, and an HTTP health check using native Aspire APIs.

## Model checks

Build first, then run the executable directly to see assertion output without the Aspire CLI interface:

```sh
dotnet build prototypes/NativeAspire
dotnet prototypes/NativeAspire/bin/Debug/net10.0/NativeAspire.dll --verify
```

The checks compare native resource types, annotation kinds, endpoint settings, reference relationships, and readiness dependencies.
They also check shared Redis registration and successful application construction.
Additional checks verify independent registrations when reusing a description across applications, wait-only dependencies, and unused descriptions.
Aspire adds project rebuilder resources automatically; those resources participate in the comparison.

The `--native` option selects an equivalent AppHost written with direct Aspire builder calls:

```sh
dotnet run --project prototypes/NativeAspire -- --native
```

## Manifest comparison

Publish local manifests from both implementations:

```sh
dotnet prototypes/NativeAspire/bin/Debug/net10.0/NativeAspire.dll --publisher manifest --output-path /tmp/aspiringfarmer-dsl.json
dotnet prototypes/NativeAspire/bin/Debug/net10.0/NativeAspire.dll --native --publisher manifest --output-path /tmp/aspiringfarmer-native.json
```

Compare the parsed JSON structures rather than textual property order.
The comparison must include environment settings, connection expressions, endpoint bindings, and service-discovery expressions.

## Verified results

On November 6, 2026, using macOS arm64, .NET SDK 10.0.400, and OrbStack:

- The native model checks passed.
- The DSL and native manifests were structurally identical.
- Manifest assertions confirmed the native callback value, Redis connection injection, and API service-discovery configuration.
- Redis, `api`, and `web` reached ready state, in that order. Both projects had HTTP health checks.
- Ctrl+C stopped the AppHost session.

A separate `net11.0` build succeeded with SDK 11.0.100-rc.3.26522.148.
The native model and registry checks also passed on the .NET 11 RC runtime after extracting the libraries.
Use the `PrototypeTargetFramework=net11.0` MSBuild property from an environment selecting that SDK to reproduce the build.
The repository continues to select .NET 10 for ordinary development.

## Design findings and limits

The F# compatibility package supplies the overloads needed for native Redis references, project references, and waits.
No generated `Projects.*` metadata is needed because this prototype uses project paths.
Aspire still emits ASPIRE001 about incomplete F# code-generation support. That warning remains visible.

Descriptions retain concrete native resource types. A per-application registry caches native builders by description identity and rejects name conflicts.
The registry uses an internal boxed cache; description IDs determine the concrete type when retrieving a builder.
There is no reflection-based resource discovery or global mutable registration context.

Resource creation currently precedes recursive dependency resolution. Aspire supports declaring relationships after either resource exists.
Readiness follows explicit wait annotations, not the order resources appear in the model.

The outer expression currently returns `IDistributedApplicationBuilder`; the entry point calls `Build` and `Run` explicitly.
This makes model inspection convenient. Decide the final return type before stabilizing the public API.

Connection references now accept typed resources implementing `IResourceWithConnectionString`; service-discovery references currently cover projects only.
Wait operations require `IResourceWithWaitSupport`. The [compiler checks](../../tests/Compiler/README.md) verify these constraints and custom connection-resource factories.
Broader custom-resource and integration support need further work.
The registry validates during materialization; a failed description can leave a partially configured native builder.
Descriptions do not yet support a separate validation pass or rollback.

No build or startup performance budget has been measured. Full startup under .NET 11 and stable .NET 11 verification remain outstanding.
