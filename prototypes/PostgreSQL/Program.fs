module PostgreSQL.Program

open Aspire.Hosting
open Aspire.Hosting.ApplicationModel
open AspiringFarmer
open AspiringFarmer.PostgreSQL

let configureApi (resource: IResourceBuilder<ProjectResource>) =
    resource.WithHttpEndpoint(name = "http").WithHttpHealthCheck("/health")
    |> ignore

let configureMigration fail (resource: IResourceBuilder<ProjectResource>) =
    if fail then
        resource.WithArgs("--fail") |> ignore

let describe args =
    let fail = Array.contains "--fail-migration" args

    distributedApp {
        withArgs (args |> Array.filter ((<>) "--fail-migration"))
        let password = secretParameter "postgres-password"
        let server = postgresWithPassword "postgres" password { configure ignore }
        let db = database "appdb" server { configure ignore }

        let migration =
            project "migration" "../PostgreSQL.Migration/PostgreSQL.Migration.fsproj" {
                reference db
                wait_for_child db
                configure (configureMigration fail)
            }

        project "api" "../Service/Service.fsproj" {
            reference db
            wait_for_completion migration
            configure configureApi
        }
    }

let native () =
    let builder = DistributedApplication.CreateBuilder [||]
    let password = builder.AddParameter("postgres-password", secret = true)
    let db = builder.AddPostgres("postgres", password = password).AddDatabase("appdb")

    let migration =
        builder.AddProject("migration", "../PostgreSQL.Migration/PostgreSQL.Migration.fsproj")

    migration.WithReference(db).WaitFor(db) |> ignore
    let api = builder.AddProject("api", "../Service/Service.fsproj")
    api.WithReference(db).WaitForCompletion(migration) |> ignore
    configureApi api
    builder

let relationships (resource: IResource) =
    resource.Annotations
    |> Seq.choose (function
        | :? ResourceRelationshipAnnotation as relationship -> Some(relationship.Type, relationship.Resource.Name)
        | :? WaitAnnotation as wait -> Some($"{wait.WaitType}:{wait.ExitCode}", wait.Resource.Name)
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
            failwith "PostgreSQL topology differs from native Aspire."

        let server =
            builder.Resources
            |> Seq.choose (function
                | :? PostgresServerResource as server -> Some server
                | _ -> None)
            |> Seq.exactlyOne

        let db =
            builder.Resources
            |> Seq.choose (function
                | :? PostgresDatabaseResource as db -> Some db
                | _ -> None)
            |> Seq.exactlyOne

        if not (obj.ReferenceEquals(server, db.Parent)) then
            failwith "The database must share the registered PostgreSQL server."

        let password =
            builder.Resources
            |> Seq.choose (function
                | :? ParameterResource as parameter when parameter.Name = "postgres-password" -> Some parameter
                | _ -> None)
            |> Seq.exactlyOne

        if
            not password.Secret
            || not (obj.ReferenceEquals(password, server.PasswordParameter))
        then
            failwith "The server must reference the registered secret parameter."

        if
            not (server.ConnectionStringExpression.ValueExpression.Contains("{postgres-password.value}"))
            || not (db.ConnectionStringExpression.ValueExpression.Contains("{postgres.connectionString}"))
        then
            failwith "The database connection string must retain the deferred password reference."

        use app = builder.Build()
        printfn "PASS: native topology and deferred secret references verified"
        0
    else
        use app = (describe args).Build()
        app.Run()
        0
