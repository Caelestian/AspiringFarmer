module SharedPostgres.Mail

open System
open System.Net.Http
open System.Net.Http.Headers
open System.Net.Http.Json
open System.Threading
open System.Threading.Tasks
open Azure.Core
open Azure.Identity
open MailKit.Net.Smtp
open MailKit.Security
open Microsoft.Extensions.Configuration
open MimeKit

type Sender = string -> string -> string -> CancellationToken -> Task<unit>

let private required (configuration: IConfiguration) key =
    match configuration[key] with
    | value when not (String.IsNullOrWhiteSpace value) -> value
    | _ -> invalidOp $"Missing mail setting {key}."

let private smtp (host: string) port (security: SecureSocketOptions)
    (authenticate: SmtpClient -> CancellationToken -> Task) (from: string) : Sender =
    let from = MailboxAddress.Parse from

    fun recipient subject body cancellationToken ->
        task {
            use message = new MimeMessage()
            message.From.Add from
            message.To.Add(MailboxAddress.Parse recipient)
            message.Subject <- subject
            message.Body <- new TextPart("plain", Text = body)
            use client = new SmtpClient()
            do! client.ConnectAsync(host, port, security, cancellationToken)
            do! authenticate client cancellationToken
            let! _ = client.SendAsync(message, cancellationToken)
            do! client.DisconnectAsync(true, cancellationToken)
        }

let private graph (http: HttpClient) getToken (from: string) : Sender =
    let path = $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString from}/sendMail"

    fun recipient subject body cancellationToken ->
        task {
            let recipient = (MailboxAddress.Parse recipient).Address
            let! token = getToken cancellationToken
            use request = new HttpRequestMessage(HttpMethod.Post, path)
            request.Headers.Authorization <- AuthenticationHeaderValue("Bearer", token)
            request.Content <-
                JsonContent.Create(
                    {| message =
                        {| subject = subject
                           body = {| contentType = "Text"; content = body |}
                           toRecipients = [| {| emailAddress = {| address = recipient |} |} |] |} |}
                )
            use! response = http.SendAsync(request, cancellationToken)
            response.EnsureSuccessStatusCode() |> ignore
        }

let createWithCredential (http: HttpClient) getCredential (configuration: IConfiguration) =
    let get = required configuration

    let entraToken scope =
        let credential: TokenCredential = getCredential ()
        let context = TokenRequestContext [| scope |]

        fun cancellationToken ->
            task {
                let! token = credential.GetTokenAsync(context, cancellationToken)
                return token.Token
            }

    match configuration["Mail:Transport"] with
    | null | "" | "disabled" -> None
    | "mailpit" ->
        let endpoint = Uri(get "Mail:Smtp:Endpoint")

        smtp endpoint.Host endpoint.Port SecureSocketOptions.None
            (fun _ _ -> Task.CompletedTask) "storefront@example.test"
        |> Some
    | "smtp" ->
        let port =
            match configuration["Mail:Smtp:Port"] with
            | null | "" -> 587
            | value -> Int32.Parse value

        let tls =
            match configuration["Mail:Smtp:Tls"] with
            | null | "" | "starttls" -> SecureSocketOptions.StartTls
            | "ssl-on-connect" -> SecureSocketOptions.SslOnConnect
            | _ -> invalidOp "Mail:Smtp:Tls must be starttls or ssl-on-connect."

        let username = get "Mail:Smtp:Username"
        let password = get "Mail:Smtp:Password"

        smtp (get "Mail:Smtp:Host") port tls
            (fun client cancellationToken -> client.AuthenticateAsync(username, password, cancellationToken))
            (get "Mail:From")
        |> Some
    | "exchange-smtp" ->
        let sender = (MailboxAddress.Parse(get "Mail:From")).Address
        let getToken = entraToken "https://outlook.office365.com/.default"

        smtp "smtp.office365.com" 587 SecureSocketOptions.StartTls
            (fun client cancellationToken ->
                task {
                    let! token = getToken cancellationToken
                    do! client.AuthenticateAsync(SaslMechanismOAuth2(sender, token), cancellationToken)
                } :> Task)
            sender
        |> Some
    | "graph" ->
        let sender = (MailboxAddress.Parse(get "Mail:From")).Address
        graph http (entraToken "https://graph.microsoft.com/.default") sender |> Some
    | _ -> invalidOp "Unsupported Mail:Transport."

let create http (configuration: IConfiguration) =
    createWithCredential http
        (fun () ->
            ClientSecretCredential(
                required configuration "Mail:TenantId",
                required configuration "Mail:ClientId",
                required configuration "Mail:ClientSecret"
            ))
        configuration
