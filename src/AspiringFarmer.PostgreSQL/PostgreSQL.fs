namespace AspiringFarmer.PostgreSQL

open System
open Aspire.Hosting
open Aspire.Hosting.ApplicationModel
open AspiringFarmer

type PostgreSQLBuilder(name: string, password: Description<ParameterResource> option) =
    inherit
        ResourceBuilder<PostgresServerResource>(
            name,
            fun builder registry ->
                match password with
                | None -> builder.AddPostgres(registry.QualifyName name)
                | Some description ->
                    let parameter = registry.Resolve description

                    if not parameter.Resource.Secret then
                        invalidArg "password" "A PostgreSQL password requires a secret parameter."

                    builder.AddPostgres(registry.QualifyName name, password = parameter)
        )

    new(name) = PostgreSQLBuilder(name, None)

    [<CustomOperation("data_volume")>]
    member _.DataVolume(state: ResourceState<PostgresServerResource>) =
        state.Add(fun _ resource -> resource.WithDataVolume() |> ignore)

    [<CustomOperation("data_volume_from_configuration")>]
    member _.DataVolumeFromConfiguration(state: ResourceState<PostgresServerResource>, key: string) =
        state.Add(fun _ resource ->
            match resource.ApplicationBuilder.Configuration[key] with
            | null -> ()
            | setting ->
                match Boolean.TryParse setting with
                | true, true -> resource.WithDataVolume() |> ignore
                | true, false -> ()
                | false, _ -> invalidOp $"Configuration '{key}' must be true or false.")

type PostgreSQLSchemaState =
    internal
        { Database: ResourceState<PostgresDatabaseResource>
          ProjectName: string
          ProjectPath: string
          ConnectionAlias: string }

type PostgreSQLDatabaseBuilder(name: string, server: Description<PostgresServerResource>) =
    inherit
        ResourceBuilder<PostgresDatabaseResource>(
            name,
            fun _ registry -> (registry.Resolve server).AddDatabase(registry.QualifyName name)
        )

    [<CustomOperation("schema_project")>]
    member _.SchemaProject(state: ResourceState<PostgresDatabaseResource>, path: string) =
        { Database = state
          ProjectName = name + "-schema"
          ProjectPath = path
          ConnectionAlias = "appdb" }

    [<CustomOperation("schema_project")>]
    member _.SchemaProject(state: ResourceState<PostgresDatabaseResource>, path: string, projectName: string, connectionAlias: string) =
        { Database = state
          ProjectName = projectName
          ProjectPath = path
          ConnectionAlias = connectionAlias }

    [<CustomOperation("configure")>]
    member _.Configure(state: PostgreSQLSchemaState, configure: IResourceBuilder<PostgresDatabaseResource> -> unit) =
        { state with Database = base.Configure(state.Database, configure) }

    member _.Run(state: PostgreSQLSchemaState) =
        let db = base.Run state.Database

        let schema =
            project state.ProjectName state.ProjectPath {
                reference_as state.ConnectionAlias db
                wait_for_child db
            }

        database_with_schema db schema

[<AutoOpen>]
module Builders =
    let postgres name = PostgreSQLBuilder name

    let postgresWithPassword name password = PostgreSQLBuilder(name, Some password)

    let database name server = PostgreSQLDatabaseBuilder(name, server)
