using BenchmarkDotNet.Running;

namespace Sitecore.API.Foundation.Authorization.Benchmarks;

public class Program
{
    public static void Main(string[] args)
    {
        BenchmarkRunner.Run<TokenCacheBenchmarks>();
    }
}
