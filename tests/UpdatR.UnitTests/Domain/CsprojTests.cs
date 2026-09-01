using Microsoft.Extensions.Logging;
using NuGet.Frameworks;
using NuGet.Versioning;
using UpdatR.Domain;
using UpdatR.Internals;

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

    [Fact]
    public void UpdatePackagesUpdatesPackageReferenceUsingUpdateAttribute()
    {
        // Arrange - PackageReference using Update (instead of Include) only overrides the
        // version of a package already referenced elsewhere, e.g. via Directory.Build.props.
        File.WriteAllText(
            _csprojPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Update="Some.Package" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """
        );

        var csproj = Csproj.Create(_csprojPath);
        var logger = new FakeLogger();

        var package = new NuGetPackage(
            "Some.Package",
            [
                new UpdatR.Internals.PackageMetadata(
                    NuGetVersion.Parse("1.0.0"),
                    [NuGetFramework.Parse("net10.0")],
                    null,
                    null
                ),
                new UpdatR.Internals.PackageMetadata(
                    NuGetVersion.Parse("2.0.0"),
                    [NuGetFramework.Parse("net10.0")],
                    null,
                    null
                ),
            ]
        );

        // Act
        var result = csproj.UpdatePackages(
            new Dictionary<string, NuGetPackage?> { ["Some.Package"] = package },
            dryRun: false,
            usePrerelease: false,
            logger: logger
        );

        // Assert
        Assert.NotNull(result);

        var updated = Assert.Single(result.UpdatedPackages);

        Assert.Equal("Some.Package", updated.PackageId);
        Assert.Equal(NuGetVersion.Parse("1.0.0"), updated.From);
        Assert.Equal(NuGetVersion.Parse("2.0.0"), updated.To);

        Assert.Contains("""Version="2.0.0" """.Trim(), File.ReadAllText(_csprojPath));
        Assert.DoesNotContain(logger.Logs, x => x.Level == LogLevel.Warning);
    }

    [Fact]
    public void UpdatePackagesLogsLicenseMismatchForInstalledVersion()
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
                <PackageReference Include="Some.Package" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """
        );

        var csproj = Csproj.Create(_csprojPath);
        var logger = new FakeLogger();

        var package = new NuGetPackage(
            "Some.Package",
            [
                new UpdatR.Internals.PackageMetadata(
                    NuGetVersion.Parse("1.0.0"),
                    [NuGetFramework.Parse("net10.0")],
                    null,
                    null,
                    "GPL-3.0"
                ),
            ]
        );

        // Act
        var result = csproj.UpdatePackages(
            new Dictionary<string, NuGetPackage?> { ["Some.Package"] = package },
            dryRun: true,
            usePrerelease: false,
            logger: logger,
            allowedLicenses: ["MIT"]
        );

        // Assert
        Assert.NotNull(result);

        var licenseMismatch = Assert.Single(result.LicenseMismatchPackages);

        Assert.Equal("Some.Package", licenseMismatch.PackageId);
        Assert.Equal(NuGetVersion.Parse("1.0.0"), licenseMismatch.Version);
        Assert.Equal("GPL-3.0", licenseMismatch.License);
        Assert.True(licenseMismatch.IsInstalledVersion);

        var (level, message) = Assert.Single(logger.Logs);

        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains("Some.Package", message, StringComparison.Ordinal);
        Assert.Contains("GPL-3.0", message, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdatePackagesLogsLicenseMismatchForSkippedUpdate()
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
                <PackageReference Include="Some.Package" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """
        );

        var csproj = Csproj.Create(_csprojPath);
        var logger = new FakeLogger();

        var package = new NuGetPackage(
            "Some.Package",
            [
                new UpdatR.Internals.PackageMetadata(
                    NuGetVersion.Parse("1.0.0"),
                    [NuGetFramework.Parse("net10.0")],
                    null,
                    null,
                    "MIT"
                ),
                new UpdatR.Internals.PackageMetadata(
                    NuGetVersion.Parse("2.0.0"),
                    [NuGetFramework.Parse("net10.0")],
                    null,
                    null,
                    "GPL-3.0"
                ),
            ]
        );

        // Act
        var result = csproj.UpdatePackages(
            new Dictionary<string, NuGetPackage?> { ["Some.Package"] = package },
            dryRun: true,
            usePrerelease: false,
            logger: logger,
            allowedLicenses: ["MIT"]
        );

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.UpdatedPackages);

        var licenseMismatch = Assert.Single(result.LicenseMismatchPackages);

        Assert.Equal("Some.Package", licenseMismatch.PackageId);
        Assert.Equal(NuGetVersion.Parse("2.0.0"), licenseMismatch.Version);
        Assert.Equal("GPL-3.0", licenseMismatch.License);
        Assert.False(licenseMismatch.IsInstalledVersion);

        var (level, message) = Assert.Single(logger.Logs);

        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains("Some.Package", message, StringComparison.Ordinal);
        Assert.Contains("2.0.0", message, StringComparison.Ordinal);
        Assert.Contains("GPL-3.0", message, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdatePackagesOnlyUpdatesToVersionCompatibleWithAllTargetFrameworks()
    {
        // Arrange - a multi-targeted (net6.0;net8.0) project can only go to 1.5.0 (2.0.0 only
        // supports net8.0), so the conservative/common update across both frameworks is 1.5.0,
        // not 2.0.0.
        File.WriteAllText(
            _csprojPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net6.0;net8.0</TargetFrameworks>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Some.Package" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """
        );

        var csproj = Csproj.Create(_csprojPath);

        var package = new NuGetPackage(
            "Some.Package",
            [
                new PackageMetadata(
                    NuGetVersion.Parse("1.0.0"),
                    [NuGetFramework.Parse("net6.0"), NuGetFramework.Parse("net8.0")],
                    null,
                    null
                ),
                new PackageMetadata(
                    NuGetVersion.Parse("1.5.0"),
                    [NuGetFramework.Parse("net6.0"), NuGetFramework.Parse("net8.0")],
                    null,
                    null
                ),
                new PackageMetadata(
                    NuGetVersion.Parse("2.0.0"),
                    [NuGetFramework.Parse("net8.0")],
                    null,
                    null
                ),
            ]
        );

        // Act
        var result = csproj.UpdatePackages(
            new Dictionary<string, NuGetPackage?> { ["Some.Package"] = package },
            dryRun: true,
            usePrerelease: false,
            logger: new FakeLogger()
        );

        // Assert
        var updated = Assert.Single(result!.UpdatedPackages);

        Assert.Equal(NuGetVersion.Parse("1.5.0"), updated.To);
    }

    [Fact]
    public void UpdatePackagesSkipsUpdateWhenAnyTargetFrameworkHasNoNewerVersion()
    {
        // Arrange - version 2.0.0 only targets net8.0, so the net472 part of this multi-targeted
        // project can't use it. Nothing should be updated even though the net8.0 part could move
        // to 2.0.0, since that could break the build for net472.
        File.WriteAllText(
            _csprojPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net472;net8.0</TargetFrameworks>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Some.Package" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """
        );

        var csproj = Csproj.Create(_csprojPath);

        var package = new NuGetPackage(
            "Some.Package",
            [
                new PackageMetadata(
                    NuGetVersion.Parse("1.0.0"),
                    [NuGetFramework.Parse("net472"), NuGetFramework.Parse("net8.0")],
                    null,
                    null
                ),
                new PackageMetadata(
                    NuGetVersion.Parse("2.0.0"),
                    [NuGetFramework.Parse("net8.0")],
                    null,
                    null
                ),
            ]
        );

        // Act
        var result = csproj.UpdatePackages(
            new Dictionary<string, NuGetPackage?> { ["Some.Package"] = package },
            dryRun: true,
            usePrerelease: false,
            logger: new FakeLogger()
        );

        // Assert
        Assert.Null(result);
        Assert.Contains("""Version="1.0.0" """.Trim(), File.ReadAllText(_csprojPath));
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
