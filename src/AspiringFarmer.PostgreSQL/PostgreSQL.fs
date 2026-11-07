namespace AspiringFarmer.PostgreSQL

open Aspire.Hosting
open Aspire.Hosting.ApplicationModel
open AspiringFarmer

type PostgreSQLBuilder(name: string) =
    inherit ResourceBuilder<PostgresServerResource>(name, fun builder -> builder.AddPostgres name)

    [<CustomOperation("data_volume")>]
    member _.DataVolume(state: ResourceState<PostgresServerResource>) =
        state.Add(fun _ resource -> resource.WithDataVolume() |> ignore)

[<AutoOpen>]
module Builders =
    let postgres name = PostgreSQLBuilder name

    let database name (server: Description<PostgresServerResource>) =
        ResourceBuilder<PostgresDatabaseResource>(
            name,
            fun _ (registry: Registry) -> (registry.Resolve server).AddDatabase name
        )
