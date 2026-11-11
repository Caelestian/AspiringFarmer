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

### Run the published sample locally

Publish the two projects and build their local images from the repository root.

```sh
output=/tmp/composition-compose
dotnet publish prototypes/Service -c Release -o "$output/service"
dotnet publish prototypes/PostgreSQL.Migration -c Release -o "$output/migration"

docker build -t aspiringfarmer-compose-service:local -f - "$output/service" <<'EOF'
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY . .
ENTRYPOINT ["dotnet", "Service.dll"]
EOF

docker build -t aspiringfarmer-compose-migration:local -f - "$output/migration" <<'EOF'
FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY . .
ENTRYPOINT ["dotnet", "PostgreSQL.Migration.dll"]
EOF
```

Set the image names, container ports, and temporary passwords in your shell.
Use an override to assign each API a free loopback port.

```sh
cd "$output"
export BLUE_API_IMAGE=aspiringfarmer-compose-service:local GREEN_API_IMAGE=aspiringfarmer-compose-service:local
export BLUE_MIGRATION_IMAGE=aspiringfarmer-compose-migration:local GREEN_MIGRATION_IMAGE=aspiringfarmer-compose-migration:local
export BLUE_API_PORT=8080 GREEN_API_PORT=8080
export BLUE_PASSWORD="$(openssl rand -hex 24)"
export GREEN_PASSWORD="$(openssl rand -hex 24)"

cat > ports.yaml <<'EOF'
services:
  blue-api:
    ports: ["127.0.0.1::8080"]
  green-api:
    ports: ["127.0.0.1::8080"]
EOF

compose() { docker compose -f docker-compose.yaml -f ports.yaml "$@"; }
compose -p composition-success up -d
compose -p composition-success ps -a
curl --fail --retry 5 --retry-connrefused "http://$(compose -p composition-success port blue-api 8080)/health"
curl --fail --retry 5 --retry-connrefused "http://$(compose -p composition-success port green-api 8080)/health"
compose -p composition-success down -v
```

Both migrations exited with code 0 and created `migration_example` in their own databases. Each API started after its migration finished and returned HTTP 200.

### Check a failed migration

Use a fresh Compose project and pass `--fail` to the blue migration.

```sh
cat > failure.yaml <<'EOF'
services:
  blue-migration:
    command: ["--fail"]
EOF

compose -p composition-failure -f failure.yaml up -d
compose -p composition-failure -f failure.yaml ps -a
compose -p composition-failure -f failure.yaml logs blue-migration
curl --fail --retry 5 --retry-connrefused "http://$(compose -p composition-failure -f failure.yaml port green-api 8080)/health"
compose -p composition-failure -f failure.yaml down -v
```

In the Docker Compose v5.5.0 run, `up` returned exit code 1. The blue migration reported SQLSTATE `42P01`, and the blue API stayed in the `created` state. The green migration created its table, and the green API returned HTTP 200.
