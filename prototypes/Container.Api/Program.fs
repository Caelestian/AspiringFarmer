open System
open System.Net.Http
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http

[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder(args)
    let upstream = builder.Configuration["UPSTREAM_URL"]

    if String.IsNullOrWhiteSpace upstream then
        invalidOp "Aspire must supply UPSTREAM_URL."

    use client = new HttpClient(Timeout = TimeSpan.FromSeconds 3.)
    let app = builder.Build()

    app.MapGet(
        "/health",
        Func<Task<IResult>>(fun () ->
            task {
                try
                    use! response = client.GetAsync(upstream)

                    if response.IsSuccessStatusCode then
                        return Results.Ok("Upstream responded")
                    else
                        return Results.StatusCode(503)
                with
                | :? HttpRequestException
                | :? TaskCanceledException -> return Results.StatusCode(503)
            })
    )
    |> ignore

    app.Run()
    0
