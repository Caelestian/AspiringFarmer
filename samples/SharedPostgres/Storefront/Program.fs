open System
open System.Net.Http
open System.Threading
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Hosting
open SharedPostgres

let subject = "SharedPostgres mail test"
let body = "Storefront sent this test message."

[<EntryPoint>]
let main args =
    use http = new HttpClient()

    match args with
    | [| "--send-test"; recipient |] ->
        let configuration = ConfigurationBuilder().AddEnvironmentVariables().Build()
        use timeout = new CancellationTokenSource(TimeSpan.FromSeconds 30.)

        match Mail.create http configuration with
        | Some send ->
            (send recipient subject body timeout.Token).GetAwaiter().GetResult()
            printfn "The mail transport accepted the message."
            0
        | None ->
            eprintfn "Set Mail__Transport before sending a test message."
            1
    | _ ->
        let configure (app: WebApplication) =
            let sender = Mail.create http app.Configuration

            match sender with
            | Some send when app.Environment.IsDevelopment() && app.Configuration["Mail:Transport"] = "mailpit" ->
                app.MapPost(
                    "/mail/test",
                    Func<CancellationToken, _>(fun cancellationToken ->
                        task {
                            do! send "demo@example.test" subject body cancellationToken
                            return Results.NoContent()
                        })
                )
                |> ignore
            | _ -> ()

        Web.runWith configure args "Storefront"
            [ "identity", "accounts"
              "catalog", "products"
              "orders", "orders" ]
        0
