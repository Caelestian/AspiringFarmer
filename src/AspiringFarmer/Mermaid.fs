namespace AspiringFarmer

open System
open Aspire.Hosting
open Aspire.Hosting.ApplicationModel

module Mermaid =
    let private label (text: string) =
        text
        |> Seq.map (fun character ->
            if
                Char.IsLetterOrDigit character
                || character = ' '
                || character = '-'
                || character = '_'
            then
                string character
            else
                $"#{int character};")
        |> String.concat ""

    /// Exports resource names and dependency annotations without evaluating resource values.
    let export (builder: IDistributedApplicationBuilder) =
        let resources = builder.Resources |> Seq.sortBy _.Name |> Seq.toArray

        let ids =
            resources
            |> Array.mapi (fun index resource -> resource.Name, $"resource{index}")
            |> Map.ofArray

        let edges =
            seq {
                for resource in resources do
                    match resource with
                    | :? IResourceWithParent as child -> yield resource.Name, child.Parent.Name, "parent"
                    | _ -> ()

                    match resource with
                    | :? IResourceWithConnectionString as connection ->
                        for provider in connection.ConnectionStringExpression.ValueProviders do
                            match provider with
                            | :? ParameterResource as parameter when parameter.Secret ->
                                yield resource.Name, parameter.Name, "uses secret"
                            | _ -> ()
                    | _ -> ()

                    for annotation in resource.Annotations do
                        match annotation with
                        | :? ResourceRelationshipAnnotation as relationship when
                            not (String.Equals(relationship.Type, "WaitFor", StringComparison.OrdinalIgnoreCase))
                            ->
                            yield resource.Name, relationship.Resource.Name, relationship.Type.ToLowerInvariant()
                        | :? WaitAnnotation as wait ->
                            let kind =
                                match wait.WaitType with
                                | WaitType.WaitForCompletion -> $"waits for exit {wait.ExitCode}"
                                | WaitType.WaitUntilHealthy -> "waits for health"
                                | _ -> "waits for start"

                            yield resource.Name, wait.Resource.Name, kind
                        | _ -> ()
            }
            |> Seq.distinct
            |> Seq.sort

        seq {
            yield "flowchart LR"

            for resource in resources do
                yield $"    {ids[resource.Name]}[\"{label resource.Name}\"]"

            for source, target, relation in edges do
                match Map.tryFind target ids with
                | Some targetId -> yield $"    {ids[source]} -->|\"{label relation}\"| {targetId}"
                | None -> ()
        }
        |> String.concat "\n"
