module AspiringFarmer.Benchmarks.NativeFSharpModel

open Aspire.Hosting
open AspiringFarmer.Benchmarks

let create count =
    let builder = DistributedApplication.CreateBuilder [||]
    let cache = builder.AddRedis("cache").WithDataVolume()

    for index in 1 .. count - 1 do
        builder
            .AddProject($"service-{index}", Measure.ServicePath)
            .WithReference(cache)
            .WaitFor(cache)
            .WithHttpEndpoint()
        |> ignore

    builder

[<EntryPoint>]
let main args = Measure.Run(args, create)
