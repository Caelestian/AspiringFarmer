# Implicit yield prototype

This executable F# script checks the proposed authoring syntax without an Aspire dependency.
Run it from the repository root with an SDK that includes F# Interactive:

```sh
dotnet fsi prototypes/ImplicitYield/Prototype.fsx
```

The repository pins .NET SDK 11.0.100. That SDK was unavailable on the prototype machine.
The script passed using the installed .NET 10.0.400 F# Interactive directly:

```sh
dotnet /usr/local/share/dotnet/sdk/10.0.400/FSharp/fsi.dll prototypes/ImplicitYield/Prototype.fsx
```

The SDK pin remains unchanged. The absolute command above is specific to that machine.

## Findings

- Bare resource expressions invoke the outer builder's generic `Yield` method implicitly.
- Local `let` bindings create descriptions without registering them.
- Yielded resources include dependencies declared through `reference` and `wait_for`.
- Registration visits dependencies first and deduplicates shared description identities.
- Distinct descriptions with the same name fail, including case-insensitive conflicts.
- Multiple yielded roots, conditionals, and loops work.
- `withArgs` is a function returning a host option. Overloaded `Yield` collects that option before materialization.
- References and readiness dependencies remain separate in the resulting model.

The script runs eight checks and prints `cache -> api -> web` as the sample registration order.
The cycle guard exists defensively; the immutable public description API does not expose a way to construct a cycle.

## Limits and next step

The model records registrations; it does not create or run a native Aspire AppHost.
Resource-kind markers survive bindings, but this prototype does not restrict operations by Aspire capability interfaces.
For example, its shared resource builder currently exposes `data_volume` on project descriptions too.

The next experiment should replace recorded registration with native Aspire resource creation.
Resolve typed descriptions to native builders inside a per-application registry, preserving shared identity and deferred values.
Verify native callbacks and third-party resource descriptions before adopting the architecture for the library.

Do not infer performance results from this syntax experiment. Its list concatenation and graph representation are provisional.
