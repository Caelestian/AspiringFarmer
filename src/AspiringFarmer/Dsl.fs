namespace AspiringFarmer

open System
open System.Collections.Generic
open Aspire.Hosting
open Aspire.Hosting.ApplicationModel

type internal ScopeIdentity = { Id: Guid; Prefix: string }

// Each application owns its registry. Descriptions can be reused between hosts.
type Registry(builder: IDistributedApplicationBuilder) as this =
    let registered = Dictionary<struct (Guid * Guid), obj>()

    let names =
        Dictionary<string, struct (Guid * Guid)>(StringComparer.OrdinalIgnoreCase)

    let visiting = HashSet<struct (Guid * Guid)>()
    let mutable scope: ScopeIdentity option = None

    /// Qualifies a local resource name in the instance being registered.
    member _.QualifyName(name: string) =
        match scope with
        | Some owner -> owner.Prefix + "-" + name
        | None -> name

    member internal _.WithinScope(owner: ScopeIdentity, action: unit -> 'T) =
        let previous = scope
        scope <- Some owner

        try
            action ()
        finally
            scope <- previous

    member private _.ResolveLocal(description: Description<'T>) : IResourceBuilder<'T> =
        let name = this.QualifyName description.Name

        let scopeId =
            match scope with
            | Some owner -> owner.Id
            | None -> Guid.Empty

        let key = struct (scopeId, description.Id)

        match names.TryGetValue name with
        | true, id when id <> key -> invalidOp $"Conflicting resource name: {name}"
        | _ -> names[name] <- key

        match registered.TryGetValue key with
        | true, resource -> unbox<IResourceBuilder<'T>> resource
        | _ ->
            if not (visiting.Add key) then
                invalidOp $"Dependency cycle at: {name}"

            try
                let resource = description.Create builder this

                if scope.IsSome && resource.Resource.Name <> name then
                    invalidOp $"Scoped resource '{name}' must use Registry.QualifyName in its factory."

                registered.Add(key, box resource)
                resource
            finally
                visiting.Remove key |> ignore

    member _.Resolve(description: Description<'T>) : IResourceBuilder<'T> =
        match description.Owner with
        | Some owner -> this.WithinScope(owner, fun () -> this.ResolveLocal description)
        | None -> this.ResolveLocal description

    member this.Register(description: Description<'T>) = this.Resolve description |> ignore

and Description<'T when 'T :> IResource> =
    internal
        { Id: Guid
          Name: string
          Owner: ScopeIdentity option
          Create: IDistributedApplicationBuilder -> Registry -> IResourceBuilder<'T> }

/// Configuration actions remain typed to their native resource builder.
type ResourceState<'T when 'T :> IResource> =
    internal
        { Actions: (Registry -> IResourceBuilder<'T> -> unit) list }

    member state.Add(action) =
        { Actions = state.Actions @ [ action ] }

/// Base for typed resource expressions, including third-party resource factories.
type ResourceBuilder<'T when 'T :> IResource>
    (name: string, create: IDistributedApplicationBuilder -> Registry -> IResourceBuilder<'T>) =
    new(name, create: IDistributedApplicationBuilder -> IResourceBuilder<'T>) =
        ResourceBuilder<'T>(name, fun builder _ -> create builder)

    member _.Yield(()) : ResourceState<'T> = { Actions = [] }

    [<CustomOperation("configure")>]
    member _.Configure(state: ResourceState<'T>, configure: IResourceBuilder<'T> -> unit) =
        state.Add(fun _ resource -> configure resource)

    member _.Run(state: ResourceState<'T>) : Description<'T> =
        { Id = Guid.NewGuid()
          Name = name
          Owner = None
          Create =
            fun builder registry ->
                let resource = create builder registry
                state.Actions |> List.iter (fun action -> action registry resource)
                resource }

/// A named endpoint resolved against the application's native resource registry.
type EndpointDescription = internal EndpointDescription of (Registry -> EndpointReference)

type DatabaseWithSchema<'T when 'T :> IResourceWithConnectionString> =
    internal DatabaseWithSchema of Description<'T> * Description<ProjectResource>

module private Parameters =
    let describe name secret =
        let parameter =
            ResourceBuilder<ParameterResource>(
                name,
                fun (builder: IDistributedApplicationBuilder) (registry: Registry) ->
                    builder.AddParameter(registry.QualifyName name, secret = secret)
            )

        parameter.Run { Actions = [] }

type OidcOptions =
    internal
        { AuthorityEndpoint: (EndpointDescription * string) option
          ConfigurationSection: string option }

type OidcDescription =
    internal OidcDescription of (Registry -> IResourceBuilder<ProjectResource> -> unit)

type OidcBuilder(name: string) =
    member _.Yield(()) : OidcOptions =
        { AuthorityEndpoint = None
          ConfigurationSection = None }

    [<CustomOperation("authority_from_endpoint")>]
    member _.AuthorityFromEndpoint(options: OidcOptions, endpoint: EndpointDescription, path: string) =
        { options with AuthorityEndpoint = Some(endpoint, path) }

    [<CustomOperation("options_from_configuration")>]
    member _.OptionsFromConfiguration(options: OidcOptions, section: string) =
        { options with ConfigurationSection = Some section }

    member _.Run(options: OidcOptions) =
        let authority = Parameters.describe (name + "-authority") false
        let clientId = Parameters.describe (name + "-client-id") false
        let clientSecret = Parameters.describe (name + "-client-secret") true

        OidcDescription(fun registry resource ->
            let section =
                options.ConfigurationSection
                |> Option.map resource.ApplicationBuilder.Configuration.GetSection

            let enabled =
                match section with
                | Some section when not (isNull section["Enabled"]) ->
                    match Boolean.TryParse section["Enabled"] with
                    | true, value -> value
                    | false, _ -> invalidOp $"Configuration '{section.Path}:Enabled' must be true or false."
                | _ -> true

            if enabled then
                match options.AuthorityEndpoint with
                | Some(EndpointDescription resolve, path) ->
                    let expression = ReferenceExpressionBuilder()
                    expression.AppendFormatted(resolve registry)
                    expression.AppendLiteral path
                    resource.WithEnvironment("Identity__Authority", expression.Build()) |> ignore
                | None ->
                    resource.WithEnvironment("Identity__Authority", registry.Resolve authority) |> ignore

                resource.WithEnvironment("Identity__ClientId", registry.Resolve clientId) |> ignore
                resource.WithEnvironment("Identity__ClientSecret", registry.Resolve clientSecret) |> ignore

                for section in Option.toList section do
                    let scopes = section.GetSection("Scopes")

                    for index, scope in scopes.GetChildren() |> Seq.indexed do
                        if not (String.IsNullOrWhiteSpace scope.Value) then
                            resource.WithEnvironment($"Identity__Scopes__{index}", scope.Value) |> ignore)

[<RequireQualifiedAccess>]
type SmtpTls =
    | StartTls
    | SslOnConnect

type SmtpOptions =
    internal
        { Port: int option
          Tls: SmtpTls option
          ConfigurationSection: string option }

type SmtpDescription =
    internal SmtpDescription of (Registry -> IResourceBuilder<ProjectResource> -> unit)

module private Smtp =
    let fromMailpit (inbox: Description<ContainerResource>) =
        SmtpDescription(fun registry resource ->
            if resource.ApplicationBuilder.ExecutionContext.IsRunMode then
                let mailpit = registry.Resolve inbox

                resource
                    .WithEnvironment("Mail__Transport", "mailpit")
                    .WithEnvironment("Mail__Smtp__Endpoint", mailpit.GetEndpoint "smtp")
                    .WaitFor(mailpit)
                |> ignore)

type SmtpBuilder(name: string) =
    member _.Yield(()) : SmtpOptions =
        { Port = None
          Tls = None
          ConfigurationSection = None }

    [<CustomOperation("port")>]
    member _.Port(options: SmtpOptions, port: int) =
        if port < 1 || port > 65535 then
            invalidArg (nameof port) "SMTP port must be between 1 and 65535."

        { options with Port = Some port }

    [<CustomOperation("tls")>]
    member _.Tls(options: SmtpOptions, tls: SmtpTls) =
        { options with Tls = Some tls }

    [<CustomOperation("options_from_configuration")>]
    member _.OptionsFromConfiguration(options: SmtpOptions, section: string) =
        { options with ConfigurationSection = Some section }

    member _.Run(options: SmtpOptions) =
        let parameter suffix secret =
            Parameters.describe (name + "-" + suffix) secret

        let parameters =
            [ "Mail__From", parameter "from" false
              "Mail__Smtp__Host", parameter "smtp-host" false
              "Mail__Smtp__Username", parameter "smtp-username" false
              "Mail__Smtp__Password", parameter "smtp-password" true ]

        SmtpDescription(fun registry resource ->
            resource.WithEnvironment("Mail__Transport", "smtp") |> ignore

            for key, parameter in parameters do
                resource.WithEnvironment(key, registry.Resolve parameter) |> ignore

            let setOption key explicitValue =
                let value =
                    match explicitValue, options.ConfigurationSection with
                    | Some value, _ -> value
                    | None, Some section -> resource.ApplicationBuilder.Configuration[$"{section}:{key}"]
                    | None, None -> null

                if not (String.IsNullOrWhiteSpace value) then
                    resource.WithEnvironment($"Mail__Smtp__{key}", value) |> ignore

            let port =
                options.Port |> Option.map (fun port -> port.ToString(Globalization.CultureInfo.InvariantCulture))

            let tls =
                options.Tls
                |> Option.map (function
                    | SmtpTls.StartTls -> "starttls"
                    | SmtpTls.SslOnConnect -> "ssl-on-connect")

            setOption "Port" port
            setOption "Tls" tls)

type MailOptions =
    internal
        { ConfigurationSection: string
          Inbox: Description<ContainerResource> option
          MailpitEnvironment: (string * string) list }

type MailDescription =
    internal MailDescription of (Registry -> IResourceBuilder<ProjectResource> -> unit)

type MailBuilder(name: string) =
    member _.Yield(()) : MailOptions =
        { ConfigurationSection = "Mail"
          Inbox = None
          MailpitEnvironment = [] }

    [<CustomOperation("options_from_configuration")>]
    member _.OptionsFromConfiguration(options: MailOptions, section: string) =
        { options with ConfigurationSection = section }

    [<CustomOperation("mailpit_inbox")>]
    member _.MailpitInbox(options: MailOptions, inbox: Description<ContainerResource>) =
        { options with Inbox = Some inbox }

    [<CustomOperation("mailpit_environment")>]
    member _.MailpitEnvironment(options: MailOptions, key: string, value: string) =
        { options with MailpitEnvironment = options.MailpitEnvironment @ [ key, value ] }

    member _.Run(options: MailOptions) =
        let smtp = SmtpBuilder(name)
        let (SmtpDescription applySmtp) =
            smtp { options_from_configuration (options.ConfigurationSection + ":Smtp") }

        let parameter suffix secret =
            Parameters.describe (name + "-" + suffix) secret

        let entraParameters =
            [ "Mail__From", parameter "from" false
              "Mail__TenantId", parameter "tenant-id" false
              "Mail__ClientId", parameter "client-id" false
              "Mail__ClientSecret", parameter "client-secret" true ]

        MailDescription(fun registry resource ->
            let builder = resource.ApplicationBuilder

            match builder.Configuration[$"{options.ConfigurationSection}:Transport"] with
            | "smtp" ->
                applySmtp registry resource
            | "mailpit" when builder.ExecutionContext.IsRunMode ->
                match options.Inbox with
                | Some inbox ->
                    let (SmtpDescription applyMailpit) = Smtp.fromMailpit inbox
                    applyMailpit registry resource

                    for key, value in options.MailpitEnvironment do
                        resource.WithEnvironment(key, value) |> ignore
                | None -> invalidOp $"Mail '{name}' requires a mailpit_inbox when using the mailpit transport."
            | "exchange-smtp" | "graph" as transport ->
                resource.WithEnvironment("Mail__Transport", transport) |> ignore

                for key, parameter in entraParameters do
                    resource.WithEnvironment(key, registry.Resolve parameter) |> ignore
            | null | "" | "disabled" | "mailpit" -> ()
            | transport -> invalidOp $"Unsupported mail transport '{transport}' for '{name}'.")

type ContainerBuilder(name: string, image: string, tag: string) =
    inherit
        ResourceBuilder<ContainerResource>(
            name,
            fun (builder: IDistributedApplicationBuilder) (registry: Registry) ->
                builder.AddContainer(registry.QualifyName name, image, tag)
        )

    [<CustomOperation("http_endpoint")>]
    member _.HttpEndpoint(state: ResourceState<ContainerResource>, name: string, targetPort: int) =
        state.Add(fun _ resource -> resource.WithHttpEndpoint(targetPort = targetPort, name = name) |> ignore)

type ProjectBuilder(name: string, path: string) =
    inherit
        ResourceBuilder<ProjectResource>(
            name,
            fun (builder: IDistributedApplicationBuilder) (registry: Registry) ->
                builder.AddProject(registry.QualifyName name, path)
        )

    [<CustomOperation("reference")>]
    member _.Reference<'T when 'T :> IResourceWithConnectionString>
        (state: ResourceState<ProjectResource>, dependency: Description<'T>)
        =
        { Actions =
            state.Actions
            @ [ fun registry resource -> resource.WithReference(registry.Resolve dependency) |> ignore ] }

    [<CustomOperation("reference")>]
    member _.Reference(state: ResourceState<ProjectResource>, dependency: Description<ProjectResource>) =
        { Actions =
            state.Actions
            @ [ fun registry resource -> resource.WithReference(registry.Resolve dependency) |> ignore ] }

    [<CustomOperation("reference_as")>]
    member _.ReferenceAs<'T when 'T :> IResourceWithConnectionString>
        (state: ResourceState<ProjectResource>, name: string, dependency: Description<'T>)
        =
        state.Add(fun registry resource ->
            let source =
                registry.Resolve dependency :?> IResourceBuilder<IResourceWithConnectionString>

            resource.WithReference(source, connectionName = name) |> ignore)

    [<CustomOperation("wait_for")>]
    member _.WaitFor<'T when 'T :> IResourceWithWaitSupport>
        (state: ResourceState<ProjectResource>, dependency: Description<'T>)
        =
        { Actions =
            state.Actions
            @ [ fun registry resource -> resource.WaitFor(registry.Resolve dependency) |> ignore ] }

    [<CustomOperation("wait_for_child")>]
    member _.WaitForChild<'T when 'T :> IResource and 'T :> IResourceWithParent>
        (state: ResourceState<ProjectResource>, dependency: Description<'T>)
        =
        state.Add(fun registry resource -> resource.WaitFor(registry.Resolve dependency) |> ignore)

    [<CustomOperation("wait_for_completion")>]
    member _.WaitForCompletion(state: ResourceState<ProjectResource>, dependency: Description<ProjectResource>) =
        state.Add(fun registry resource -> resource.WaitForCompletion(registry.Resolve dependency) |> ignore)

    [<CustomOperation("use_database")>]
    member this.UseDatabase(state: ResourceState<ProjectResource>, DatabaseWithSchema(database, schema)) =
        let state = this.Reference(state, database)
        this.WaitForCompletion(state, schema)

    [<CustomOperation("endpoint_environment")>]
    member _.EndpointEnvironment(state: ResourceState<ProjectResource>, name: string, EndpointDescription resolve) =
        state.Add(fun registry resource -> resource.WithEnvironment(name, resolve registry) |> ignore)

    [<CustomOperation("oidc_reference")>]
    member _.OidcReference(state: ResourceState<ProjectResource>, OidcDescription apply) =
        state.Add apply

    [<CustomOperation("oidc_reference")>]
    member this.OidcReference(state: ResourceState<ProjectResource>, oidc: OidcDescription option) =
        match oidc with
        | Some description -> this.OidcReference(state, description)
        | None -> state

    [<CustomOperation("smtp_reference")>]
    member _.SmtpReference(state: ResourceState<ProjectResource>, SmtpDescription apply) =
        state.Add apply

    [<CustomOperation("smtp_reference")>]
    member this.SmtpReference(state: ResourceState<ProjectResource>, smtp: SmtpDescription option) =
        match smtp with
        | Some description -> this.SmtpReference(state, description)
        | None -> state

    [<CustomOperation("mail_reference")>]
    member _.MailReference(state: ResourceState<ProjectResource>, MailDescription apply) =
        state.Add apply

    [<CustomOperation("http_endpoint")>]
    member _.HttpEndpoint(state: ResourceState<ProjectResource>, name: string) =
        state.Add(fun _ resource -> resource.WithHttpEndpoint(name = name) |> ignore)

    [<CustomOperation("https_endpoint")>]
    member _.HttpsEndpoint(state: ResourceState<ProjectResource>, name: string) =
        state.Add(fun _ resource -> resource.WithHttpsEndpoint(name = name) |> ignore)

    [<CustomOperation("http_health_check")>]
    member _.HttpHealthCheck(state: ResourceState<ProjectResource>, endpointName: string, path: string) =
        state.Add(fun _ resource -> resource.WithHttpHealthCheck(path, endpointName = endpointName) |> ignore)

    [<CustomOperation("external_http_endpoints")>]
    member _.ExternalHttpEndpoints(state: ResourceState<ProjectResource>) =
        { Actions =
            state.Actions
            @ [ fun _ resource -> resource.WithExternalHttpEndpoints() |> ignore ] }

type HostOption = Args of string array

type AppItem =
    | Option of HostOption
    | Register of (Registry -> unit)

/// Resource registrations that can be included in any AppHost.
type AppDefinition = internal AppDefinition of (Registry -> unit) list

type AppItemsBuilder() =
    member _.Yield(description: Description<'T>) =
        [ Register(fun registry -> registry.Register description) ]

    member _.Yield(AppDefinition registrations) =
        [ Register(fun registry -> List.iter (fun register -> register registry) registrations) ]

    member _.Zero() : AppItem list = []
    member _.Combine(left, right: unit -> AppItem list) = left @ right ()
    member _.Delay(body: unit -> AppItem list) = body

    member _.For(values: seq<'T>, body: 'T -> AppItem list) =
        values |> Seq.collect body |> Seq.toList

type AppDefinitionBuilder() =
    inherit AppItemsBuilder()

    member _.Run(body: unit -> AppItem list) =
        body ()
        |> List.map (function
            | Register register -> register
            | Option _ -> invalidOp "Host options belong on the outer AppHost.")
        |> AppDefinition

type AppInstance<'T> =
    { Definition: AppDefinition
      Exports: 'T }

type AppScope internal (prefix: string) =
    let owner = { Id = Guid.NewGuid(); Prefix = prefix }

    member _.Export(description: Description<'T>) =
        match description.Owner with
        | Some _ -> description
        | None -> { description with Owner = Some owner }

    member internal this.Create(define: AppScope -> AppDefinition * 'T) =
        let AppDefinition registrations, exports = define this

        { Definition =
            AppDefinition
                [ fun registry ->
                      registry.WithinScope(owner, fun () -> List.iter (fun register -> register registry) registrations) ]
          Exports = exports }

    member _.Instantiate(segment: string, define: AppScope -> AppDefinition * 'T) =
        if String.IsNullOrWhiteSpace segment then
            invalidArg "segment" "A component prefix is required."

        AppScope(prefix + "-" + segment).Create define

module AppHost =
    let instantiate prefix define =
        if String.IsNullOrWhiteSpace prefix then
            invalidArg "prefix" "A component prefix is required."

        AppScope(prefix).Create define

    let buildWith args (describe: IDistributedApplicationBuilder -> AppDefinition) =
        let builder = DistributedApplication.CreateBuilder(args: string array)
        let (AppDefinition registrations) = describe builder
        let registry = Registry builder
        registrations |> List.iter (fun register -> register registry)
        builder

    let build args definition = buildWith args (fun _ -> definition)

    let run (builder: IDistributedApplicationBuilder) =
        use app = builder.Build()
        app.Run()

type DistributedAppBuilder() =
    inherit AppItemsBuilder()

    member _.Yield(option: HostOption) = [ Option option ]

    member _.Run(body: unit -> AppItem list) =
        let items = body ()

        let args =
            match
                items
                |> List.choose (function
                    | Option(Args args) -> Some args
                    | _ -> None)
            with
            | [] -> [||]
            | [ args ] -> args
            | _ -> invalidOp "withArgs can appear only once."

        let definition =
            items
            |> List.choose (function
                | Register register -> Some register
                | _ -> None)
            |> AppDefinition

        AppHost.build args definition

[<AutoOpen>]
module Builders =
    let project name path = ProjectBuilder(name, path)

    let database_with_schema database schema = DatabaseWithSchema(database, schema)

    let smtp name = SmtpBuilder(name)

    let mail name = MailBuilder(name)

    let oidc name = OidcBuilder(name)

    let container name image tag = ContainerBuilder(name, image, tag)

    let mailpit name tag =
        container name "axllent/mailpit" tag {
            http_endpoint "inbox" 8025
            configure (fun resource ->
                resource
                    .WithEndpoint(targetPort = 1025, scheme = "tcp", name = "smtp")
                    .WithHttpHealthCheck("/readyz", endpointName = "inbox")
                |> ignore)
        }

    /// Connects an SMTP client to a local Mailpit resource during a run.
    let smtpFromMailpit inbox = Smtp.fromMailpit inbox

    let endpoint<'T when 'T :> IResourceWithEndpoints> name (resource: Description<'T>) =
        EndpointDescription(fun registry -> (registry.Resolve resource).GetEndpoint name)

    let secretParameter name = Parameters.describe name true

    let withArgs args = Args(Array.copy args)
    let appDefinition = AppDefinitionBuilder()
    let distributedApp = DistributedAppBuilder()
