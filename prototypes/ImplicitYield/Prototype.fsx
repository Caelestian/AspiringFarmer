// Run with: dotnet fsi prototypes/ImplicitYield/Prototype.fsx
open System
open System.Collections.Generic

type ResourceNode =
    { Id: Guid
      Name: string
      Kind: string
      References: ResourceNode list
      Waits: ResourceNode list
      Settings: string list }

// The marker preserves the resource kind without erasing it in user bindings.
type Redis = Redis
type Project = Project
type Resource<'Kind> = private Resource of ResourceNode

let private node (Resource value) = value

type ResourceBuilder<'Kind>(name: string, kind: string) =
    member _.Yield(()) : ResourceNode =
        { Id = Guid.NewGuid()
          Name = name
          Kind = kind
          References = []
          Waits = []
          Settings = [] }

    [<CustomOperation("reference")>]
    member _.Reference(state: ResourceNode, dependency: Resource<'Dependency>) =
        { state with References = state.References @ [ node dependency ] }

    [<CustomOperation("wait_for")>]
    member _.WaitFor(state: ResourceNode, dependency: Resource<'Dependency>) =
        { state with Waits = state.Waits @ [ node dependency ] }

    [<CustomOperation("data_volume")>]
    member _.DataVolume(state: ResourceNode) =
        { state with Settings = state.Settings @ [ "data_volume" ] }

    [<CustomOperation("external_http_endpoints")>]
    member _.ExternalHttpEndpoints(state: ResourceNode) =
        { state with Settings = state.Settings @ [ "external_http_endpoints" ] }

    member _.Run(state) : Resource<'Kind> = Resource state

let redis name = ResourceBuilder<Redis>(name, "redis")
let project name path = ResourceBuilder<Project>(name, "project:" + path)

type HostOption = Args of string array
let withArgs args = Args args

type AppItem =
    | Option of HostOption
    | Root of ResourceNode

type Application =
    { Args: string array
      Resources: ResourceNode list }

let private materialize items =
    let byName = Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
    let visited = HashSet<Guid>()
    let visiting = HashSet<Guid>()
    let ordered = ResizeArray<ResourceNode>()

    let rec visit resource =
        match byName.TryGetValue(resource.Name) with
        | true, existing when existing <> resource.Id ->
            invalidOp $"Conflicting resource name: {resource.Name}"
        | _ -> byName[resource.Name] <- resource.Id

        if visiting.Contains resource.Id then
            invalidOp $"Dependency cycle at: {resource.Name}"

        if not (visited.Contains resource.Id) then
            visiting.Add resource.Id |> ignore
            List.iter visit resource.References
            List.iter visit resource.Waits
            visiting.Remove resource.Id |> ignore
            visited.Add resource.Id |> ignore
            ordered.Add resource

    let options = items |> List.choose (function Option(Args args) -> Some args | _ -> None)
    let args =
        match options with
        | [] -> [||]
        | [ args ] -> Array.copy args
        | _ -> invalidOp "withArgs can appear only once."

    items |> List.iter (function Root resource -> visit resource | _ -> ())
    { Args = args; Resources = List.ofSeq ordered }

type DistributedAppBuilder() =
    member _.Yield(resource: Resource<'Kind>) = [ Root(node resource) ]
    member _.Yield(option: HostOption) = [ Option option ]
    member _.Zero() : AppItem list = []
    member _.Combine(left, right: unit -> AppItem list) = left @ right ()
    member _.Delay(body: unit -> AppItem list) = body
    member _.Run(body: unit -> AppItem list) = body () |> materialize
    member _.For(values: seq<'T>, body: 'T -> AppItem list) =
        values |> Seq.collect body |> Seq.toList
    member _.YieldFrom(items: AppItem list) = items

let distributedApp = DistributedAppBuilder()

// No host argument, let!, explicit yield, or add operation.
let app = distributedApp {
    withArgs [| "--environment"; "Development" |]

    let cache = redis "cache" {
        data_volume
    }

    let api = project "api" "../Api/Api.fsproj" {
        reference cache
        wait_for cache
        external_http_endpoints
    }

    project "web" "../Web/Web.fsproj" {
        reference api
        reference cache
        wait_for api
    }
}

let names app = app.Resources |> List.map _.Name
let check label condition =
    if not condition then failwith $"FAIL: {label}"
    printfn "PASS: %s" label

let rejects label expected action =
    let error =
        try
            action ()
            None
        with :? InvalidOperationException as error -> Some error.Message
    check label (error = Some expected)

check "implicit yield registers transitive dependencies in order"
    (names app = [ "cache"; "api"; "web" ])
check "arguments reach the application"
    (app.Args = [| "--environment"; "Development" |])
check "reference and wait remain separate"
    (app.Resources[1].References.Length = 1 && app.Resources[1].Waits.Length = 1)

let shared = redis "shared" { data_volume }
let composed = distributedApp {
    shared
    shared
    if true then
        project "conditional" "Conditional.fsproj" { reference shared }
    if false then
        project "excluded" "Excluded.fsproj" { reference shared }
    for index in 1..2 do
        project $"worker-{index}" "Worker.fsproj" { reference shared }
}
check "multiple roots, conditionals, loops, and shared dependency deduplication"
    (names composed = [ "shared"; "conditional"; "worker-1"; "worker-2" ])

let unused = distributedApp {
    let unregistered = redis "unused" { data_volume }
    shared
}
check "ordinary let does not register an unused description" (names unused = [ "shared" ])

let waitOnly = distributedApp {
    project "waiter" "Waiter.fsproj" { wait_for shared }
}
check "wait-only dependency registers without adding a reference"
    (names waitOnly = [ "shared"; "waiter" ] && waitOnly.Resources[1].References.IsEmpty)

rejects "conflicting descriptions fail" "Conflicting resource name: duplicate" (fun () ->
    distributedApp {
        redis "duplicate" { data_volume }
        redis "duplicate" { data_volume }
    } |> ignore)

rejects "duplicate host arguments fail" "withArgs can appear only once." (fun () ->
    distributedApp {
        withArgs [| "first" |]
        withArgs [| "second" |]
    } |> ignore)

printfn "\nApplication registration order: %s" (String.concat " -> " (names app))
