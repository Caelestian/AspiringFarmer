open System
open System.Collections.Generic
open System.Net
open System.Net.Http
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Azure.Core
open Microsoft.Extensions.Configuration
open SharedPostgres

let require condition message = if not condition then failwith message

type Credential() =
    inherit TokenCredential()
    member val Scopes = [||] with get, set
    override this.GetToken(context: TokenRequestContext, _: CancellationToken) =
        this.Scopes <- context.Scopes
        AccessToken("test-token", DateTimeOffset.UtcNow.AddMinutes 5.)
    override this.GetTokenAsync(context: TokenRequestContext, cancellationToken: CancellationToken) =
        ValueTask<AccessToken>(this.GetToken(context, cancellationToken))

type GraphHandler() =
    inherit HttpMessageHandler()
    member val Calls = 0 with get, set
    override this.SendAsync(request, cancellationToken) =
        task {
            this.Calls <- this.Calls + 1
            require (request.Method = HttpMethod.Post) "Graph must use POST."
            require (request.RequestUri.AbsoluteUri = "https://graph.microsoft.com/v1.0/users/sender%40example.test/sendMail")
                "Unexpected Graph sender URL."
            require (request.Headers.Authorization.ToString() = "Bearer test-token") "Missing bearer token."
            let! content = request.Content.ReadAsStringAsync cancellationToken
            use document = JsonDocument.Parse content
            let message = document.RootElement.GetProperty "message"
            require (message.GetProperty("subject").GetString() = "Sample subject") "Missing subject."
            require (message.GetProperty("body").GetProperty("content").GetString() = "Sample body") "Missing body."
            let recipients = message.GetProperty "toRecipients"
            require (recipients[0].GetProperty("emailAddress").GetProperty("address").GetString() = "recipient@example.test")
                "Unexpected recipient."
            return new HttpResponseMessage(HttpStatusCode.Accepted)
        }

let configuration entries =
    ConfigurationBuilder().AddInMemoryCollection(entries |> List.map (fun (key, value) -> KeyValuePair(key, value))).Build()

[<EntryPoint>]
let main _ =
    use handler = new GraphHandler()
    use http = new HttpClient(handler)
    let credential = Credential()
    let config = configuration [ "Mail:Transport", "graph"; "Mail:From", "sender@example.test" ]
    let send = Mail.createWithCredential http (fun () -> credential) config |> Option.get
    (send "recipient@example.test" "Sample subject" "Sample body" CancellationToken.None).GetAwaiter().GetResult()
    require (credential.Scopes = [| "https://graph.microsoft.com/.default" |]) "Wrong Graph token audience."
    require (handler.Calls = 1) "Expected one Graph request."
    printfn "PASS: Graph sender, payload, bearer token, and token audience"

    let smtp =
        configuration [
            "Mail:Transport", "smtp"
            "Mail:From", "sender@example.test"
            "Mail:Smtp:Host", "smtp.example.test"
            "Mail:Smtp:Username", "sender"
            "Mail:Smtp:Password", "test-secret"
            "Mail:Smtp:Tls", "none"
        ]
    let rejected =
        try
            Mail.create http smtp |> ignore
            false
        with :? InvalidOperationException -> true
    require rejected "External SMTP must reject an unencrypted configuration."
    printfn "PASS: external SMTP requires TLS"
    0
