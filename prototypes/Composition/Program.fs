module Composition.Program

open System
open Aspire.Hosting
open Aspire.Hosting.ApplicationModel
open AspiringFarmer
open AspiringFarmer.PostgreSQL

let migrationPath = "../PostgreSQL.Migration/PostgreSQL.Migration.fsproj"
let apiPath = "../Service/Service.fsproj"

let configureMigration (databaseName: string) (resource: IResourceBuilder<ProjectResource>) =
    resource.WithEnvironment("MIGRATION_DATABASE", databaseName) |> ignore

let configureApi (resource: IResourceBuilder<ProjectResource>) =
    resource.WithHttpEndpoint(name = "http").WithHttpHealthCheck("/health")
    |> ignore

type DatabaseTopology =
    { Database: Description<PostgresDatabaseResource>
      Migration: Description<ProjectResource> }

let databaseTopology prefix password =
    let databaseName = prefix + "-db"

    let server =
        postgresWithPassword (prefix + "-postgres") password { configure ignore }

    let db = database databaseName server { configure ignore }

    let migration =
        project (prefix + "-migration") migrationPath {
            reference db
            wait_for_child db
            configure (configureMigration databaseName)
        }

    { Database = db; Migration = migration }

let describe args (instances: (string * string) list) =
    distributedApp {
        withArgs args

        for prefix, apiName in instances do
            let password = secretParameter (prefix + "-password")
            let topology = databaseTopology prefix password

            project apiName apiPath {
                reference topology.Database
                wait_for_completion topology.Migration
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
        migration.WithReference(db).WaitFor(db) |> ignore
        configureMigration databaseName migration
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

    if collision <> Some "Conflicting resource name: blue-db" then
        failwith "Expected a duplicate database name for the reused prefix."

    printfn "PASS: reusing a prefix reports the conflicting resource name"

[<EntryPoint>]
let main args =
    let instances = [ "blue", "blue-api"; "green", "green-api" ]

    if Array.contains "--verify" args then
        verifyDuplicatePrefix ()
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
