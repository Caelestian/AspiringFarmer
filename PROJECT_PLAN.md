# AspiringFarmer project plan

Status: proposal for discussion. No DSL or performance claims have been validated yet.

## Purpose

AspiringFarmer provides a typed F# domain-specific language for Aspire AppHosts. Developers should be able to read the application topology directly from `Program.fs`.

The library should preserve access to Aspire capabilities while reducing the ceremony of common resource declarations. Build and startup overhead must remain measurable and small.

The initial audience is F# developers who already understand projects, containers, and service dependencies.

## Recommended direction

Use Farmer-style computation expressions over the native Aspire hosting APIs. Keep Aspire responsible for orchestration, resource lifetimes, service discovery, health, dashboard integration, and publishing.

Preserve native `IResourceBuilder<'Resource>` values at interoperability boundaries. Avoid runtime reflection, dynamic resource lookup, and a separate orchestration engine.

Use one outer `distributedApp` computation expression to own host creation and resource registration. Resource expressions must not require an explicit application-builder argument.

Nested resource expressions produce typed registration descriptions. The outer expression binds each description against its application builder and returns a native resource builder.

Prototype `let!` for registration and typed resource bindings. Ordinary `let` binds reusable descriptions or local values without registering a resource.

Keep the application builder explicit inside the implementation. Do not use global state or ambient mutable context to connect nested expressions.

## Meaning of full capability

Separate capability access from dedicated DSL syntax:

- Every supported public Aspire hosting operation should remain accessible through native builders or a documented F# compatibility adapter.
- Common operations should have typed DSL syntax and examples.
- Integrations without dedicated syntax should work through ordinary Aspire packages and custom resource factories.
- Experimental APIs should retain their upstream warnings and version requirements.

The first release will not provide custom syntax for every Aspire integration. Publish a coverage table that distinguishes DSL support, native interoperability, unverified behavior, and known limitations.

Do not claim complete compatibility until representative third-party integrations, custom resources, callbacks, and publishing paths pass verification.

## Proposed authoring experience

The following code is a design sketch, not a working API. Names and argument forms need compiler validation.

```fsharp
open AspiringFarmer

[<EntryPoint>]
let main args =
    let app = distributedApp {
        withArgs args

        let! cache = redis "cache" {
            data_volume
        }

        let! databaseServer = postgres "postgres" {
            data_volume
        }

        let! database = database databaseServer "appdb"

        let! api = project "api" "../Api/Api.fsproj" {
            reference cache
            reference database
            wait_for cache
            wait_for database
            external_http_endpoints
        }

        let! frontend = project "frontend" "../Web/Web.fsproj" {
            reference api
            wait_for api
        }
    }

    app.Run()
    0
```

The outer expression creates and builds the application. Running the result starts it, preserving a separate boundary for tests and publishing workflows.

Each `let!` registers a resource once and binds its native typed builder for later declarations. Here, binding represents registration, not asynchronous execution.

An ordinary `let` cannot implicitly register a description with the enclosing expression. Confirm that this distinction produces understandable compiler errors and documentation.

Prototype the interaction between `withArgs`, `let!`, local bindings, and the final expression. F# custom-operation rules may require implementation changes or syntax adjustments.

Collect host options before creating the native application builder. The implementation must pass arguments to host creation before registering resources.

Keep connection references and readiness dependencies separate. Referencing a resource must not silently add a wait condition.

Use ordinary F# functions to create resource descriptions. Add a typed composition operation for reusable groups that register several resources and return their builders.

Provide explicit native callback operations for both the host and individual resources. Native access must work without manually passing the host through every declaration.

Support project paths first if that provides the simplest working F# AppHost. Evaluate generated `Projects.*` metadata as an additional authoring option.

The path form does not guarantee project existence at compile time. Document that distinction from typed resource references.

## Type design and validation

Preserve each concrete Aspire resource type through declaration and configuration. A database reference must not become an untyped resource name.

Constrain operations using Aspire capability interfaces. Environment operations should require environment support; connection and endpoint references need their respective constraints.

F# does not automatically apply the generic covariance used by some Aspire APIs. Evaluate existing compatibility overloads before creating new adapters.

Prefer ordinary generic constraints over statically resolved type parameters where possible. Check compiler errors and editor completion before adding complex generic abstractions.

Use compile-time checks for resource capabilities. Use deterministic validation for duplicate names, invalid literal settings, and unsupported combinations where the DSL has enough information.

Leave lifecycle validation and dynamic state to Aspire. Do not promise compile-time detection of runtime health failures or dynamically created dependency cycles.

Native callbacks can bypass DSL validation. Explain which guarantees apply to DSL operations and which rely on Aspire.

## Existing work to evaluate

[Farmer](https://compositionalit.github.io/farmer/) provides the reference for readable resource expressions and typed relationships. Borrow its authoring conventions where they fit an executable AppHost.

[FSharp.Aspire.Hosting](https://github.com/Tarmil/FSharp.Aspire.Hosting) already provides F# AppHost tooling and compatibility overloads. Evaluate its packages, metadata generation, maintenance, and version compatibility.

Prefer a dependency or contribution where practical. Record a concrete reason before implementing equivalent tooling locally.

[Aspire project resources](https://aspire.dev/integrations/dotnet/project-resources/) documents project references, generated metadata, and project build coordination. Verify behavior against the pinned release rather than assuming current documentation matches it.

## Package structure

Begin with the smallest useful set of projects:

| Project | Responsibility |
| --- | --- |
| `AspiringFarmer` | Host helpers, project/container/executable expressions, shared typed operations, native interoperability |
| `AspiringFarmer.Redis` | Optional Redis expressions and its hosting dependency |
| `AspiringFarmer.PostgreSQL` | Optional PostgreSQL expressions and its hosting dependency |
| `AspiringFarmer.Tests` | Compiler checks, application-model comparisons, and selected runtime tests |
| `samples/Basic.AppHost` | Executable API, frontend, Redis, and PostgreSQL example |
| `benchmarks/` | Equivalent native C#, native F#, and AspiringFarmer applications |

Add a template package after project setup and version requirements stabilize. Do not make the core package reference every hosting integration.

## Performance requirements

Treat fast startup as several measurements. The DSL controls only part of the total time.

| Measurement | Boundary |
| --- | --- |
| Clean build | Restored dependencies and clean outputs to completed AppHost build |
| No-change build | Repeated build with unchanged source and existing outputs |
| AppHost edit | One topology edit to completed incremental build |
| Resource edit | One application edit to completed required builds |
| Model construction | Host builder creation through completed application model construction |
| AppHost startup | Start of an already built AppHost process to an explicitly defined dashboard/resource-service readiness event |
| Application readiness | Start of the launch command to all selected resources reporting healthy |

Measure restore and first container image pulls separately. Also record total user-visible launch time for cold and warm workflows.

Use equivalent native C# and native F# AppHosts as baselines. The F# comparison isolates DSL overhead; the C# comparison represents the standard authoring workflow.

Test small, medium, and large models, initially 5, 25, and 100 resources. Use container-free models for construction measurements and a realistic sample for readiness measurements.

Record SDK, Aspire version, OS, hardware, resource count, cache state, and launch command. Report medians and tail latency with the repeat count.

Proposed initial budgets, subject to baseline measurements:

- No-change builds must not trigger DSL generation or unnecessary downstream project builds.
- AppHost-edit build time should remain within 10% of the equivalent native F# AppHost.
- Additional model-construction time should remain below 10 ms at 100 resources on the documented benchmark machine.
- Already built AppHost startup should remain within 5% of the native F# baseline, with an absolute tolerance derived from observed noise.

These are acceptance targets, not measured results. Revisit thresholds after collecting a reproducible baseline.

Avoid an extra process, runtime compilation, and runtime assembly scanning in the default execution path. Do not prioritize trimming or Native AOT without a compatibility experiment.

## Delivery milestones

### 0. Prove AppHost compatibility and establish baselines

Create equivalent minimal C# and F# AppHosts with pinned dependencies. Confirm the installed SDK can build the repository's selected target.

The repository currently pins SDK `11.0.100`. Decide the library target framework separately from the development SDK and verify the supported combination.

Evaluate FSharp.Aspire.Hosting, path-based projects, generated project metadata, CLI launch, dashboard startup, and one publishing workflow.

Exit when both AppHosts run the same sample and baseline build/startup results are recorded. Document any required F# tooling or compatibility adapter.

### 1. Validate the DSL with a vertical prototype

Implement project and Redis expressions, typed references, explicit waits, and native callbacks. Prototype the outer expression, typed registration descriptions, and reusable resource groups.

Add compiler checks for valid and invalid capability combinations. Compare the resulting application model with an equivalent native Aspire declaration.

Exit when the sample runs, native extensions remain usable, and error messages are understandable. Record the chosen execution model and its tradeoffs.

### 2. Release an alpha for common local applications

Add PostgreSQL databases, containers, executables, parameters, secret parameters, environment values, endpoints, volumes, and lifecycle options.

Preserve deferred Aspire expressions for endpoint and connection values. Do not resolve dynamic values early or log secret values.

Add examples for reusable topology functions, migrations with completion waits, conditional resources, and a third-party integration without a DSL adapter.

Exit when model comparisons, selected end-to-end tests, and build/startup budgets pass for the documented supported versions.

### 3. Establish extensibility and publishing compatibility

Document custom resource factories, native application-builder access, resource annotations, and callbacks. Verify representative deployment integrations and run/publish differences.

Publish the capability coverage table. Add an upgrade check against the next selected Aspire version and identify required adapter changes.

Exit when an integration author can add support without modifying the core library. List unsupported or unverified capabilities explicitly.

### 4. Prepare version 1

Stabilize naming, argument order, package boundaries, and supported version policy. Publish templates, migration guidance, API documentation, and benchmark results.

Require examples to compile in CI. Verify supported OS environments and distinguish container-dependent checks from ordinary unit tests.

Version 1 requires measured performance and verified compatibility for its documented scope. Do not equate release readiness with wrapping every upstream method.

## Verification strategy

Use compiler tests for type guarantees, including invalid reference targets and operations unavailable on a resource type.

Compare application models for endpoints, annotations, references, waits, parameters, callbacks, and publishing behavior. Normalize generated values rather than snapshotting unstable output.

Run a small end-to-end sample to verify service discovery, health waits, connection injection, and shutdown. Keep model-only tests independent of Docker.

Measure compile time alongside runtime performance. A DSL with expensive inference or poor editor feedback fails the readability goal even if execution is fast.

## Decisions to settle during the prototype

1. How should reusable groups bind multiple resources and return typed results? Keep the outer `distributedApp` expression and avoid explicit host arguments in user declarations.
2. Should project metadata generation be required? The initial recommendation is an optional typed metadata form alongside project paths.
3. Which Aspire release and target framework form the first supported pair? Pin a verified pair before expanding integration coverage.
4. Can existing F# compatibility tooling satisfy the interoperability requirement? Verify provider-specific generic constraints and packaging before choosing a dependency.
5. Which publishing integration provides the first deployment acceptance case? Choose one representative workflow and document its tested scope.

## First implementation backlog

1. Add a solution, central dependency versions, and a minimal runnable F# AppHost.
2. Add the equivalent C# and native F# benchmark applications.
3. Record project metadata and compatibility-tooling findings.
4. Compile the outer expression with `withArgs`, nested resources, local bindings, and typed `let!` registration.
5. Implement one resource description, its outer-expression binding, and native callback access.
6. Add typed reference and wait operations, including negative compiler checks.
7. Run the vertical sample and compare its model and timing with the baselines.
8. Write the architecture decision before expanding the DSL.

## Source notes

Planning research reviewed November 6, 2026. Online documentation may describe behavior newer than the selected package release.

- [Farmer documentation](https://compositionalit.github.io/farmer/)
- [Aspire documentation](https://aspire.dev/)
- [Aspire project resources](https://aspire.dev/integrations/dotnet/project-resources/)
- [Aspire hosting implementation](https://github.com/microsoft/aspire/blob/main/src/Aspire.Hosting/ProjectResourceBuilderExtensions.cs)
- [FSharp.Aspire.Hosting tooling and compatibility explanation](https://github.com/Tarmil/FSharp.Aspire.Hosting)
