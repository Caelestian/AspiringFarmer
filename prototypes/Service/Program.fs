open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http

let builder = WebApplication.CreateBuilder(Environment.GetCommandLineArgs()[1..])
let app = builder.Build()

app.MapGet("/", Func<string>(fun () -> "AspiringFarmer native Aspire prototype"))
|> ignore

app.MapGet("/health", Func<IResult>(fun () -> Results.Ok("healthy"))) |> ignore
app.Run()
