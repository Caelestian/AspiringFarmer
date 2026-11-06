namespace AspiringFarmer.Redis

open Aspire.Hosting
open Aspire.Hosting.ApplicationModel
open AspiringFarmer

type RedisBuilder(name: string) =
    inherit ResourceBuilder<RedisResource>(name, fun builder -> builder.AddRedis name)

    [<CustomOperation("data_volume")>]
    member _.DataVolume(state: ResourceState<RedisResource>) =
        state.Add(fun _ resource -> resource.WithDataVolume() |> ignore)

[<AutoOpen>]
module Builders =
    let redis name = RedisBuilder name
