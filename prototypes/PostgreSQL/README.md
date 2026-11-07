# PostgreSQL sample

The optional `AspiringFarmer.PostgreSQL` library provides typed server and database descriptions. This sample yields the API, which references `appdb`. Resolving the database registers its PostgreSQL parent through the same application registry.

The migration project uses `wait_for_child appdb` to wait for Aspire's database health checks. It creates the `migration_example` table using the connection string Aspire injects for `appdb`. The API uses `wait_for_completion migration` to wait for the migration project to exit with code zero.

Each launch creates a temporary database. The migration uses `CREATE TABLE IF NOT EXISTS` so the command can be run again against the same database.

The AppHost declares the password with `secretParameter "postgres-password"` and passes its description to `postgresWithPassword`. Supply the value through Aspire configuration under `Parameters:postgres-password`, or through the `Parameters__postgres-password` environment variable. The server connection string references the secret parameter, and the database connection string references the server connection string.

Run from the repository root with a container engine available.

```sh
dotnet run --project prototypes/PostgreSQL
```

Stop the successful run before trying the failure scenario.

```sh
dotnet run --project prototypes/PostgreSQL -- --fail-migration
```

The flag sends the migration project a query for a nonexistent table. Check the Aspire dashboard or logs for the failed migration and the API startup failure. Completion waits apply to local development.

To compare resource types and relationships with direct Aspire declarations without starting containers, run these commands.

```sh
dotnet build prototypes/PostgreSQL
dotnet prototypes/PostgreSQL/bin/Debug/net10.0/PostgreSQL.dll --verify
```
