open System
open Npgsql

[<EntryPoint>]
let main args =
    let connectionString =
        Environment.GetEnvironmentVariable("ConnectionStrings__appdb")

    if String.IsNullOrWhiteSpace connectionString then
        eprintfn "Missing appdb connection string."
        1
    else
        try
            use connection = new NpgsqlConnection(connectionString)
            connection.Open()

            let sql =
                if Array.contains "--fail" args then
                    "SELECT * FROM intentional_missing_migration_table"
                else
                    "CREATE TABLE IF NOT EXISTS migration_example (id integer PRIMARY KEY)"

            use command = new NpgsqlCommand(sql, connection)
            command.ExecuteNonQuery() |> ignore
            printfn "Migration completed."
            0
        with
        | :? PostgresException as error ->
            eprintfn "Migration SQL failed with SQLSTATE %s." error.SqlState
            1
        | :? NpgsqlException ->
            eprintfn "Migration could not connect to appdb or execute SQL."
            1
