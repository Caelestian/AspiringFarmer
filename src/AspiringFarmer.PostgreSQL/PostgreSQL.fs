namespace AspiringFarmer.PostgreSQL

open Aspire.Hosting
open Aspire.Hosting.ApplicationModel
open AspiringFarmer

type PostgreSQLBuilder(name: string, password: Description<ParameterResource> option) =
    inherit
        ResourceBuilder<PostgresServerResource>(
            name,
            fun builder registry ->
                match password with
                | None -> builder.AddPostgres name
                | Some description ->
                    let parameter = registry.Resolve description

                    if not parameter.Resource.Secret then
                        invalidArg "password" "A PostgreSQL password requires a secret parameter."

                    builder.AddPostgres(name, password = parameter)
        )

    new(name) = PostgreSQLBuilder(name, None)

    [<CustomOperation("data_volume")>]
    member _.DataVolume(state: ResourceState<PostgresServerResource>) =
        state.Add(fun _ resource -> resource.WithDataVolume() |> ignore)

[<AutoOpen>]
module Builders =
    let postgres name = PostgreSQLBuilder name

    let postgresWithPassword name password = PostgreSQLBuilder(name, Some password)

    let database name (server: Description<PostgresServerResource>) =
        ResourceBuilder<PostgresDatabaseResource>(
            name,
            fun _ (registry: Registry) -> (registry.Resolve server).AddDatabase name
        )
