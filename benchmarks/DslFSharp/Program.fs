open AspiringFarmer
open AspiringFarmer.Redis
open AspiringFarmer.Benchmarks

let create count =
    distributedApp {
        let cache = redis "cache" { data_volume }

        for index in 1 .. count - 1 do
            project $"service-{index}" Measure.ServicePath {
                reference cache
                wait_for cache

                configure (fun resource ->
                    Aspire.Hosting.ResourceBuilderExtensions.WithHttpEndpoint(resource) |> ignore)
            }
    }

[<EntryPoint>]
let main args = Measure.Run(args, create)
