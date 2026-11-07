module Composition.Program

open System
open Aspire.Hosting
open Aspire.Hosting.ApplicationModel
open AspiringFarmer
open AspiringFarmer.PostgreSQL

let migrationPath = "../PostgreSQL.Migration/PostgreSQL.Migration.fsproj"
let apiPath = "../Service/Service.fsproj"

let configureApi (resource: IResourceBuilder<ProjectResource>) =
    resource.WithHttpEndpoint(name = "http").WithHttpHealthCheck("/health")
    |> ignore

type DatabaseTopology =
    { Database: Description<PostgresDatabaseResource>
      Migration: Description<ProjectResource> }

let databaseTopology (scope: AppScope) =
    let password = secretParameter "password"
    let server = postgresWithPassword "postgres" password { configure ignore }
    let db = database "db" server { configure ignore }

    let migration =
        project "migration" migrationPath {
            reference_as "appdb" db
            wait_for_child db
        }

    appDefinition { migration },
    { Database = scope.Export db
      Migration = scope.Export migration }

let describe args (instances: (string * string) list) =
    distributedApp {
        withArgs args

        for prefix, apiName in instances do
            let topology = AppHost.instantiate prefix databaseTopology
            topology.Definition

            project apiName apiPath {
                reference topology.Exports.Database
                wait_for_completion topology.Exports.Migration
                configure configureApi
            }
    }

let native args instances =
    let builder = DistributedApplication.CreateBuilder(args: string array)

    for prefix, apiName in instances do
        let databaseName = prefix + "-db"
        let password = builder.AddParameter(prefix + "-password", secret = true)

        let db =
            builder.AddPostgres(prefix + "-postgres", password = password).AddDatabase(databaseName)

        let migration = builder.AddProject(prefix + "-migration", migrationPath)
        let source = db :?> IResourceBuilder<IResourceWithConnectionString>
        migration.WithReference(source, connectionName = "appdb").WaitFor(db) |> ignore
        let api = builder.AddProject(apiName, apiPath)
        api.WithReference(db).WaitForCompletion(migration) |> ignore
        configureApi api

    builder

let verifyDuplicatePrefix () =
    let collision =
        try
            describe [||] [ "blue", "first-api"; "blue", "second-api" ] |> ignore
            None
        with :? InvalidOperationException as error ->
            Some error.Message

    if collision <> Some "Conflicting resource name: blue-migration" then
        failwith "Expected a duplicate migration name for the reused prefix."

    printfn "PASS: reusing a prefix reports the conflicting resource name"

let verifyNestedExports () =
    let local = secretParameter "credential"

    let credentialDefinition (scope: AppScope) =
        appDefinition { local }, scope.Export local

    let parent =
        AppHost.instantiate "parent" (fun scope ->
            let child = scope.Instantiate("child", credentialDefinition)
            appDefinition { child.Definition }, child.Exports)

    let sibling = AppHost.instantiate "sibling" credentialDefinition

    let consumer =
        AppHost.instantiate "consumer" (fun _ -> appDefinition { parent.Exports }, ())

    let builder =
        appDefinition {
            consumer.Definition
            parent.Definition
            sibling.Definition
        }
        |> AppHost.build [||]

    let names = builder.Resources |> Seq.map _.Name |> Seq.sort |> Seq.toList

    if names <> [ "parent-child-credential"; "sibling-credential" ] then
        failwith $"Unexpected scoped exports: {names}"

    use app = builder.Build()
    printfn "PASS: nested exports resolve in their owning scope across instances"

[<EntryPoint>]
let main args =
    let instances = [ "blue", "blue-api"; "green", "green-api" ]

    if Array.contains "--verify" args then
        verifyDuplicatePrefix ()
        verifyNestedExports ()
        let builder = describe [||] instances

        for prefix, apiName in instances do
            let api = builder.Resources |> Seq.find (fun resource -> resource.Name = apiName)

            let waits =
                api.Annotations
                |> Seq.choose (function
                    | :? WaitAnnotation as wait -> Some wait
                    | _ -> None)

            let wait = Seq.exactlyOne waits

            if
                wait.Resource.Name <> prefix + "-migration"
                || wait.WaitType <> WaitType.WaitForCompletion
            then
                failwith $"Unexpected migration wait for {apiName}."

        use app = builder.Build()
        printfn "PASS: each API waits for its own migration"
        0
    else
        let builder =
            if Array.contains "--native" args then
                native (Array.filter ((<>) "--native") args) instances
            else
                describe args instances

        use app = builder.Build()
        app.Run()
        0
