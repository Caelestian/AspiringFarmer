module MailConfiguration

open Aspire.Hosting
open Aspire.Hosting.ApplicationModel
open AspiringFarmer

let describe verifyMail (builder: IDistributedApplicationBuilder) =
    let transport = if verifyMail then "mailpit" else builder.Configuration["Mail:Transport"]

    match transport with
    | "mailpit" when builder.ExecutionContext.IsRunMode ->
        let inbox = mailpit "mailpit" "v1.33.1"
        let configure (resource: IResourceBuilder<ProjectResource>) =
            resource.WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development") |> ignore

        Some(smtpFromMailpit inbox), configure
    | "smtp" ->
        Some(smtp "mail" { options_from_configuration "Mail:Smtp" }), ignore
    | "exchange-smtp" | "graph" ->
        let configure (resource: IResourceBuilder<ProjectResource>) =
            let parameter key name secret =
                resource.WithEnvironment(key, builder.AddParameter(name, secret = secret)) |> ignore

            resource.WithEnvironment("Mail__Transport", transport) |> ignore
            parameter "Mail__From" "mail-from" false
            parameter "Mail__TenantId" "mail-tenant-id" false
            parameter "Mail__ClientId" "mail-client-id" false
            parameter "Mail__ClientSecret" "mail-client-secret" true

        None, configure
    | null | "" | "disabled" | "mailpit" ->
        None, ignore
    | _ ->
        invalidOp "Unsupported Mail:Transport."
