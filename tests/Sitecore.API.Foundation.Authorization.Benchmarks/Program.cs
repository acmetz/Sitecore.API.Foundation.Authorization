using BenchmarkDotNet.Running;

namespace Sitecore.API.Foundation.Authorization.Benchmarks;

public static class Program
{
    public static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            BenchmarkRunner.Run<TokenCacheBenchmarks>();
            return;
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
