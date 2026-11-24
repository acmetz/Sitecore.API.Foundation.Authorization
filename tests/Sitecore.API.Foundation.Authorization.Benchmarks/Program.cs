using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

namespace Sitecore.API.Foundation.Authorization.Benchmarks;

public static class Program
{
    public static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            var defaultConfig = ManualConfig.Create(DefaultConfig.Instance)
                .AddJob([.. new SimpleJobAttribute(RuntimeMoniker.Net10_0).Config.GetJobs(), .. new SimpleJobAttribute(RuntimeMoniker.Net11_0).Config.GetJobs()]);
            BenchmarkRunner.Run<TokenCacheBenchmarks>(defaultConfig);
            return;
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
