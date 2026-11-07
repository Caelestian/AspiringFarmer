module Components.Program

open System
open Aspire.Hosting
open Aspire.Hosting.ApplicationModel
open AspiringFarmer

let catalog name =
    let upstream =
        container name "nginx" "alpine" {
            http_endpoint "http" 80
            configure (fun resource -> resource.WithHttpHealthCheck("/") |> ignore)
        }

    let definition = appDefinition { upstream }
    definition, upstream

let billing upstream =
    appDefinition {
        project "billing" "../Container.Api/Container.Api.fsproj" {
            endpoint_environment "UPSTREAM_URL" (endpoint "http" upstream)
            wait_for upstream

            configure (fun resource ->
                resource.WithHttpEndpoint(name = "http").WithHttpHealthCheck("/health")
                |> ignore)
        }
    }

let catalogDefinition, catalogEndpointOwner = catalog "catalog"

let billingStandalone =
    let localCatalog, upstream = catalog "billing-catalog"

    appDefinition {
        localCatalog
        billing upstream
    }

let combined =
    appDefinition {
        catalogDefinition
        billing catalogEndpointOwner
    }

let verify () =
    let check expected (builder: IDistributedApplicationBuilder) =
        let names =
            builder.Resources
            |> Seq.filter (fun resource -> resource :? ContainerResource || resource :? ProjectResource)
            |> Seq.map _.Name
            |> Set.ofSeq

        if names <> Set.ofList expected then
            failwith $"Unexpected resources: {names}"

        use app = builder.Build()
        ()

    AppHost.build [||] catalogDefinition |> check [ "catalog" ]
    AppHost.build [||] billingStandalone |> check [ "billing"; "billing-catalog" ]
    let combinedHost = AppHost.build [| "--component-probe"; "combined" |] combined

    if combinedHost.Configuration["component-probe"] <> "combined" then
        failwith "Expected runner arguments in the combined AppHost configuration."

    combinedHost |> check [ "catalog"; "billing" ]

    let collision =
        try
            let duplicate, _ = catalog "catalog"

            appDefinition {
                catalogDefinition
                duplicate
            }
            |> AppHost.build [||]
            |> ignore

            None
        with :? InvalidOperationException as error ->
            Some error.Message

    if collision <> Some "Conflicting resource name: catalog" then
        failwith "Expected duplicate names across components to be rejected."

    printfn "PASS: standalone and combined hosts build, share dependencies, and reject name collisions"

[<EntryPoint>]
let main args =
    if Array.contains "--verify" args then
        verify ()
    else
        let hostArgs =
            args |> Array.filter (fun arg -> arg <> "--catalog" && arg <> "--billing")

        let definition =
            match Array.contains "--catalog" args, Array.contains "--billing" args with
            | true, true -> invalidArg "args" "Select one standalone component."
            | true, false -> catalogDefinition
            | false, true -> billingStandalone
            | false, false -> combined

        definition |> AppHost.build hostArgs |> AppHost.run

    0
