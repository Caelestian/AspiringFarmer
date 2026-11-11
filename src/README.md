# Library implementation

`AspiringFarmer` contains typed descriptions, per-application registration, the outer `distributedApp` expression, project expressions, and resource-factory extension points.
`AspiringFarmer.Redis` adds Redis expressions and the Redis hosting dependency.
Both libraries target `net10.0`. Their public API is experimental.

```fsharp
open AspiringFarmer
open AspiringFarmer.Redis

let builder = distributedApp {
    withArgs args

    let cache = redis "cache" { data_volume }

    project "api" "../Api/Api.fsproj" {
        reference cache
        wait_for cache
        http_endpoint "http"
        http_health_check "http" "/health"
        external_http_endpoints
    }
}

use app = builder.Build()
app.Run()
```

Resource expressions return `Description<'T>` for their native Aspire resource type.
Ordinary bindings create descriptions; bare expressions include those descriptions and their dependencies in the application.

Project `reference` operations accept connection-string resources through a generic capability constraint.
Project descriptions also support service-discovery references to other project descriptions.
The current implementation does not yet generalize service-discovery references to every custom resource type.
Project `wait_for` requires `IResourceWithWaitSupport` on the dependency.

For projects, `http_endpoint` and `https_endpoint` add named endpoints with Aspire-assigned ports.
Use `http_health_check` with the name of an HTTP or HTTPS endpoint and a path served by that project.
Aspire treats HTTP 200 as healthy and uses the result when another project waits for this resource.

`mailpit name tag` creates a container description with a TCP endpoint named `smtp` and an HTTP endpoint named `inbox`. The inbox readiness check uses `/readyz`.
Use `endpoint_environment "Mail__Smtp__Endpoint" (endpoint "smtp" inbox)` in a project expression to pass the deferred SMTP URI. Add `wait_for inbox` to wait for Mailpit's readiness check.

Use the SMTP builder to configure a project that reads the `Mail` settings.

```fsharp
let outboundMail = smtp "mail" { options_from_configuration "Mail:Smtp" }

project "storefront" "../Storefront/Storefront.fsproj" {
    smtp_reference outboundMail
}
```

The reference passes the sender, host, username, and secret password as Aspire parameter references. Supply their values under `Parameters:mail-from`, `Parameters:mail-smtp-host`, `Parameters:mail-smtp-username`, and `Parameters:mail-smtp-password`.
The AppHost reads optional `Port` and `Tls` settings from the named configuration section when it builds the model. Set `port 465` or `tls SmtpTls.SslOnConnect` in the expression to override the corresponding setting. An omitted option leaves the client default in effect. The SharedPostgres client defaults to port 587 with STARTTLS.

Use `AppHost.buildWith` when the application definition depends on Aspire's execution mode or configuration. The function receives the native builder before it registers the definition.

Declare an OIDC client in your AppHost and reference it from a project.

```fsharp
let signIn = oidc "storefront-auth" { options_from_configuration "Identity" }

project "storefront" "../Storefront/Storefront.fsproj" {
    https_endpoint "https"
    oidc_reference signIn
}
```

Supply the issuer URL, client ID, and client secret through `Parameters:storefront-auth-authority`, `Parameters:storefront-auth-client-id`, and `Parameters:storefront-auth-client-secret`. The reference keeps all three values deferred and marks the client secret as secret. `options_from_configuration` copies optional scopes from the named section.

For a provider hosted by Aspire, use `authority_from_endpoint (endpoint "https" provider) ""` in the `oidc` expression. Pass a path such as `"/realms/example"` for a Keycloak realm. The endpoint and path must form the HTTPS issuer URL that the browser and client can reach. The provider's client registration supplies the client ID and secret.

`AspiringFarmer.Oidc` provides an ASP.NET Core client helper. Reference the project in your web application and register the services before building it.

```fsharp
open AspiringFarmer.Oidc

Oidc.add builder
let app = builder.Build()
Oidc.map app
```

Set `Identity:Authority` to the provider's HTTPS issuer URL. Set `Identity:ClientId` and supply `Identity:ClientSecret` from your secret configuration. Add optional scopes under `Identity:Scopes`.

Register the application's HTTPS `/signin-oidc` and `/signout-callback-oidc` URLs with the provider. The provider must support authorization code with PKCE and expose an end-session endpoint for provider sign-out. Use `/auth/sign-in` to start sign-in and `/auth/sign-out` to open the sign-out confirmation form. Apply `RequireAuthorization()` to routes that need a signed-in user. [ASP.NET Core OIDC configuration](https://learn.microsoft.com/aspnet/core/security/authentication/configure-oidc-web-authentication)

`ResourceBuilder<'T>` lets integrations define resource expressions without changing the core library.
Its factory receives the native application builder and returns the native resource builder.
Its `name` must match the resource name used by the factory.
Derived builders can add operations with `ResourceState<'T>.Add` and resolve typed dependencies through the per-application registry.

The core currently depends on `FSharp.Aspire.Hosting.AppHost` for F# compatibility overloads.
AppHost build assets are excluded in the core, which is a library rather than an executable host.
Executable hosts still supply the Aspire SDK and AppHost package themselves.
The core has no Redis hosting dependency.

Package versions are centralized in `Directory.Packages.props`. The executable host's Aspire SDK version must stay aligned with its hosting package version.
FSharp.Core is explicitly versioned because central package management disables its implicit SDK reference.

Name validation occurs during materialization. A failed declaration can leave a partially configured builder.
Separate validation and stable public API naming remain outstanding.
The [initial performance baseline](../benchmarks/baseline-2026-11-06.md) records model-construction overhead and source-project build costs.
