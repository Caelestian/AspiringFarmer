using System.Diagnostics;
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

    public static int Run(string[] args, Func<int, IDistributedApplicationBuilder> create)
    {
        var count = int.Parse(args[0]);
        var iterations = int.Parse(args[1]);
        if (count < 2 || iterations < 1) throw new ArgumentOutOfRangeException(nameof(args));
        // A single iteration deliberately has no warmup: a new process reports its first model.
        var warmups = iterations == 1 ? 0 : 3;
        var samples = new List<object>();
        string? fingerprint = null;
        for (var index = -warmups; index < iterations; index++)
        {
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var timer = Stopwatch.StartNew();
            var builder = create(count);
            var constructionMs = timer.Elapsed.TotalMilliseconds;
            var constructionBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            timer.Restart();
            using var app = builder.Build();
            var buildMs = timer.Elapsed.TotalMilliseconds;
            if (index >= 0)
            {
                samples.Add(new { constructionMs, constructionBytes, buildMs });
                fingerprint ??= Fingerprint(builder);
            }
        }
        Console.WriteLine(JsonSerializer.Serialize(new { count, iterations, fingerprint, samples }));
        return 0;
    }

    private static string Fingerprint(IDistributedApplicationBuilder builder)
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
