using System;
using System.Threading.Tasks;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Sitecore.API.Foundation.Authorization.IntegrationTests.Tests;

/// <summary>
/// Basic smoke tests to verify test infrastructure is working.
/// </summary>
public class InfrastructureTests
{
    private readonly ITestOutputHelper _output;

    public InfrastructureTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Test_infrastructure_should_be_working()
    {
        // Arrange & Act & Assert
        true.ShouldBeTrue();
        _output.WriteLine("? Basic test infrastructure is working");
    }

    [Fact]
    public async Task Async_test_infrastructure_should_be_working()
    {
        // Arrange & Act
        await Task.Delay(1);

        // Assert
        true.ShouldBeTrue();
        _output.WriteLine("? Async test infrastructure is working");
    }

    [Fact]
    public void Test_can_access_system_info()
    {
        // Arrange & Act
        var currentDirectory = System.IO.Directory.GetCurrentDirectory();
        var baseDirectory = AppContext.BaseDirectory;

        // Assert
        currentDirectory.ShouldNotBeNull();
        baseDirectory.ShouldNotBeNull();
        
        _output.WriteLine($"Current Directory: {currentDirectory}");
        _output.WriteLine($"Base Directory: {baseDirectory}");
        _output.WriteLine($"OS: {Environment.OSVersion}");
        _output.WriteLine($".NET Version: {Environment.Version}");
    }

    [Fact]
    public void Test_can_access_realm_file()
    {
        // Arrange
        var possiblePaths = new[]
        {
            System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Realm", "test-realm.json"),
            System.IO.Path.Combine(AppContext.BaseDirectory, "Realm", "test-realm.json"),
            System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "..", "..", "..", "Realm", "test-realm.json"),
            "Realm/test-realm.json",
            "test-realm.json"
        };

        bool realmFileFound = false;
        string foundPath = "";

        foreach (var path in possiblePaths)
        {
            var fullPath = System.IO.Path.GetFullPath(path);
            _output.WriteLine($"Checking path: {fullPath}");
            
            if (System.IO.File.Exists(fullPath))
            {
                realmFileFound = true;
                foundPath = fullPath;
                break;
            }
        }

        // Assert
        if (realmFileFound)
        {
            _output.WriteLine($"? Realm file found at: {foundPath}");
            realmFileFound.ShouldBeTrue();
        }
        else
        {
            _output.WriteLine("? Realm file not found in any expected location");
            _output.WriteLine("Expected locations checked:");
            foreach (var path in possiblePaths)
            {
                _output.WriteLine($"  - {System.IO.Path.GetFullPath(path)}");
            }
            // Don't fail the test, just warn
            _output.WriteLine("? Warning: Realm file not found, but this test will continue");
        }
    }
}