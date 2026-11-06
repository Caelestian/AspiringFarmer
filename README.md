# AspiringFarmer
F# dsl for creating aspire apphosts. Inspired by Farmer https://compositionalit.github.io/farmer/

See the [project plan](PROJECT_PLAN.md) for the proposed DSL design, compatibility
strategy, performance targets, and implementation milestones.

## Planned compatibility

AspiringFarmer will support .NET 10 and .NET 11 AppHosts, with an initial
`net10.0` library target. .NET 11 compatibility is provisional until verified
with native Aspire hosting. Development uses the stable .NET 10 SDK.

## Development

Open the repository in a dev container to get the .NET 10 SDK, Ionide, Fantomas,
and FsAutoComplete. The container restores the local .NET tools when it is first
created.

Run the formatter from the repository root:

```sh
dotnet fantomas .
```
