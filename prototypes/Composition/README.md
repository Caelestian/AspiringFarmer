# Reusable PostgreSQL topology

The `databaseTopology` function declares local names for its secret, PostgreSQL server, database, and migration. The AppHost applies the prefix once with `AppHost.instantiate`.

```fsharp
let blue = AppHost.instantiate "blue" databaseTopology
let green = AppHost.instantiate "green" databaseTopology
```

Include each instance's `Definition` in the parent. Within the topology, `scope.Export` binds the typed database and migration descriptions to their owner. Consumers use `blue.Exports.Database` and `blue.Exports.Migration` without constructing names.

Use `scope.Instantiate` at a nested component boundary. A child called `storage` inside `blue` prefixes its resources with `blue-storage-`. An exported description retains that owner when a consumer resolves it, including when the consumer appears first in the parent.

Custom factories using the registry-aware `ResourceBuilder` constructor should pass `registry.QualifyName name` to their native resource factory. Keep configuration keys and aliases explicit.

Supply separate values for `Parameters:blue-password` and `Parameters:green-password` through Aspire configuration. The equivalent environment variable names are `Parameters__blue-password` and `Parameters__green-password`. Run from the repository root with a container engine available.

```sh
dotnet run --project prototypes/Composition
```

Each migration uses `reference_as "appdb" db` to keep its connection string key stable across instances. The AppHost waits for each migration to succeed before starting its API. Each launch provisions temporary databases.

Check the completion waits, nested exports, and duplicate-prefix diagnostic without starting containers.

```sh
dotnet build prototypes/Composition
dotnet prototypes/Composition/bin/Debug/net10.0/Composition.dll --verify
```

Publish both models into the same directory to compare their project paths and deferred references.

```sh
dotnet prototypes/Composition/bin/Debug/net10.0/Composition.dll --publisher manifest --output-path /tmp/composition-dsl.json
dotnet prototypes/Composition/bin/Debug/net10.0/Composition.dll --native --publisher manifest --output-path /tmp/composition-native.json
```

Compare generated `annotated.string` aliases by their filter and referenced value. Aspire can emit multiple aliases for the same URI-encoded password expression. The manifest supplies `ConnectionStrings__blue-db` to `blue-api` and `ConnectionStrings__green-db` to `green-api`.

## Mermaid export

Generate a Mermaid flowchart from the AppHost model. Paste the output into a `mermaid` block in Markdown.

```sh
dotnet prototypes/Composition/bin/Debug/net10.0/Composition.dll --mermaid
```

The export shows resource names, references, parents, and wait conditions. It shows secret parameter names and keeps their values deferred. See the [generated topology](topology.md).

## Publish to Docker Compose

Build the AppHost before publishing. Run the publish command from the repository root.

```sh
dotnet build prototypes/Composition
aspire publish --apphost prototypes/Composition/Composition.fsproj --no-build --output-path /tmp/composition-compose -- --compose
docker compose -f /tmp/composition-compose/docker-compose.yaml config --no-interpolate --quiet
```

The publish command writes `docker-compose.yaml` and an `.env` template. Supply image names for the API and migration services, API container ports, and both passwords before starting the stack.

The Compose configuration sets `POSTGRES_DB` for each server. Each migration waits for its server's TCP health check. Aspire emits `service_completed_successfully` for each API's migration dependency. Each branch uses its own database host and password input.

Docker Compose validated the generated configuration and both dependency chains. Runtime verification covered the local Aspire sample in the earlier composition experiment.
