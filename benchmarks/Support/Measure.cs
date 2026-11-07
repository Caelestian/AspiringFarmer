using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace AspiringFarmer.Benchmarks;

public static class Measure
{
    public static string ServicePath => Environment.GetEnvironmentVariable("BENCH_SERVICE_PROJECT")
        ?? throw new InvalidOperationException("Set BENCH_SERVICE_PROJECT to the sample Service.fsproj path.");

    // Only the external process boundary is timed by Python. Runtime measurements use BenchmarkDotNet.
    public static int Run(string[] args, Func<int, IDistributedApplicationBuilder> create)
    {
        var count = int.Parse(args[0]);
        if (count < 2) throw new ArgumentOutOfRangeException(nameof(args));
        var builder = create(count);
        using var app = builder.Build();
        Console.WriteLine(JsonSerializer.Serialize(new { count, fingerprint = Fingerprint(builder), runtime = Environment.Version.ToString() }));
        return 0;
    }

    public static string Fingerprint(IDistributedApplicationBuilder builder)
    {
        var resources = builder.Resources.OrderBy(resource => resource.Name).Select(resource => new
        {
            resource.Name,
            type = resource.GetType().FullName,
            annotations = resource.Annotations.Select(annotation => annotation switch
            {
                WaitAnnotation wait => $"wait:{wait.Resource.Name}:{wait.WaitType}",
                ResourceRelationshipAnnotation relationship => $"relationship:{relationship.Resource.Name}:{relationship.Type}",
                EndpointAnnotation endpoint => $"endpoint:{endpoint.Name}:{endpoint.TargetPort}:{endpoint.IsExternal}",
                _ => annotation.GetType().FullName
            }).Order().ToArray()
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(resources))));
    }
}
