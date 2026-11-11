module Verification

open System
open System.Net.Http
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Aspire.Hosting
open Aspire.Hosting.ApplicationModel

let private databaseNames = [ "identity"; "catalog"; "orders"; "inventory"; "reporting" ]

let private measure (app: DistributedApplication) (afterReady: CancellationToken -> Task) =
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

let private verifyDelivery (builder: IDistributedApplicationBuilder) =
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

let run (builder: IDistributedApplicationBuilder) verifyMail =
    let afterReady = if verifyMail then verifyDelivery builder else fun _ -> Task.CompletedTask
    use app = builder.Build()
    (measure app afterReady).GetAwaiter().GetResult()
