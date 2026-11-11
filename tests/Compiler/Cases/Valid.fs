module CompilerFixtures.Valid

open Aspire.Hosting
open Aspire.Hosting.ApplicationModel
open AspiringFarmer
open AspiringFarmer.Redis

let cache: Description<RedisResource> = redis "cache" { data_volume }
let inbox: Description<ContainerResource> = mailpit "mailpit" "v1.33.1"
let outboundMail = smtp "mail" { port 465; tls SmtpTls.SslOnConnect }

let identityProvider = project "identity-provider" "IdentityProvider.fsproj" { https_endpoint "https" }
let signIn = oidc "web-auth" { authority_from_endpoint (endpoint "https" identityProvider) "/realms/example" }

// No core-library changes are needed to describe another connection resource.
let connection =
    ResourceBuilder<IResourceWithConnectionString>("external", fun builder -> builder.AddConnectionString("external")) {
        configure ignore
    }

let api: Description<ProjectResource> =
    project "api" "Api.fsproj" {
        reference cache
        reference connection
        wait_for cache
        https_endpoint "https"
        http_health_check "https" "/health"
        smtp_reference outboundMail
        oidc_reference signIn
        configure (fun resource -> resource.WithEnvironment("MODE", "test") |> ignore)
    }

let app =
    distributedApp {
        withArgs [||]
        let unused = redis "unused" { data_volume }
        cache

        if true then
            project "web" "Web.fsproj" {
                reference api
                wait_for api
            }

        for index in 1..2 do
            project $"worker-{index}" "Worker.fsproj" { reference cache }
    }
