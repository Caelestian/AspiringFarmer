# Container endpoint sample

The core DSL's `container` builder takes a resource name, image, and tag. This sample exposes nginx port 80 as a named HTTP endpoint. The API binds that endpoint to `UPSTREAM_URL` with `endpoint_environment`.

The `endpoint` function accepts descriptions of resources that support endpoints. Use the endpoint name declared on the resource. Aspire allocates the host port and resolves the URL when it starts the API.

Run from the repository root with a container engine available.

```sh
dotnet run --project prototypes/Container
```

In the Aspire dashboard, check that nginx becomes healthy and the API follows. The API's `/health` route returns 200 after the upstream request succeeds and 503 when it fails.

To inspect the deferred URL, publish a local manifest.

```sh
dotnet build prototypes/Container
dotnet prototypes/Container/bin/Debug/net10.0/Container.dll --publisher manifest --output-path /tmp/aspiringfarmer-container.json
```

The API environment entry `UPSTREAM_URL` contains `{upstream.bindings.http.url}`.
