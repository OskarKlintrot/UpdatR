using Microsoft.Extensions.Logging;
using NuGet.Frameworks;
using NuGet.Versioning;
using UpdatR.Domain;
using UpdatR.Internals;

namespace UpdatR.UnitTests.Domain;

public class PropsFileTests : IDisposable
{
    private readonly string _propsPath = Path.Combine(
        Path.GetTempPath(),
        $"{Guid.NewGuid()}.props"
    );

    public void Dispose()
    {
        if (File.Exists(_propsPath))
        {
            File.Delete(_propsPath);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void UpdatePackagesUpdatesPackageReference()
    {
        // Arrange
        File.WriteAllText(
            _propsPath,
            """
            <Project>
              <ItemGroup>
                <PackageReference Include="Some.Package" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """
        );

        var propsFile = PropsFile.Create(_propsPath, [NuGetFramework.Parse("net10.0")]);
        var packages = CreatePackages("Some.Package", "1.0.0", "2.0.0");

        // Act
        var result = propsFile.UpdatePackages(
            packages,
            dryRun: false,
            usePrerelease: false,
            logger: new FakeLogger()
        );

        // Assert
        var updated = Assert.Single(result!.UpdatedPackages);

        Assert.Equal("Some.Package", updated.PackageId);
        Assert.Equal(NuGetVersion.Parse("1.0.0"), updated.From);
        Assert.Equal(NuGetVersion.Parse("2.0.0"), updated.To);

        Assert.Contains("""Version="2.0.0" """.Trim(), File.ReadAllText(_propsPath));
    }

    [Fact]
    public void UpdatePackagesUpdatesPackageReferenceUsingUpdateAttribute()
    {
        // Arrange - PackageReference using Update (instead of Include) only overrides the
        // version of a package already referenced elsewhere, e.g. via Directory.Build.props.
        File.WriteAllText(
            _propsPath,
            """
            <Project>
              <ItemGroup>
                <PackageReference Update="Some.Package" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """
        );

        var propsFile = PropsFile.Create(_propsPath, [NuGetFramework.Parse("net10.0")]);
        var packages = CreatePackages("Some.Package", "1.0.0", "2.0.0");
        var logger = new FakeLogger();

        // Act
        var result = propsFile.UpdatePackages(
            packages,
            dryRun: false,
            usePrerelease: false,
            logger: logger
        );

        // Assert
        var updated = Assert.Single(result!.UpdatedPackages);

        Assert.Equal("Some.Package", updated.PackageId);
        Assert.Equal(NuGetVersion.Parse("1.0.0"), updated.From);
        Assert.Equal(NuGetVersion.Parse("2.0.0"), updated.To);

        Assert.Contains("""Version="2.0.0" """.Trim(), File.ReadAllText(_propsPath));
        Assert.DoesNotContain(logger.Logs, x => x.Level == LogLevel.Warning);
    }

    [Fact]
    public void UpdatePackagesUpdatesPackageVersionForCentralPackageManagement()
    {
        // Arrange
        File.WriteAllText(
            _propsPath,
            """
            <Project>
              <ItemGroup>
                <PackageVersion Include="Some.Package" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """
        );

        var propsFile = PropsFile.Create(_propsPath, [NuGetFramework.Parse("net10.0")]);
        var packages = CreatePackages("Some.Package", "1.0.0", "2.0.0");

        // Act
        var result = propsFile.UpdatePackages(
            packages,
            dryRun: false,
            usePrerelease: false,
            logger: new FakeLogger()
        );

        // Assert
        var updated = Assert.Single(result!.UpdatedPackages);

        Assert.Equal("Some.Package", updated.PackageId);
        Assert.Equal(NuGetVersion.Parse("2.0.0"), updated.To);

        var content = File.ReadAllText(_propsPath);

        Assert.Contains("PackageVersion", content);
        Assert.Contains("2.0.0", content);
    }

    [Fact]
    public void UpdatePackagesUpdatesGlobalPackageReference()
    {
        // Arrange - GlobalPackageReference is typically used for analyzers/source generators
        // that should apply to every project, and unlike PackageVersion it carries its own
        // Version directly.
        File.WriteAllText(
            _propsPath,
            """
            <Project>
              <ItemGroup>
                <GlobalPackageReference Include="Some.Analyzer" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """
        );

        var propsFile = PropsFile.Create(_propsPath, [NuGetFramework.Parse("net10.0")]);
        var packages = CreatePackages("Some.Analyzer", "1.0.0", "2.0.0");

        // Act
        var result = propsFile.UpdatePackages(
            packages,
            dryRun: false,
            usePrerelease: false,
            logger: new FakeLogger()
        );

        // Assert
        var updated = Assert.Single(result!.UpdatedPackages);

        Assert.Equal("Some.Analyzer", updated.PackageId);
        Assert.Equal(NuGetVersion.Parse("1.0.0"), updated.From);
        Assert.Equal(NuGetVersion.Parse("2.0.0"), updated.To);

        var content = File.ReadAllText(_propsPath);

        Assert.Contains("GlobalPackageReference", content);
        Assert.Contains("""Version="2.0.0" """.Trim(), content);
    }

    [Fact]
    public void UpdatePackagesDoesNotSaveFileWhenDryRun()
    {
        // Arrange
        var original = """
            <Project>
              <ItemGroup>
                <PackageReference Include="Some.Package" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """;

        File.WriteAllText(_propsPath, original);

        var propsFile = PropsFile.Create(_propsPath, [NuGetFramework.Parse("net10.0")]);
        var packages = CreatePackages("Some.Package", "1.0.0", "2.0.0");

        // Act
        var result = propsFile.UpdatePackages(
            packages,
            dryRun: true,
            usePrerelease: false,
            logger: new FakeLogger()
        );

        // Assert
        Assert.NotNull(result);
        Assert.Contains("1.0.0", File.ReadAllText(_propsPath));
        Assert.DoesNotContain("2.0.0", File.ReadAllText(_propsPath));
    }

    [Fact]
    public void UpdatePackagesSkipsPackageReferenceWithoutVersionAttributeWithoutWarning()
    {
        // Arrange
        File.WriteAllText(
            _propsPath,
            """
            <Project>
              <ItemGroup>
                <PackageReference Update="Some.Package" GeneratePathProperty="true">
                  <PrivateAssets>none</PrivateAssets>
                </PackageReference>
              </ItemGroup>
            </Project>
            """
        );

        var propsFile = PropsFile.Create(_propsPath, [NuGetFramework.Parse("net10.0")]);
        var logger = new FakeLogger();

        // Act
        var result = propsFile.UpdatePackages(
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
            _propsPath,
            """
            <Project>
              <ItemGroup>
                <PackageReference Include="Some.Package" Version="not-a-version" />
              </ItemGroup>
            </Project>
            """
        );

        var propsFile = PropsFile.Create(_propsPath, [NuGetFramework.Parse("net10.0")]);
        var logger = new FakeLogger();

        // Act
        var result = propsFile.UpdatePackages(
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
    public void UpdatePackagesSkipsFloatingVersionWithoutWarningWhenAlreadyLatest()
    {
        // Arrange - a floating version like "4.8.*" isn't a NuGetVersion, but NuGet already
        // resolves it to the latest matching version on restore. If nothing newer than that is
        // available, there's nothing to update.
        File.WriteAllText(
            _propsPath,
            """
            <Project>
              <ItemGroup>
                <PackageReference Include="Some.Package" Version="4.8.*" />
              </ItemGroup>
            </Project>
            """
        );

        var propsFile = PropsFile.Create(_propsPath, [NuGetFramework.Parse("net10.0")]);
        var logger = new FakeLogger();

        var package = new NuGetPackage(
            "Some.Package",
            [new PackageMetadata(NuGetVersion.Parse("4.8.5"), [], null, null)]
        );

        // Act
        var result = propsFile.UpdatePackages(
            new Dictionary<string, NuGetPackage?> { ["Some.Package"] = package },
            dryRun: true,
            usePrerelease: false,
            logger: logger
        );

        // Assert
        Assert.Null(result);
        Assert.DoesNotContain(logger.Logs, x => x.Level == LogLevel.Warning);

        var (level, message) = Assert.Single(logger.Logs);

        Assert.Equal(LogLevel.Debug, level);
        Assert.Equal(
            """Skipping automatic update of floating version 4.8.* for package reference <PackageReference Include="Some.Package" Version="4.8.*" /> since NuGet already resolves it to the latest matching version.""",
            message
        );
        Assert.Contains("""Version="4.8.*" """.Trim(), File.ReadAllText(_propsPath));
    }

    [Fact]
    public void UpdatePackagesReportsDeprecationForFloatingVersionWithoutBumpingIt()
    {
        // Arrange - 1.5.0 is both deprecated and the highest version matching "1.*" (no newer
        // version at all exists), so nothing should be rewritten, but the deprecation should
        // still be reported.
        File.WriteAllText(
            _propsPath,
            """
            <Project>
              <ItemGroup>
                <PackageReference Include="Some.Package" Version="1.*" />
              </ItemGroup>
            </Project>
            """
        );

        var propsFile = PropsFile.Create(_propsPath, [NuGetFramework.Parse("net10.0")]);
        var logger = new FakeLogger();

        var package = new NuGetPackage(
            "Some.Package",
            [
                new PackageMetadata(NuGetVersion.Parse("1.0.0"), [], null, null),
                new PackageMetadata(
                    NuGetVersion.Parse("1.5.0"),
                    [],
                    new PackageDeprecationMetadata("deprecated", ["Legacy"], null),
                    null
                ),
            ]
        );

        // Act
        var result = propsFile.UpdatePackages(
            new Dictionary<string, NuGetPackage?> { ["Some.Package"] = package },
            dryRun: true,
            usePrerelease: false,
            logger: logger
        );

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.UpdatedPackages);

        var deprecated = Assert.Single(result.DeprecatedPackages);

        Assert.Equal("Some.Package", deprecated.PackageId);
        Assert.Equal(NuGetVersion.Parse("1.5.0"), deprecated.Version);
        Assert.Contains("""Version="1.*" """.Trim(), File.ReadAllText(_propsPath));
    }

    [Fact]
    public void UpdatePackagesBumpsFloatingVersionToNewerSeries()
    {
        // Arrange - "4.8.*" only floats within the 4.8.x series. If the latest available version
        // is 4.9.2, UpdatR should bump the fixed prefix to "4.9.*" so the project can float to
        // the newer series too, since NuGet won't do that on its own.
        File.WriteAllText(
            _propsPath,
            """
            <Project>
              <ItemGroup>
                <PackageReference Include="Some.Package" Version="4.8.*" />
              </ItemGroup>
            </Project>
            """
        );

        var propsFile = PropsFile.Create(_propsPath, [NuGetFramework.Parse("net10.0")]);
        var logger = new FakeLogger();

        var package = new NuGetPackage(
            "Some.Package",
            [
                new PackageMetadata(NuGetVersion.Parse("4.8.5"), [], null, null),
                new PackageMetadata(NuGetVersion.Parse("4.9.2"), [], null, null),
            ]
        );

        // Act
        var result = propsFile.UpdatePackages(
            new Dictionary<string, NuGetPackage?> { ["Some.Package"] = package },
            dryRun: false,
            usePrerelease: false,
            logger: logger
        );

        // Assert
        Assert.NotNull(result);

        var updated = Assert.Single(result.UpdatedPackages);

        Assert.Equal("Some.Package", updated.PackageId);
        Assert.Equal(NuGetVersion.Parse("4.8.5"), updated.From);
        Assert.Equal(NuGetVersion.Parse("4.9.2"), updated.To);

        Assert.Contains("""Version="4.9.*" """.Trim(), File.ReadAllText(_propsPath));
        Assert.DoesNotContain(logger.Logs, x => x.Level == LogLevel.Warning);
    }

    [Fact]
    public void UpdatePackagesTracksUnknownPackage()
    {
        // Arrange
        File.WriteAllText(
            _propsPath,
            """
            <Project>
              <ItemGroup>
                <PackageReference Include="Unknown.Package" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """
        );

        var propsFile = PropsFile.Create(_propsPath, [NuGetFramework.Parse("net10.0")]);
        var logger = new FakeLogger();

        // Act
        var result = propsFile.UpdatePackages(
            new Dictionary<string, NuGetPackage?>(),
            dryRun: true,
            usePrerelease: false,
            logger: logger
        );

        // Assert - an unknown package alone (no actual updates/deprecations/vulnerabilities)
        // doesn't make UpdatePackages return a report, matching Csproj's behavior; the missing
        // package is still logged.
        Assert.Null(result);
        Assert.Contains(logger.Logs, x => x.Message == "Could not find Unknown.Package.");
    }

    [Fact]
    public void UpdatePackagesOnlyUpdatesToVersionCompatibleWithAllContributingTargetFrameworks()
    {
        // Arrange - net6.0 can only go to 1.5.0 (2.0.0 only supports net8.0), so the
        // conservative/common update across both frameworks is 1.5.0, not 2.0.0.
        File.WriteAllText(
            _propsPath,
            """
            <Project>
              <ItemGroup>
                <PackageReference Include="Some.Package" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """
        );

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

        var propsFile = PropsFile.Create(
            _propsPath,
            [NuGetFramework.Parse("net6.0"), NuGetFramework.Parse("net8.0")]
        );

        // Act
        var result = propsFile.UpdatePackages(
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
    public void UpdatePackagesSkipsUpdateWhenAnyContributingTargetFrameworkHasNoNewerVersion()
    {
        // Arrange - version 2.0.0 only targets net8.0, so the net472 project (a different
        // framework family entirely) can't use it and stays on 1.0.0. Since this file is
        // imported by both, nothing should be updated - even though the net8.0 project could
        // move to 2.0.0 - as that could break the net472 project that also imports this file.
        File.WriteAllText(
            _propsPath,
            """
            <Project>
              <ItemGroup>
                <PackageReference Include="Some.Package" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """
        );

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

        var propsFile = PropsFile.Create(
            _propsPath,
            [NuGetFramework.Parse("net472"), NuGetFramework.Parse("net8.0")]
        );

        // Act
        var result = propsFile.UpdatePackages(
            new Dictionary<string, NuGetPackage?> { ["Some.Package"] = package },
            dryRun: true,
            usePrerelease: false,
            logger: new FakeLogger()
        );

        // Assert
        Assert.Null(result);
        Assert.Equal(
            "1.0.0",
            NuGetVersion.Parse(propsFile.Packages["Some.Package"].ToString()).ToString()
        );
        Assert.Contains("1.0.0", File.ReadAllText(_propsPath));
    }

    [Fact]
    public void UpdatePackagesLogsLicenseMismatchForInstalledVersion()
    {
        // Arrange
        File.WriteAllText(
            _propsPath,
            """
            <Project>
              <ItemGroup>
                <PackageReference Include="Some.Package" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """
        );

        var package = new NuGetPackage(
            "Some.Package",
            [
                new PackageMetadata(
                    NuGetVersion.Parse("1.0.0"),
                    [NuGetFramework.Parse("net10.0")],
                    null,
                    null,
                    "GPL-3.0"
                ),
            ]
        );

        var propsFile = PropsFile.Create(_propsPath, [NuGetFramework.Parse("net10.0")]);
        var logger = new FakeLogger();

        // Act
        var result = propsFile.UpdatePackages(
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
    }

    [Fact]
    public void UpdatePackagesLogsLicenseMismatchForSkippedUpdateSharedAcrossTfms()
    {
        // Arrange - shared by both net6.0 and net8.0. Version 2.0.0 is compatible with both
        // frameworks, but its license isn't allowed, so the update is skipped and reported.
        File.WriteAllText(
            _propsPath,
            """
            <Project>
              <ItemGroup>
                <PackageReference Include="Some.Package" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """
        );

        var package = new NuGetPackage(
            "Some.Package",
            [
                new PackageMetadata(
                    NuGetVersion.Parse("1.0.0"),
                    [NuGetFramework.Parse("net6.0"), NuGetFramework.Parse("net8.0")],
                    null,
                    null,
                    "MIT"
                ),
                new PackageMetadata(
                    NuGetVersion.Parse("2.0.0"),
                    [NuGetFramework.Parse("net6.0"), NuGetFramework.Parse("net8.0")],
                    null,
                    null,
                    "GPL-3.0"
                ),
            ]
        );

        var propsFile = PropsFile.Create(
            _propsPath,
            [NuGetFramework.Parse("net6.0"), NuGetFramework.Parse("net8.0")]
        );
        var logger = new FakeLogger();

        // Act
        var result = propsFile.UpdatePackages(
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
    }

    [Fact]
    public void PackagesReturnsBothPackageReferenceAndPackageVersionItems()
    {
        // Arrange
        File.WriteAllText(
            _propsPath,
            """
            <Project>
              <ItemGroup>
                <PackageReference Include="Package.One" Version="1.0.0" />
                <PackageVersion Include="Package.Two" Version="2.0.0" />
              </ItemGroup>
            </Project>
            """
        );

        var propsFile = PropsFile.Create(_propsPath);

        // Act
        var packages = propsFile.Packages;

        // Assert
        Assert.Equal(NuGetVersion.Parse("1.0.0"), packages["Package.One"]);
        Assert.Equal(NuGetVersion.Parse("2.0.0"), packages["Package.Two"]);
    }

    [Fact]
    public void PackagesReturnsPackageReferenceUsingUpdateAttribute()
    {
        // Arrange - PackageReference using Update (instead of Include) only overrides the
        // version of a package already referenced elsewhere, e.g. via Directory.Build.props.
        File.WriteAllText(
            _propsPath,
            """
            <Project>
              <ItemGroup>
                <PackageReference Update="Package.One" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """
        );

        var propsFile = PropsFile.Create(_propsPath);

        // Act
        var packages = propsFile.Packages;

        // Assert
        Assert.Equal(NuGetVersion.Parse("1.0.0"), packages["Package.One"]);
    }

    private static Dictionary<string, NuGetPackage?> CreatePackages(
        string packageId,
        string from,
        string to
    ) =>
        new()
        {
            [packageId] = new NuGetPackage(
                packageId,
                [
                    new PackageMetadata(NuGetVersion.Parse(from), [], null, null),
                    new PackageMetadata(NuGetVersion.Parse(to), [], null, null),
                ]
            ),
        };

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
