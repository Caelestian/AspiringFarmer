open System
open System.Threading
open System.Threading.Tasks
open Aspire.Hosting
open Aspire.Hosting.ApplicationModel
open AspiringFarmer
open AspiringFarmer.PostgreSQL

let databaseNames = [ "identity"; "catalog"; "orders"; "inventory"; "reporting" ]

let schemaJob name projectName db =
    project name $"../{projectName}/{projectName}.fsproj" {
        reference_as "appdb" db
        wait_for_child db
    }

let configureWeb (resource: IResourceBuilder<ProjectResource>) =
    resource.WithHttpEndpoint(name = "http").WithHttpHealthCheck("/health") |> ignore

let describe args ephemeral =
    distributedApp {
        withArgs args

        let server =
            if ephemeral then
                postgres "postgres" { configure ignore }
            else
                postgres "postgres" { data_volume }

        let identity = database "identity" server { configure ignore }
        let catalog = database "catalog" server { configure ignore }
        let orders = database "orders" server { configure ignore }
        let inventory = database "inventory" server { configure ignore }
        let reporting = database "reporting" server { configure ignore }

        let identitySchema = schemaJob "identity-schema" "Identity.Schema" identity
        let catalogSchema = schemaJob "catalog-schema" "Catalog.Schema" catalog
        let ordersSchema = schemaJob "orders-schema" "Orders.Schema" orders
        let inventorySchema = schemaJob "inventory-schema" "Inventory.Schema" inventory
        let reportingSchema = schemaJob "reporting-schema" "Reporting.Schema" reporting

        identitySchema
        catalogSchema
        ordersSchema
        inventorySchema
        reportingSchema

        project "storefront" "../Storefront/Storefront.fsproj" {
            reference identity
            reference catalog
            reference orders
            wait_for_completion identitySchema
            wait_for_completion catalogSchema
            wait_for_completion ordersSchema
            configure configureWeb
        }

        project "back-office" "../BackOffice/BackOffice.fsproj" {
            reference identity
            reference inventory
            reference reporting
            wait_for_completion identitySchema
            wait_for_completion inventorySchema
            wait_for_completion reportingSchema
            configure configureWeb
        }
    }

let measure (app: DistributedApplication) =
    task {
        use timeout = new CancellationTokenSource(TimeSpan.FromMinutes 2.0)
        let notifications = app.ResourceNotifications

        let watchHealth name =
            task {
                let! _ = notifications.WaitForResourceHealthyAsync(name, WaitBehavior.StopOnResourceUnavailable, timeout.Token)
                Console.WriteLine($"sample:ready:{name}")
            }

        let watchSchema name =
            task {
                let! _ =
                    notifications.WaitForResourceAsync(
                        name, [| KnownResourceStates.Finished; KnownResourceStates.FailedToStart; KnownResourceStates.Exited |],
                        timeout.Token
                    )

                match notifications.TryGetCurrentState name with
                | true, state when state.Snapshot.ExitCode = Nullable 0 -> Console.WriteLine($"sample:ready:{name}")
                | _ -> failwith $"Schema job {name} did not exit successfully."
            }

        let healthChecks = "postgres" :: "storefront" :: "back-office" :: databaseNames |> List.map watchHealth
        let schemaChecks = databaseNames |> List.map (fun name -> watchSchema (name + "-schema"))
        let readiness = Task.WhenAll(healthChecks @ schemaChecks)

        do! app.StartAsync timeout.Token
        Console.WriteLine("sample:host-started")
        try
            let! _ = readiness
            Console.WriteLine("sample:all-ready")
        with error ->
            do! app.StopAsync()
            return raise error

        do! app.StopAsync()
    }

[<EntryPoint>]
let main args =
    let benchmark = Array.contains "--benchmark" args
    let builder = describe (Array.filter ((<>) "--benchmark") args) benchmark

    if benchmark then
        use app = builder.Build()
        (measure app).GetAwaiter().GetResult()
    else
        AppHost.run builder

    0
