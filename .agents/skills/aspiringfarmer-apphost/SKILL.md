---
name: aspiringfarmer-apphost
description: Create or edit F# Aspire AppHosts that use the AspiringFarmer DSL, including project references, schema jobs, startup waits, mail, and OIDC configuration.
---

# AspiringFarmer AppHosts

Read the target AppHost's F# and project file. Use the [DSL reference](../../../src/README.md) and the [SharedPostgres AppHost](../../../samples/SharedPostgres/AppHost/Program.fs) to find the operations available on this checkout.

## Declare the resources

- When the AppHost imports the [MSBuild generator](../../../samples/SharedPostgres/AppHost/ProjectReferences.targets), add or update the named `ProjectReference` in its project file. Use `AspiringFarmerName` for the generated `Projects.*` member. Copy the project reference metadata from [SharedPostgres](../../../samples/SharedPostgres/AppHost/AppHost.fsproj). Use `project_ref` and `schema_project_ref` with generated members. Use `project` and `schema_project` with paths in hosts that use path-based declarations.
- Pair a PostgreSQL database with its migration or seed job using `schema_project` or `schema_project_ref`. The job waits for the database and receives `ConnectionStrings__appdb` by default. Use `use_database` in each dependent project to pass the connection string and wait for a successful schema job. Reuse the same database declaration when several projects share the job. Use `reference` for a connection without a schema completion dependency.
- Keep endpoints and health probes explicit. Use `http_health_check` with the name of the declared HTTP or HTTPS endpoint. Use `configure` for native Aspire options that the DSL does not expose.

## Wire configuration

- Use `mail_reference` for configuration-selected mail. The `mail` builder reads `Mail:Transport` by default. Mailpit is registered in run mode when selected and given a `mailpit_inbox`. See the [mail setup](../../../samples/SharedPostgres/README.md#send-mail) for production parameter names.
- Use `oidc_reference` to pass settings to an OIDC client. In an `oidc` expression, `options_from_configuration "Identity"` reads `Identity:Enabled` and scopes. The DSL defaults an omitted flag to enabled, and SharedPostgres explicitly defaults it to disabled. See the [sign-in setup](../../../samples/SharedPostgres/README.md#sign-in-to-storefront) for the Aspire parameters and callback URLs.
- Supply credentials through Aspire secret parameters or the deployment's secret configuration. Check that published configuration retains references to secrets.

## Verify the change

Build the affected solution or AppHost. For SharedPostgres, run `dotnet build samples/SharedPostgres/SharedPostgres.slnx` from the repository root.

Choose checks that cover the change. Use `python3 tests/Compiler/check.py` for DSL compiler behavior, `python3 tests/Mail/check.py` for mail transport and publish configuration, or `python3 tests/Oidc/check.py` for OIDC enablement and manifest settings. For changes to startup order or local delivery, use the [SharedPostgres run or smoke commands](../../../samples/SharedPostgres/README.md#run-the-sample) with a running container engine. Confirm the expected resources reach readiness and the changed dependency or delivery path works.
