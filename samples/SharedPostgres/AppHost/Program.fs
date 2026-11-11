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

let schemaJob name projectName db =
    project name $"../{projectName}/{projectName}.fsproj" {
        reference_as "appdb" db
        wait_for_child db
    }

let describe ephemeral verifyMail (builder: IDistributedApplicationBuilder) =
    let transport = if verifyMail then "mailpit" else builder.Configuration["Mail:Transport"]

    let externalSmtp =
        if transport = "smtp" then
            Some(smtp "mail" { options_from_configuration "Mail:Smtp" })
        else
            None

    let inbox =
        if transport = "mailpit" && builder.ExecutionContext.IsRunMode then
            Some(mailpit "mailpit" "v1.33.1")
        else
            None

    let configureMail (resource: IResourceBuilder<ProjectResource>) =
        let parameter key name secret =
            resource.WithEnvironment(key, builder.AddParameter(name, secret = secret)) |> ignore

        match transport with
        | "mailpit" when inbox.IsSome ->
            resource
                .WithEnvironment("Mail__Transport", "mailpit")
                .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
            |> ignore
        | null | "" | "disabled" | "mailpit" | "smtp" -> ()
        | "exchange-smtp" | "graph" ->
            resource.WithEnvironment("Mail__Transport", transport) |> ignore
            parameter "Mail__From" "mail-from" false
            parameter "Mail__TenantId" "mail-tenant-id" false
            parameter "Mail__ClientId" "mail-client-id" false
            parameter "Mail__ClientSecret" "mail-client-secret" true
        | _ -> invalidOp "Unsupported Mail:Transport."

    appDefinition {
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

        match inbox with
        | Some inbox ->
            project "storefront" "../Storefront/Storefront.fsproj" {
                reference identity
                reference catalog
                reference orders
                wait_for_completion identitySchema
                wait_for_completion catalogSchema
                wait_for_completion ordersSchema
                https_endpoint "https"
                http_health_check "https" "/health"
                configure configureMail
                endpoint_environment "Mail__Smtp__Endpoint" (endpoint "smtp" inbox)
                wait_for inbox
            }
        | None ->
            project "storefront" "../Storefront/Storefront.fsproj" {
                reference identity
                reference catalog
                reference orders
                wait_for_completion identitySchema
                wait_for_completion catalogSchema
                wait_for_completion ordersSchema
                https_endpoint "https"
                http_health_check "https" "/health"
                configure configureMail
                smtp_reference externalSmtp
            }

        project "back-office" "../BackOffice/BackOffice.fsproj" {
            reference identity
            reference inventory
            reference reporting
            wait_for_completion identitySchema
            wait_for_completion inventorySchema
            wait_for_completion reportingSchema
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
    let builder = AppHost.buildWith hostArgs (describe (benchmark || verifyMail) verifyMail)

    if benchmark || verifyMail then
        let afterReady = if verifyMail then verifyDelivery builder else fun _ -> Task.CompletedTask
        use app = builder.Build()
        (measure app afterReady).GetAwaiter().GetResult()
    else
        AppHost.run builder

    0
