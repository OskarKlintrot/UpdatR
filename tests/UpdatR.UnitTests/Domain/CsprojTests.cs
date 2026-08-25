using Microsoft.Extensions.Logging;
using UpdatR.Domain;

namespace UpdatR.UnitTests.Domain;

public class CsprojTests : IDisposable
{
    private readonly string _csprojPath = Path.Combine(
        Path.GetTempPath(),
        $"{Guid.NewGuid()}.csproj"
    );

    public void Dispose()
    {
        if (File.Exists(_csprojPath))
        {
            File.Delete(_csprojPath);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void UpdatePackagesSkipsPackageReferenceWithoutVersionAttributeWithoutWarning()
    {
        // Arrange
        File.WriteAllText(
            _csprojPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Update="Some.Package" GeneratePathProperty="true">
                  <PrivateAssets>none</PrivateAssets>
                </PackageReference>
              </ItemGroup>
            </Project>
            """
        );

        var csproj = Csproj.Create(_csprojPath);
        var logger = new FakeLogger();

        // Act
        var result = csproj.UpdatePackages(
            new Dictionary<string, NuGetPackage?>(),
            dryRun: true,
            usePrerelease: false,
            logger: logger
        );

        // Assert
        Assert.Null(result);
        Assert.Empty(logger.Logs);
    }

    [Fact]
    public void UpdatePackagesLogsPackageReferenceXmlForInvalidVersion()
    {
        // Arrange
        File.WriteAllText(
            _csprojPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Some.Package" Version="not-a-version" />
              </ItemGroup>
            </Project>
            """
        );

        var csproj = Csproj.Create(_csprojPath);
        var logger = new FakeLogger();

        // Act
        var result = csproj.UpdatePackages(
            new Dictionary<string, NuGetPackage?>(),
            dryRun: true,
            usePrerelease: false,
            logger: logger
        );

        // Assert
        Assert.Null(result);

        var (level, message) = Assert.Single(logger.Logs);

        Assert.Equal(LogLevel.Warning, level);
        Assert.Equal(
            """Could not parse not-a-version to NuGetVersion for package reference <PackageReference Include="Some.Package" Version="not-a-version" />.""",
            message
        );
    }

    private sealed class FakeLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Logs { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            Logs.Add((logLevel, formatter(state, exception)));
        }
    }
}
