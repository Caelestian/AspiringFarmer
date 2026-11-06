# AspiringFarmer
F# dsl for creating aspire apphosts. Inspired by Farmer https://compositionalit.github.io/farmer/

See the [project plan](PROJECT_PLAN.md) for the proposed DSL design, compatibility
strategy, performance targets, and implementation milestones.

## Planned compatibility

AspiringFarmer will support .NET 10 and .NET 11 AppHosts, with an initial
`net10.0` library target. .NET 11 compatibility is provisional until verified
with native Aspire hosting. Development uses the stable .NET 10 SDK.

## Prototypes

- [Implicit yield syntax](prototypes/ImplicitYield/README.md)
- [Native Aspire registration and runnable sample](prototypes/NativeAspire/README.md)
- [Performance benchmarks and initial results](benchmarks/README.md)

## Development

The initial [library implementation](src/README.md) includes the core DSL and an
optional Redis integration. Its API remains experimental.

Build the solution and run the capability compiler checks:

```sh
dotnet build AspiringFarmer.slnx
python3 tests/Compiler/check.py
```

Open the repository in a dev container to get the .NET 10 SDK, Ionide, Fantomas,
and FsAutoComplete. The container restores the local .NET tools when it is first
created.

Run the formatter from the repository root:

```sh
dotnet fantomas .
```
