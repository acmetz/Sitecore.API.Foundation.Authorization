using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace Sitecore.API.Foundation.Tests;

/// <summary>
/// Contract and structural tests for GitHub Actions workflows (.github/workflows/*.yml)
/// to ensure pipeline configurations stay in sync with project target frameworks,
/// referenced files, symbol packaging, and NuGet OIDC Trusted Publishing requirements.
/// </summary>
public class GitHubWorkflowTests
{
    private static readonly string RepoRoot = FindRepositoryRoot();
    private static readonly string NugetReleaseWorkflowPath = Path.Combine(RepoRoot, ".github", "workflows", "nuget-release.yml");
    private static readonly string PrCheckWorkflowPath = Path.Combine(RepoRoot, ".github", "workflows", "pr-check.yml");
    private static readonly string MainCsprojPath = Path.Combine(RepoRoot, "src", "Sitecore.API.Foundation.Authorization", "Sitecore.API.Foundation.Authorization.csproj");
    private static readonly string DirectoryBuildPropsPath = Path.Combine(RepoRoot, "Directory.Build.props");
    private static readonly string GlobalJsonPath = Path.Combine(RepoRoot, "global.json");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Sitecore.API.Foundation.Authorization.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate repository root containing Sitecore.API.Foundation.Authorization.sln.");
    }

    [Fact]
    public void WorkflowFiles_ShouldExist_AndNotContainTabIndentation()
    {
        File.Exists(NugetReleaseWorkflowPath).ShouldBeTrue("nuget-release.yml must exist");
        File.Exists(PrCheckWorkflowPath).ShouldBeTrue("pr-check.yml must exist");

        foreach (var workflowFile in Directory.GetFiles(Path.Combine(RepoRoot, ".github", "workflows"), "*.yml"))
        {
            var lines = File.ReadAllLines(workflowFile);
            lines.ShouldNotBeEmpty();

            for (var i = 0; i < lines.Length; i++)
            {
                lines[i].StartsWith('\t').ShouldBeFalse(
                    $"{Path.GetFileName(workflowFile)} line {i + 1} uses tab indentation, which is invalid in YAML.");
            }
        }
    }

    [Fact]
    public void Workflows_ShouldNotCombineYamlFoldedScalarWithBackslashContinuations()
    {
        foreach (var workflowFile in Directory.GetFiles(Path.Combine(RepoRoot, ".github", "workflows"), "*.yml"))
        {
            var content = File.ReadAllText(workflowFile);
            // Using "run: >" (folded block scalar) with "\" line continuations is fragile in YAML
            Regex.IsMatch(content, @"run:\s*>\s*\r?\n(?:.*\r?\n)*?.*\\\s*\r?\n").ShouldBeFalse(
                $"{Path.GetFileName(workflowFile)} should use 'run: |' instead of 'run: >' for multi-line commands with backslash continuations.");
        }
    }

    [Fact]
    public void Workflows_ShouldInstallAllTargetFrameworkSdks_AndMatchGlobalJson()
    {
        var csprojDoc = XDocument.Load(MainCsprojPath);
        var targetFrameworksRaw = csprojDoc.Descendants("TargetFrameworks").First().Value;
        var targetFrameworks = targetFrameworksRaw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        targetFrameworks.ShouldNotBeEmpty();

        var nugetWorkflow = File.ReadAllText(NugetReleaseWorkflowPath);
        var prCheckWorkflow = File.ReadAllText(PrCheckWorkflowPath);

        foreach (var tfm in targetFrameworks)
        {
            // e.g. "net8.0" -> "8.0.x"
            tfm.StartsWith("net", StringComparison.Ordinal).ShouldBeTrue($"Unexpected TFM format: {tfm}");
            var versionPrefix = tfm["net".Length..];
            var sdkChannel = $"{versionPrefix}.x";

            nugetWorkflow.ShouldContain(sdkChannel, Case.Sensitive,
                $"nuget-release.yml must install .NET SDK '{sdkChannel}' for target framework '{tfm}'.");
            nugetWorkflow.ShouldContain($"-p:TargetFramework={tfm}", Case.Sensitive,
                $"nuget-release.yml must explicitly build target framework '{tfm}'.");

            prCheckWorkflow.ShouldContain(sdkChannel, Case.Sensitive,
                $"pr-check.yml must install .NET SDK '{sdkChannel}' for target framework '{tfm}'.");
        }

        // Verify global.json major version is also installed in the release job of nuget-release.yml
        using var globalJson = JsonDocument.Parse(File.ReadAllText(GlobalJsonPath));
        var sdkVersion = globalJson.RootElement.GetProperty("sdk").GetProperty("version").GetString()!;
        var majorMinor = string.Join('.', sdkVersion.Split('.').Take(2));
        nugetWorkflow.ShouldContain($"dotnet-version: '{majorMinor}.x'", Case.Sensitive,
            $"nuget-release.yml release job must install '{majorMinor}.x' to satisfy global.json ({sdkVersion}).");
    }

    [Fact]
    public void NugetReleaseWorkflow_ShouldUseOidcTrustedPublishing_AndLeastPrivilegePermissions()
    {
        var nugetWorkflow = File.ReadAllText(NugetReleaseWorkflowPath);

        // Root permissions should be read-only
        Regex.IsMatch(nugetWorkflow, @"^permissions:\s*\r?\n\s+contents:\s*read", RegexOptions.Multiline)
            .ShouldBeTrue("Root workflow permissions in nuget-release.yml should be 'contents: read'.");

        // Release job should require v* tag, use 'release' environment, and request id-token: write
        nugetWorkflow.ShouldContain("if: startsWith(github.ref, 'refs/tags/v')");
        nugetWorkflow.ShouldContain("environment: release");
        nugetWorkflow.ShouldContain("id-token: write");
        nugetWorkflow.ShouldContain("uses: NuGet/login@v1");
        nugetWorkflow.ShouldContain("user: ${{ secrets.NUGET_USER }}");

        // Must not use legacy long-lived NUGET_API_KEY secret from repository secrets
        nugetWorkflow.ShouldNotContain("secrets.NUGET_API_KEY");

        // Must pass temporary OIDC key via environment variable and use --skip-duplicate
        nugetWorkflow.ShouldContain("NUGET_API_KEY: ${{ steps.nuget-login.outputs.NUGET_API_KEY }}");
        nugetWorkflow.ShouldContain("--api-key \"$NUGET_API_KEY\"");
        nugetWorkflow.ShouldContain("--skip-duplicate");
    }

    [Fact]
    public void NugetReleaseWorkflow_ShouldUploadSymbolPackages_WhenIncludeSymbolsIsEnabled()
    {
        var propsDoc = XDocument.Load(DirectoryBuildPropsPath);
        var includeSymbols = propsDoc.Descendants("IncludeSymbols").FirstOrDefault()?.Value;
        var symbolFormat = propsDoc.Descendants("SymbolPackageFormat").FirstOrDefault()?.Value;

        if (string.Equals(includeSymbols, "true", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(symbolFormat, "snupkg", StringComparison.OrdinalIgnoreCase))
        {
            var nugetWorkflow = File.ReadAllText(NugetReleaseWorkflowPath);
            nugetWorkflow.ShouldContain("./nupkgs/SitecoreAPIAuthorization*.nupkg");
            nugetWorkflow.ShouldContain("./nupkgs/SitecoreAPIAuthorization*.snupkg",
                Case.Sensitive,
                "Directory.Build.props enables snupkg symbol packages, so nuget-release.yml must upload *.snupkg artifacts.");
        }
    }

    [Fact]
    public void NugetReleaseWorkflow_ShouldAlignPackageVersionWithGitTag()
    {
        var nugetWorkflow = File.ReadAllText(NugetReleaseWorkflowPath);

        nugetWorkflow.ShouldContain("PACKAGE_VERSION=\"${GITHUB_REF_NAME#v}\"");
        nugetWorkflow.ShouldContain("-p:PackageVersion=\"${PACKAGE_VERSION}\"");
    }

    [Fact]
    public void Workflows_ReferencedRepositoryFiles_ShouldExist()
    {
        var nugetWorkflow = File.ReadAllText(NugetReleaseWorkflowPath);

        // Verify referenced project and changelog files exist in the repo
        nugetWorkflow.ShouldContain("src/Sitecore.API.Foundation.Authorization/Sitecore.API.Foundation.Authorization.csproj");
        File.Exists(Path.Combine(RepoRoot, "src/Sitecore.API.Foundation.Authorization/Sitecore.API.Foundation.Authorization.csproj"))
            .ShouldBeTrue();

        nugetWorkflow.ShouldContain("bodyFile: CHANGELOG.md");
        File.Exists(Path.Combine(RepoRoot, "CHANGELOG.md"))
            .ShouldBeTrue("CHANGELOG.md referenced by release action must exist in repository root.");

        // Ensure broken/missing release-drafter config is not referenced unless the file exists in .github/
        if (nugetWorkflow.Contains("config-name: release-drafter.yml", StringComparison.Ordinal))
        {
            File.Exists(Path.Combine(RepoRoot, ".github", "release-drafter.yml"))
                .ShouldBeTrue(".github/release-drafter.yml is referenced in nuget-release.yml but does not exist.");
        }
    }

    [Fact]
    public void Workflows_TestFilter_ShouldNotExcludeUnitTestSuite()
    {
        var nugetWorkflow = File.ReadAllText(NugetReleaseWorkflowPath);
        var prCheckWorkflow = File.ReadAllText(PrCheckWorkflowPath);

        // The unit test assembly uses namespace "Sitecore.API.Foundation.Tests" (not "UnitTests").
        // Filtering only for "~UnitTests" silently skips all unit tests in Sitecore.API.Foundation.Tests.dll.
        nugetWorkflow.ShouldContain("--filter \"FullyQualifiedName!~TokenServiceIntegrationTests\"");
        prCheckWorkflow.ShouldContain("--filter \"FullyQualifiedName!~TokenServiceIntegrationTests\"");
    }
}
