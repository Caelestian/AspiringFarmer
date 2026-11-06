module NativeAspire.Program

open System
open Aspire.Hosting
open Aspire.Hosting.ApplicationModel
open NativeAspire.Dsl

let configureProject (resource: IResourceBuilder<ProjectResource>) =
    resource.WithHttpEndpoint(targetPort = 8080, name = "http") |> ignore
    resource.WithEnvironment("PROTOTYPE", "native-callback") |> ignore
    resource.WithHttpHealthCheck("/health") |> ignore

let describe args =
    distributedApp {
        withArgs args

        let cache = redis "cache" { data_volume }

        let api =
            project "api" "../Service/Service.fsproj" {
                reference cache
                wait_for cache
                configure configureProject
                external_http_endpoints
            }

        project "web" "../Service/Service.fsproj" {
            reference api
            reference cache
            wait_for api
            wait_for cache
            configure configureProject
            external_http_endpoints
        }
    }

let native (args: string array) =
    let builder = DistributedApplication.CreateBuilder args
    let cache = builder.AddRedis("cache").WithDataVolume()
    let api = builder.AddProject("api", "../Service/Service.fsproj")
    api.WithReference(cache).WaitFor(cache) |> ignore
    configureProject api
    api.WithExternalHttpEndpoints() |> ignore
    let web = builder.AddProject("web", "../Service/Service.fsproj")

    web.WithReference(api).WithReference(cache).WaitFor(api).WaitFor(cache)
    |> ignore

    configureProject web
    web.WithExternalHttpEndpoints() |> ignore
    builder

let signature (builder: IDistributedApplicationBuilder) =
    builder.Resources
    |> Seq.map (fun resource ->
        resource.Name,
        resource.GetType().FullName,
        resource.Annotations
        |> Seq.map (fun annotation ->
            match annotation with
            | :? WaitAnnotation as wait -> $"wait:{wait.Resource.Name}:{wait.WaitType}"
            | :? ResourceRelationshipAnnotation as relationship ->
                $"relationship:{relationship.Resource.Name}:{relationship.Type}"
            | :? EndpointAnnotation as endpoint ->
                $"endpoint:{endpoint.Name}:{endpoint.TargetPort}:{endpoint.IsExternal}"
            | other -> other.GetType().FullName)
        |> Seq.sort
        |> Seq.toList)
    |> Seq.sortBy (fun (name, _, _) -> name)
    |> Seq.toList

[<EntryPoint>]
let main args =
    if Array.contains "--verify" args then
        let dsl = describe [||]
        let baseline = native [||]

        if signature dsl <> signature baseline then
            failwith "Native application models differ."

        let names =
            dsl.Resources
            |> Seq.filter (fun resource -> resource :? ProjectResource || resource :? RedisResource)
            |> Seq.map _.Name
            |> Seq.toList

        if List.sort names <> [ "api"; "cache"; "web" ] then
            failwith $"Unexpected registrations: {names}"

        use app = dsl.Build()
        printfn "PASS: native model matches resource types, endpoints, references, and waits"
        printfn "PASS: shared Redis description registers exactly once"
        printfn "PASS: native Aspire application builds successfully"

        for resource in dsl.Resources do
            printfn "  %s: %s" resource.Name (resource.GetType().Name)

        0
    else
        let builder =
            if Array.contains "--native" args then
                native (args |> Array.filter ((<>) "--native"))
            else
                describe args

        use app = builder.Build()
        app.Run()
        0
