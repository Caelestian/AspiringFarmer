# Reusable PostgreSQL topology

The `databaseTopology` function takes a name prefix and a secret parameter description. It returns typed database and migration descriptions. The AppHost calls it for `blue` and `green` and wires each API to its own database and migration.

Supply separate values for `Parameters:blue-password` and `Parameters:green-password` through Aspire configuration. The equivalent environment variable names are `Parameters__blue-password` and `Parameters__green-password`. Run from the repository root with a container engine available.

```sh
dotnet run --project prototypes/Composition
```

Each migration uses the database name passed in `MIGRATION_DATABASE` to select its injected connection string. The AppHost waits for each migration to succeed before starting its API. Each launch provisions temporary databases.

Check the two completion waits and the duplicate-prefix diagnostic without starting containers.

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
