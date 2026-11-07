# SQL Server sample

The optional `AspiringFarmer.SqlServer` library provides typed SQL Server and database descriptions. The sample API references `appdb` and uses `wait_for_child` to wait for the database.

Run the model comparison from the repository root.

```sh
dotnet build prototypes/SqlServer
dotnet prototypes/SqlServer/bin/Debug/net10.0/SqlServer.dll --verify
```

Run the sample with a container engine on x86-64 Linux.

```sh
dotnet run --project prototypes/SqlServer
```

Microsoft supports the SQL Server Linux container on x86-64 Linux hosts. See [Microsoft's container guidance](https://learn.microsoft.com/sql/linux/install-upgrade/quickstart-install-docker).
