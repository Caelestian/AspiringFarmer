open System
open System.Net.Http
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Aspire.Hosting
open Aspire.Hosting.ApplicationModel
open AspiringFarmer
open AspiringFarmer.PostgreSQL

let databaseNames = [ "identity"; "catalog"; "orders"; "inventory"; "reporting" ]

let describe ephemeral (builder: IDistributedApplicationBuilder) =
    let outboundMail =
        mail "mail" {
            mailpit_inbox (mailpit "mailpit" "v1.33.1")
            mailpit_environment "ASPNETCORE_ENVIRONMENT" "Development"
        }

    let signIn =
        if String.Equals(builder.Configuration["Identity:Enabled"], "true", StringComparison.OrdinalIgnoreCase) then
            Some(oidc "storefront-auth" { options_from_configuration "Identity" })
        else
            None

    appDefinition {
        let server =
            if ephemeral then
                postgres "postgres" { configure ignore }
            else
                postgres "postgres" { data_volume }

        let identity = database "identity" server { schema_project "../Identity.Schema/Identity.Schema.fsproj" }
        let catalog = database "catalog" server { schema_project "../Catalog.Schema/Catalog.Schema.fsproj" }
        let orders = database "orders" server { schema_project "../Orders.Schema/Orders.Schema.fsproj" }
        let inventory = database "inventory" server { schema_project "../Inventory.Schema/Inventory.Schema.fsproj" }
        let reporting = database "reporting" server { schema_project "../Reporting.Schema/Reporting.Schema.fsproj" }

        project "storefront" "../Storefront/Storefront.fsproj" {
            use_database identity
            use_database catalog
            use_database orders
            https_endpoint "https"
            http_health_check "https" "/health"
            oidc_reference signIn
            mail_reference outboundMail
        }

        project "back-office" "../BackOffice/BackOffice.fsproj" {
            use_database identity
            use_database inventory
            use_database reporting
            http_endpoint "http"
            http_health_check "http" "/health"
        }
    }

let measure (app: DistributedApplication) (afterReady: CancellationToken -> Task) =
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
            do! afterReady timeout.Token
        with error ->
            do! app.StopAsync()
            return raise error

        do! app.StopAsync()
    }

let verifyDelivery (builder: IDistributedApplicationBuilder) =
    let endpoint resourceName (endpointName: string) =
        let resource = builder.Resources |> Seq.find (fun resource -> resource.Name = resourceName)
        EndpointReference(resource :?> IResourceWithEndpoints, endpointName)

    let storefront = endpoint "storefront" "https"
    let inbox = endpoint "mailpit" "inbox"

    fun cancellationToken ->
        task {
            use client = new HttpClient()
            use! response = client.PostAsync(storefront.Url + "/mail/test", null, cancellationToken)
            response.EnsureSuccessStatusCode() |> ignore
            use! content = client.GetStreamAsync(inbox.Url + "/api/v1/messages", cancellationToken)
            use! messages = JsonDocument.ParseAsync(content, cancellationToken = cancellationToken)
            let delivered =
                messages.RootElement.GetProperty("messages").EnumerateArray()
                |> Seq.exists (fun message -> message.GetProperty("Subject").GetString() = "SharedPostgres mail test")

            if not delivered then
                failwith "Mailpit did not capture Storefront's message."

            Console.WriteLine("PASS: Storefront sent a message captured by Mailpit.")
        }
        :> Task

[<EntryPoint>]
let main args =
    let benchmark = Array.contains "--benchmark" args
    let verifyMail = Array.contains "--verify-mail" args
    let hostArgs = args |> Array.filter (fun arg -> arg <> "--benchmark" && arg <> "--verify-mail")
    let hostArgs = if verifyMail then Array.append hostArgs [| "--Mail:Transport=mailpit" |] else hostArgs
    let builder = AppHost.buildWith hostArgs (describe (benchmark || verifyMail))

    if benchmark || verifyMail then
        let afterReady = if verifyMail then verifyDelivery builder else fun _ -> Task.CompletedTask
        use app = builder.Build()
        (measure app afterReady).GetAwaiter().GetResult()
    else
        AppHost.run builder

    0
