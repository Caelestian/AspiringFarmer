# PostgreSQL sample

The optional `AspiringFarmer.PostgreSQL` library provides typed server and database descriptions. This sample yields the API, which references `appdb`. Resolving the database registers its PostgreSQL parent through the same application registry.

The API uses `wait_for_child appdb` to wait for Aspire's database health checks. The `wait_for` operation remains available for resources implementing Aspire's wait-support interface.

Run from the repository root with a container engine available.

```sh
dotnet run --project prototypes/PostgreSQL
```

To compare resource types and relationships with direct Aspire declarations without starting containers, run these commands.

```sh
dotnet build prototypes/PostgreSQL
dotnet prototypes/PostgreSQL/bin/Debug/net10.0/PostgreSQL.dll --verify
```
