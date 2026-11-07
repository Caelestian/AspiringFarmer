module Container.Program

open Aspire.Hosting
open Aspire.Hosting.ApplicationModel
open AspiringFarmer

let configureApi (resource: IResourceBuilder<ProjectResource>) =
    resource.WithHttpEndpoint(name = "http").WithHttpHealthCheck("/health")
    |> ignore

let describe args =
    distributedApp {
        withArgs args

        let upstream =
            container "upstream" "nginx" "alpine" {
                http_endpoint "http" 80
                configure (fun resource -> resource.WithHttpHealthCheck("/") |> ignore)
            }

        project "api" "../Container.Api/Container.Api.fsproj" {
            endpoint_environment "UPSTREAM_URL" (endpoint "http" upstream)
            wait_for upstream
            configure configureApi
        }
    }

[<EntryPoint>]
let main args =
    use app = (describe args).Build()
    app.Run()
    0
