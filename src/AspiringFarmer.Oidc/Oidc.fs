namespace AspiringFarmer.Oidc

open System
open System.Text.Encodings.Web
open System.Threading.Tasks
open Microsoft.AspNetCore.Antiforgery
open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Authentication.Cookies
open Microsoft.AspNetCore.Authentication.OpenIdConnect
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.IdentityModel.Protocols.OpenIdConnect

[<RequireQualifiedAccess>]
module Oidc =
    let private sessionScheme = "AspiringFarmer.Oidc.Session"
    let private providerScheme = "AspiringFarmer.Oidc.Provider"

    /// Registers a confidential web client using the Identity configuration section.
    let add (builder: WebApplicationBuilder) =
        let identity = builder.Configuration.GetSection "Identity"

        let required key =
            match identity[key] with
            | value when not (String.IsNullOrWhiteSpace value) -> value
            | _ -> invalidOp $"Missing OIDC setting Identity:{key}."

        let authority = required "Authority"
        let clientId = required "ClientId"
        let clientSecret = required "ClientSecret"

        match Uri.TryCreate(authority, UriKind.Absolute) with
        | true, uri when uri.Scheme = Uri.UriSchemeHttps && not (String.IsNullOrEmpty uri.Host) -> ()
        | _ -> invalidOp "Identity:Authority must be an absolute HTTPS URL."

        builder.Services
            .AddAuthentication(fun options ->
                options.DefaultScheme <- sessionScheme
                options.DefaultChallengeScheme <- providerScheme)
            .AddCookie(sessionScheme, fun options -> options.Cookie.SecurePolicy <- CookieSecurePolicy.Always)
            .AddOpenIdConnect(providerScheme, fun options ->
                options.Authority <- authority
                options.ClientId <- clientId
                options.ClientSecret <- clientSecret
                options.SignInScheme <- sessionScheme
                options.ResponseType <- OpenIdConnectResponseType.Code
                options.UsePkce <- true
                options.SaveTokens <- true
                options.CallbackPath <- PathString "/signin-oidc"
                options.SignedOutCallbackPath <- PathString "/signout-callback-oidc"

                for scope in identity.GetSection("Scopes").GetChildren() do
                    if not (String.IsNullOrWhiteSpace scope.Value) && not (options.Scope.Contains scope.Value) then
                        options.Scope.Add scope.Value)
        |> ignore

        builder.Services.AddAuthorization() |> ignore
        builder.Services.AddAntiforgery(fun options -> options.Cookie.SecurePolicy <- CookieSecurePolicy.Always)
        |> ignore

    let private returnHome (context: HttpContext) =
        AuthenticationProperties(RedirectUri = context.Request.PathBase.Add(PathString "/").Value)

    /// Adds sign-in and sign-out routes. The OIDC middleware handles the protocol callbacks.
    let map (app: WebApplication) =
        app.MapGet(
            "/auth/sign-in",
            Func<HttpContext, IResult>(fun context -> Results.Challenge(returnHome context, [| providerScheme |]))
        ).AllowAnonymous()
        |> ignore

        app.MapGet(
            "/auth/sign-out",
            Func<HttpContext, IAntiforgery, IResult>(fun context antiforgery ->
                let tokens = antiforgery.GetAndStoreTokens context
                let field = HtmlEncoder.Default.Encode tokens.FormFieldName
                let token = HtmlEncoder.Default.Encode tokens.RequestToken

                Results.Content(
                    $"""<form method="post"><input type="hidden" name="{field}" value="{token}"><button type="submit">Sign out</button></form>""",
                    "text/html"
                ))
        ).RequireAuthorization()
        |> ignore

        app.MapPost(
            "/auth/sign-out",
            Func<HttpContext, IAntiforgery, Task<IResult>>(fun context antiforgery ->
                task {
                    try
                        do! antiforgery.ValidateRequestAsync context
                        return Results.SignOut(returnHome context, [| sessionScheme; providerScheme |])
                    with :? AntiforgeryValidationException ->
                        return Results.BadRequest()
                })
        ).RequireAuthorization()
        |> ignore
