module SharedPostgres.Schema

open System
open Npgsql

let run migration seed =
    try
        let connectionString = Environment.GetEnvironmentVariable "ConnectionStrings__appdb"

        if String.IsNullOrWhiteSpace connectionString then
            invalidOp "Missing the appdb connection string."

        use connection = new NpgsqlConnection(connectionString)
        connection.Open()
        use transaction = connection.BeginTransaction()

        use migrationCommand = new NpgsqlCommand(migration, connection, transaction)
        migrationCommand.ExecuteNonQuery() |> ignore

        use seedCommand = new NpgsqlCommand(seed, connection, transaction)
        seedCommand.ExecuteNonQuery() |> ignore

        transaction.Commit()
        printfn "Schema migration and seeding completed."
        0
    with
    | :? PostgresException as error ->
        eprintfn "Schema job failed with SQLSTATE %s." error.SqlState
        1
    | :? NpgsqlException ->
        eprintfn "Schema job could not connect to PostgreSQL or execute SQL."
        1
    | :? InvalidOperationException ->
        eprintfn "Schema job could not complete. Check the appdb connection configuration."
        1
