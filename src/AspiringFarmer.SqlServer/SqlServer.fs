namespace AspiringFarmer.SqlServer

open Aspire.Hosting
open Aspire.Hosting.ApplicationModel
open AspiringFarmer

type SqlServerBuilder(name: string) =
    inherit
        ResourceBuilder<SqlServerServerResource>(
            name,
            fun (builder: IDistributedApplicationBuilder) (registry: Registry) ->
                builder.AddSqlServer(registry.QualifyName name)
        )

    [<CustomOperation("data_volume")>]
    member _.DataVolume(state: ResourceState<SqlServerServerResource>) =
        state.Add(fun _ resource -> resource.WithDataVolume() |> ignore)

[<AutoOpen>]
module Builders =
    let sqlServer name = SqlServerBuilder name

    let database name (server: Description<SqlServerServerResource>) =
        ResourceBuilder<SqlServerDatabaseResource>(
            name,
            fun _ (registry: Registry) -> (registry.Resolve server).AddDatabase(registry.QualifyName name)
        )
