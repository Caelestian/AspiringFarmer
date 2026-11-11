# Shared PostgreSQL AppHost

The [AppHost](AppHost/Program.fs) declares one PostgreSQL server and five databases. Each database has one F# console project that applies its schema and seeds one row in a transaction. Each web app waits for its three schema jobs to exit successfully.

| Web app | Databases and schema jobs |
| --- | --- |
| Storefront | Identity, Catalog, Orders |
| Back Office | Identity, Inventory, Reporting |

Both apps share the Identity database and wait on the same Identity schema job. Each app's `/health` endpoint queries the seeded row in all three of its databases.
Storefront serves HTTPS. Back Office serves HTTP. Aspire probes each app through its declared endpoint.

The schema jobs read their connection string from `ConnectionStrings__appdb`. The normal AppHost uses a data volume and saves the generated PostgreSQL password in .NET user secrets. The schema and seed SQL can run again against an existing volume.

## Run the sample

Use the .NET 10 SDK, the Aspire CLI, and a running Docker engine. Trust the ASP.NET Core development certificate before starting the sample. Run these commands from the repository root.

```sh
dotnet dev-certs https --trust
dotnet build samples/SharedPostgres/SharedPostgres.slnx
dotnet run --project samples/SharedPostgres/AppHost --no-build
```

Open the Aspire dashboard from the terminal output. The five schema jobs finish and the two web apps become healthy. Use the dashboard's endpoint links to open the web apps.

The sample was verified with two successive launches against the same PostgreSQL volume. Both apps passed their health checks on the second launch.

## Sign in to Storefront

Storefront enables the OIDC routes when it receives an `Identity` configuration section. Supply `Identity__Authority`, `Identity__ClientId`, and `Identity__ClientSecret` in the Storefront process environment. Register the Storefront HTTPS URLs ending in `/signin-oidc` and `/signout-callback-oidc` with your provider. Open `/auth/sign-in` to sign in and `/auth/sign-out` to open the sign-out form. See the [OIDC helper configuration](../../src/README.md).

## Send mail

The local launch profile selects Mailpit. Open the Storefront and Mailpit links in the Aspire dashboard. Send the fixed sample message with `curl -X POST <storefront-url>/mail/test`, then open the Mailpit inbox.

Run the delivery smoke check after building the solution. It starts temporary databases, posts to Storefront over HTTPS, and checks that Mailpit captured the message.

```sh
DOTNET_ENVIRONMENT=Development ASPNETCORE_URLS=http://127.0.0.1:0 ASPIRE_ALLOW_UNSECURED_TRANSPORT=true \
  dotnet samples/SharedPostgres/AppHost/bin/Debug/net10.0/AppHost.dll --verify-mail
```

### Configure a production transport

Set `Mail__Transport` on the AppHost before publishing. Supply the selected transport's parameters through your deployment configuration. The AppHost declares the password or client secret as a secret parameter.
Prefix each parameter name with `Parameters:` in .NET configuration, such as `Parameters:mail-from`. The equivalent environment key is `Parameters__mail-from`.

| Transport | AppHost parameter names | Delivery |
| --- | --- | --- |
| `smtp` | `mail-from`, `mail-smtp-host`, `mail-smtp-username`, `mail-smtp-password` | External authenticated SMTP |
| `exchange-smtp` | `mail-from`, `mail-tenant-id`, `mail-client-id`, `mail-client-secret` | Exchange Online SMTP with app-only OAuth |
| `graph` | `mail-from`, `mail-tenant-id`, `mail-client-id`, `mail-client-secret` | Microsoft Graph `sendMail` |

External SMTP defaults to port 587 with required STARTTLS. Set `Mail__Smtp__Port` and `Mail__Smtp__Tls=ssl-on-connect` for providers that require implicit TLS. Use provider-issued SMTP credentials for services such as Amazon SES. Microsoft 365 High Volume Email also fits this transport with HVE account credentials and same-tenant recipients. [Amazon SES SMTP setup](https://docs.aws.amazon.com/ses/latest/dg/smtp-connect.html) · [Microsoft 365 HVE setup](https://learn.microsoft.com/en-us/exchange/mail-flow-best-practices/high-volume-mails-m365)

For `exchange-smtp`, use a licensed sender mailbox with SMTP AUTH enabled. Configure the Entra application for `SMTP.SendAsApp` and grant the Exchange service principal access to send as that mailbox. [Exchange SMTP OAuth setup](https://learn.microsoft.com/en-us/exchange/client-developer/legacy-protocols/how-to-authenticate-an-imap-pop-smtp-application-by-using-oauth)

For `graph`, grant the application's mail send role through Exchange application RBAC and scope it to the sender mailbox. Avoid a separate tenant-wide Entra mail send grant, which would broaden the application's access. [Exchange application RBAC](https://learn.microsoft.com/en-us/exchange/permissions-exo/application-rbac) · [Graph sendMail](https://learn.microsoft.com/en-us/graph/api/user-sendmail?view=graph-rest-1.0)

To configure Storefront directly, set `Mail__Transport` and `Mail__From`. For external SMTP, set `Mail__Smtp__Host`, `Mail__Smtp__Username`, and `Mail__Smtp__Password`. For Exchange OAuth or Graph, set `Mail__TenantId`, `Mail__ClientId`, and `Mail__ClientSecret`.

Supply secrets through the environment of the deployed app. Run the following command with a recipient you control to test the configured transport.

```sh
dotnet Storefront.dll --send-test your-test-address@example.com
```

The command reports acceptance by the mail service. Check the recipient inbox to confirm delivery. Mailpit and the sample HTTP send route are available in the local development configuration.

The AppHost omits Mailpit when publishing. Select a production transport explicitly. To generate a Graph manifest from the compiled AppHost, run this command.

```sh
Mail__Transport=graph dotnet samples/SharedPostgres/AppHost/bin/Debug/net10.0/AppHost.dll \
  --publisher manifest --output-path artifacts/shared-postgres-graph.json
```

Run the transport and publish checks with `python3 tests/Mail/check.py`. The checks verify Graph requests against an in-process HTTP handler and inspect each published configuration. Live production delivery requires your provider credentials and an authorized recipient.

## Benchmark

Run the benchmark with Python 3.9 or later and a running Docker engine.

```sh
python3 samples/SharedPostgres/benchmark.py --runs 5
```

The benchmark restores packages and performs one untimed build and startup. Each measured run cleans the solution, times a Debug build, then times an immediate no-change build. Both build timings exclude package restore.

The benchmark launches the compiled AppHost in the Development environment and measures wall-clock time from process launch. AppHost startup ends when Aspire's `StartAsync` returns. Resource readiness ends when Aspire confirms the following conditions.

- PostgreSQL and its five database resources are ready.
- All five schema jobs exited with code 0.
- Both web apps passed their database-backed HTTP health checks.

Each startup uses fresh PostgreSQL storage and a cached image, with mail disabled. The AppHost stops after the readiness check. The benchmark writes individual measurements, resource timings, and medians to `artifacts/shared-postgres.json`.

### Measured results

Five runs on November 10, 2026 used an Apple M2 with 16 GB of memory, macOS 27.2, and .NET SDK 10.0.400. The AppHost used Aspire 13.6.4 and the local Aspire 13.5.0 CLI bundle. OrbStack ran the cached arm64 `postgres:18.3` image.

| Milestone | Median | Range |
| --- | ---: | ---: |
| Clean solution build | 5.31 s | 4.94–6.97 s |
| No-change solution build | 0.82 s | 0.79–0.93 s |
| AppHost started | 9.80 s | 9.60–10.10 s |
| All 13 application resources ready | 12.22 s | 11.72–12.32 s |

Both runtime timings start at the same process launch. See the [original five runs with native configuration callbacks](../../benchmarks/results/2026-11-10-shared-postgres.json).

After both apps switched to `http_endpoint` and `http_health_check`, a second five-run pass recorded medians of 9.78 s for AppHost startup and 12.65 s for resource readiness. All 13 resources reached readiness in every run. See the [DSL run data and individual resource timings](../../benchmarks/results/2026-11-10-shared-postgres-http-dsl.json).
