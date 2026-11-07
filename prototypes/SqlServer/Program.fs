module SqlServer.Program

open Aspire.Hosting
open Aspire.Hosting.ApplicationModel
open AspiringFarmer
open AspiringFarmer.SqlServer

let configureApi (resource: IResourceBuilder<ProjectResource>) =
    resource.WithHttpEndpoint(name = "http").WithHttpHealthCheck("/health")
    |> ignore

let describe args =
    distributedApp {
        withArgs args
        let server = sqlServer "sqlserver" { data_volume }
        let db = database "appdb" server { configure ignore }

        project "api" "../Service/Service.fsproj" {
            reference db
            wait_for_child db
            configure configureApi
        }
    }

let native () =
    let builder = DistributedApplication.CreateBuilder [||]
    let db = builder.AddSqlServer("sqlserver").WithDataVolume().AddDatabase("appdb")
    let api = builder.AddProject("api", "../Service/Service.fsproj")
    api.WithReference(db).WaitFor(db) |> ignore
    configureApi api
    builder

let relationships (resource: IResource) =
    resource.Annotations
    |> Seq.choose (function
        | :? ResourceRelationshipAnnotation as relationship -> Some(relationship.Type, relationship.Resource.Name)
        | :? WaitAnnotation as wait -> Some(string wait.WaitType, wait.Resource.Name)
        | _ -> None)
    |> Seq.sort
    |> Seq.toList

let signature (builder: IDistributedApplicationBuilder) =
    builder.Resources
    |> Seq.map (fun resource -> resource.Name, resource.GetType(), relationships resource)
    |> Seq.sortBy (fun (name, _, _) -> name)
    |> Seq.toList

[<EntryPoint>]
let main args =
    if Array.contains "--verify" args then
        let builder = describe [||]

        if signature builder <> signature (native ()) then
            failwith "SQL Server topology differs from native Aspire."

        let server =
            builder.Resources
            |> Seq.choose (function
                | :? SqlServerServerResource as server -> Some server
                | _ -> None)
            |> Seq.exactlyOne

        let db =
            builder.Resources
            |> Seq.choose (function
                | :? SqlServerDatabaseResource as db -> Some db
                | _ -> None)
            |> Seq.exactlyOne

        if not (obj.ReferenceEquals(server, db.Parent)) then
            failwith "The database must share the registered SQL Server instance."

        use app = builder.Build()
        printfn "PASS: database parent, reference, and wait match native Aspire"
        0
    else
        use app = (describe args).Build()
        app.Run()
        0
