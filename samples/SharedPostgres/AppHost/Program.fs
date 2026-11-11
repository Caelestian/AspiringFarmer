open System
open System.IO
open Aspire.Hosting
open Microsoft.Extensions.Configuration
open AspiringFarmer
open AspiringFarmer.PostgreSQL

let loadDefaults (builder: IDistributedApplicationBuilder) =
    // Aspire can load appsettings.json from the working directory when launched as a DLL.
    let defaults =
        lazy (
            ConfigurationBuilder()
                .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
                .Build()
        )

    for key in [ "Identity:Enabled"; "Postgres:PersistData" ] do
        if isNull builder.Configuration[key] then
            builder.Configuration[key] <- defaults.Value[key]

let describe () =
    let outboundMail =
        mail "mail" {
            mailpit_inbox (mailpit "mailpit" "v1.33.1")
            mailpit_environment "ASPNETCORE_ENVIRONMENT" "Development"
        }

    let signIn = oidc "storefront-auth" { options_from_configuration "Identity" }

    appDefinition {
        let server = postgres "postgres" { data_volume_from_configuration "Postgres:PersistData" }

        let identity = database "identity" server { schema_project "../Identity.Schema/Identity.Schema.fsproj" }
        let catalog = database "catalog" server { schema_project "../Catalog.Schema/Catalog.Schema.fsproj" }
        let orders = database "orders" server { schema_project "../Orders.Schema/Orders.Schema.fsproj" }
        let inventory = database "inventory" server { schema_project "../Inventory.Schema/Inventory.Schema.fsproj" }
        let reporting = database "reporting" server { schema_project "../Reporting.Schema/Reporting.Schema.fsproj" }

        project "storefront" "../Storefront/Storefront.fsproj" {
            use_database identity
            use_database catalog
            use_database orders
            https_endpoint "https"
            http_health_check "https" "/health"
            oidc_reference signIn
            mail_reference outboundMail
        }

        project "back-office" "../BackOffice/BackOffice.fsproj" {
            use_database identity
            use_database inventory
            use_database reporting
            http_endpoint "http"
            http_health_check "http" "/health"
        }
    }

[<EntryPoint>]
let main args =
    let benchmark = Array.contains "--benchmark" args
    let verifyMail = Array.contains "--verify-mail" args
    let hostArgs = args |> Array.filter (fun arg -> arg <> "--benchmark" && arg <> "--verify-mail")
    let verificationOverrides =
        [| if benchmark || verifyMail then "--Postgres:PersistData=false"
           if verifyMail then "--Mail:Transport=mailpit" |]

    let hostArgs = Array.append hostArgs verificationOverrides
    let builder =
        AppHost.buildWith hostArgs (fun builder ->
            loadDefaults builder
            describe ())

    if benchmark || verifyMail then
        Verification.run builder verifyMail
    else
        AppHost.run builder

    0
