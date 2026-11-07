using Aspire.Hosting;
using AspiringFarmer.Benchmarks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

// Child benchmark processes inherit this absolute path.
if (Environment.GetEnvironmentVariable("BENCH_SERVICE_PROJECT") is null)
{
    var directory = new DirectoryInfo(Environment.CurrentDirectory);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AspiringFarmer.slnx")))
        directory = directory.Parent;
    if (directory is null) throw new InvalidOperationException("Run from the repository or set BENCH_SERVICE_PROJECT.");
    Environment.SetEnvironmentVariable("BENCH_SERVICE_PROJECT", Path.Combine(directory.FullName, "prototypes/Service/Service.fsproj"));
}

if (args.Contains("--verify"))
{
    foreach (var count in new[] { 5, 25, 100 })
    {
        var fingerprints = new HashSet<string>();
        foreach (var implementation in Enum.GetValues<Implementation>())
        {
            var builder = Models.Create(implementation, count);
            fingerprints.Add(Measure.Fingerprint(builder));
            using var app = builder.Build();
        }
        if (fingerprints.Count != 1) throw new InvalidOperationException($"Models differ at {count} resources.");
        Console.WriteLine($"PASS: equivalent BenchmarkDotNet factories at {count} resources");
    }
    return;
}

var summaries = BenchmarkSwitcher.FromAssembly(typeof(ConstructionBenchmarks).Assembly).Run(args).ToArray();
if (summaries.Length == 0 || summaries.Any(summary => summary.HasCriticalValidationErrors || summary.Reports.Any(report => !report.Success)))
    Environment.ExitCode = 1;

public enum Implementation { NativeCSharp, NativeFSharp, DslFSharp }

public static class Models
{
    public static IDistributedApplicationBuilder Create(Implementation implementation, int count) => implementation switch
    {
        Implementation.NativeCSharp => NativeCSharpModel.Create(count),
        Implementation.NativeFSharp => NativeFSharpModel.create(count),
        Implementation.DslFSharp => DslFSharpModel.create(count),
        _ => throw new ArgumentOutOfRangeException(nameof(implementation))
    };
}

[MemoryDiagnoser]
public class ConstructionBenchmarks
{
    [Params(5, 25, 100)] public int Count { get; set; }
    [ParamsAllValues] public Implementation Implementation { get; set; }
    private IDistributedApplicationBuilder? builder;

    [Benchmark]
    public IDistributedApplicationBuilder Construct() => builder = Models.Create(Implementation, Count);

    // One invocation per iteration prevents accumulating unowned builders.
    // Native host construction/disposal is outside the timed construction interval.
    [IterationCleanup]
    public void Cleanup()
    {
        if (builder is not null)
        {
            using var app = builder.Build();
            builder = null;
        }
    }
}

[MemoryDiagnoser]
public class HostBuildBenchmarks
{
    [Params(5, 25, 100)] public int Count { get; set; }
    [ParamsAllValues] public Implementation Implementation { get; set; }
    private IDistributedApplicationBuilder builder = null!;
    private DistributedApplication? app;

    [IterationSetup]
    public void Setup() => builder = Models.Create(Implementation, Count);

    [Benchmark]
    public DistributedApplication Build() => app = builder.Build();

    [IterationCleanup]
    public void Cleanup()
    {
        app?.Dispose();
        app = null;
    }
}
