module SharedPostgres.Web

open System
open System.Threading
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Configuration
open Npgsql

let runWith configure args name databases =
    let builder = WebApplication.CreateBuilder(args: string array)
    let app = builder.Build()

    app.MapGet("/", Func<string>(fun () -> name)) |> ignore

    app.MapGet(
        "/health",
        Func<CancellationToken, _>(fun cancellationToken ->
            task {
                try
                    for database, table in databases do
                        let connectionString = app.Configuration.GetConnectionString database

                        if String.IsNullOrWhiteSpace connectionString then
                            invalidOp $"Missing the {database} connection string."

                        use connection = new NpgsqlConnection(connectionString)
                        do! connection.OpenAsync cancellationToken
                        use command = new NpgsqlCommand($"SELECT 1 FROM {table} WHERE id = 1", connection)
                        let! result = command.ExecuteScalarAsync cancellationToken

                        if result <> box 1 then
                            invalidOp $"Missing seed data in {database}."

                    return Results.Ok "healthy"
                with
                | :? NpgsqlException
                | :? InvalidOperationException -> return Results.StatusCode 503
            })
    )
    |> ignore

    configure app
    app.Run()

let run args name databases = runWith ignore args name databases
