using Aspire.Hosting;
using AspiringFarmer.Benchmarks;

return Measure.Run(args, NativeCSharpModel.Create);

namespace AspiringFarmer.Benchmarks
{
    public static class NativeCSharpModel
    {
        public static IDistributedApplicationBuilder Create(int count)
        {
            var builder = DistributedApplication.CreateBuilder([]);
            var cache = builder.AddRedis("cache").WithDataVolume();
            for (var index = 1; index < count; index++)
            {
                builder.AddProject($"service-{index}", Measure.ServicePath)
                    .WithReference(cache)
                    .WaitFor(cache)
                    .WithHttpEndpoint();
            }
            return builder;
        }
    }
}
