# AppHost components

An `appDefinition` collects resource declarations for later registration. Build a definition on its own with `AppHost.build args`, or include it as a bare expression in a parent `distributedApp`.

```fsharp
let app = distributedApp {
    withArgs args
    catalogDefinition
    billing catalogEndpointOwner
}
```

The parent creates one Aspire builder and registry. Definitions share the parent's resource names, configuration, and lifecycle. Billing uses the catalog's typed container description to wait for it and resolve its HTTP endpoint. The registry registers the catalog once even though billing also references it.

The sample offers three launch modes from one executable. In standalone mode, billing supplies its own local catalog. In combined mode, billing uses the catalog exported by the other component.

```sh
dotnet run --project prototypes/Components -- --catalog
dotnet run --project prototypes/Components -- --billing
dotnet run --project prototypes/Components
```

Stop each run before starting the next. Billing's health route requests the injected catalog URL, so healthy status confirms the HTTP call worked.

Use unique resource names across the parent. When reusing a definition from another directory, pass absolute project paths derived from that definition's source directory. Aspire resolves relative project paths against the AppHost that registers them.

Build and verify the component models and duplicate-name diagnostic without running containers.

```sh
dotnet build prototypes/Components
dotnet prototypes/Components/bin/Debug/net10.0/Components.dll --verify
```
