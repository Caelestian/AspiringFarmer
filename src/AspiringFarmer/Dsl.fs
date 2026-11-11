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

    [<CustomOperation("endpoint_environment")>]
    member _.EndpointEnvironment(state: ResourceState<ProjectResource>, name: string, EndpointDescription resolve) =
        state.Add(fun registry resource -> resource.WithEnvironment(name, resolve registry) |> ignore)

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

    let build args (AppDefinition registrations) =
        let builder = DistributedApplication.CreateBuilder(args: string array)
        let registry = Registry builder
        registrations |> List.iter (fun register -> register registry)
        builder

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

    let container name image tag = ContainerBuilder(name, image, tag)

    let endpoint<'T when 'T :> IResourceWithEndpoints> name (resource: Description<'T>) =
        EndpointDescription(fun registry -> (registry.Resolve resource).GetEndpoint name)

    let secretParameter name =
        let parameter =
            ResourceBuilder<ParameterResource>(
                name,
                fun (builder: IDistributedApplicationBuilder) (registry: Registry) ->
                    builder.AddParameter(registry.QualifyName name, secret = true)
            )

        parameter.Run { Actions = [] }

    let withArgs args = Args(Array.copy args)
    let appDefinition = AppDefinitionBuilder()
    let distributedApp = DistributedAppBuilder()
