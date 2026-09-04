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
    public async Task UpdatePackagesSkipsPackageReferenceWithoutVersionAttributeWithoutWarning()
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
        var result = await csproj.UpdatePackagesAsync(
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
    public async Task UpdatePackagesLogsPackageReferenceXmlForInvalidVersion()
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
        var result = await csproj.UpdatePackagesAsync(
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
    public async Task UpdatePackagesUpdatesPackageReferenceUsingUpdateAttribute()
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
        var result = await csproj.UpdatePackagesAsync(
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
    public async Task UpdatePackagesUpdatesPackageReferenceUsingVersionOverrideAttribute()
    {
        // Arrange - with Central Package Management, a project overrides the centrally managed
        // version for a single package using VersionOverride instead of Version.
        File.WriteAllText(
            _csprojPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Some.Package" VersionOverride="1.0.0" />
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
        var result = await csproj.UpdatePackagesAsync(
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

        Assert.Contains("""VersionOverride="2.0.0" """.Trim(), File.ReadAllText(_csprojPath));
        Assert.DoesNotContain(logger.Logs, x => x.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task UpdatePackagesLogsLicenseMismatchForInstalledVersion()
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
        var result = await csproj.UpdatePackagesAsync(
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
    public async Task UpdatePackagesLogsLicenseMismatchForSkippedUpdate()
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
        var result = await csproj.UpdatePackagesAsync(
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
    public async Task UpdatePackagesOnlyUpdatesToVersionCompatibleWithAllTargetFrameworks()
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
        var result = await csproj.UpdatePackagesAsync(
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
    public async Task UpdatePackagesSkipsUpdateWhenAnyTargetFrameworkHasNoNewerVersion()
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
        var result = await csproj.UpdatePackagesAsync(
            new Dictionary<string, NuGetPackage?> { ["Some.Package"] = package },
            dryRun: true,
            usePrerelease: false,
            logger: new FakeLogger()
        );

        // Assert
        Assert.NotNull(result);
        var skipped = Assert.Single(result.SkippedUpdatePackages);
        Assert.Equal("Some.Package", skipped.PackageId);
        Assert.Equal(NuGetVersion.Parse("2.0.0"), skipped.Version);
        Assert.Equal(SkippedUpdateReason.IncompatibleTargetFramework, skipped.Reason);
        Assert.Contains("""Version="1.0.0" """.Trim(), File.ReadAllText(_csprojPath));
    }

    [Fact]
    public async Task UpdatePackagesUpdatesConditionedPackageReferencesToDifferentVersionsPerTfm()
    {
        // Arrange - a multi-targeted (net6.0;net8.0) project referencing the same package at
        // different versions for each framework, via a Condition on $(TargetFramework) on each
        // ItemGroup. Each occurrence should be evaluated - and can be updated - independently,
        // using only the target framework(s) it actually applies to (resolved via a real
        // per-framework MSBuild evaluation), not every target framework of the whole project.
        File.WriteAllText(
            _csprojPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net6.0;net8.0</TargetFrameworks>
              </PropertyGroup>
              <ItemGroup Condition="'$(TargetFramework)'=='net6.0'">
                <PackageReference Include="Some.Package" Version="1.0.0" />
              </ItemGroup>
              <ItemGroup Condition="'$(TargetFramework)'=='net8.0'">
                <PackageReference Include="Some.Package" Version="2.0.0" />
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
                new PackageMetadata(
                    NuGetVersion.Parse("2.5.0"),
                    [NuGetFramework.Parse("net8.0")],
                    null,
                    null
                ),
            ]
        );

        // Act
        var result = await csproj.UpdatePackagesAsync(
            new Dictionary<string, NuGetPackage?> { ["Some.Package"] = package },
            dryRun: false,
            usePrerelease: false,
            logger: new FakeLogger()
        );

        // Assert - the net6.0 branch can only reach 1.5.0 (2.0.0+ dropped net6.0 support), while
        // the net8.0 branch, evaluated independently, reaches the actual latest, 2.5.0.
        Assert.Equal(2, result!.UpdatedPackages.Count());

        Assert.Contains(
            result.UpdatedPackages,
            x =>
                x.PackageId == "Some.Package"
                && x.From == NuGetVersion.Parse("1.0.0")
                && x.To == NuGetVersion.Parse("1.5.0")
        );

        Assert.Contains(
            result.UpdatedPackages,
            x =>
                x.PackageId == "Some.Package"
                && x.From == NuGetVersion.Parse("2.0.0")
                && x.To == NuGetVersion.Parse("2.5.0")
        );

        var content = File.ReadAllText(_csprojPath);

        Assert.Contains("""Version="1.5.0" """.Trim(), content);
        Assert.Contains("""Version="2.5.0" """.Trim(), content);
    }

    [Fact]
    public async Task UpdatePackagesUpdatesConditionedPackageReferenceOnlyDeclaredForOneOfMultipleTfms()
    {
        // Arrange - a multi-targeted (net5.0;net6.0) project where a Condition on
        // $(TargetFramework) means Some.Package is only ever referenced for net5.0 - net6.0 never
        // gets it at all. Even though the package itself is only compatible with net5.0, the
        // update should still go through, evaluated against just the net5.0 it actually applies
        // to, rather than being blocked because net6.0 (which never uses it) doesn't support it.
        File.WriteAllText(
            _csprojPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net5.0;net6.0</TargetFrameworks>
              </PropertyGroup>
              <ItemGroup Condition="'$(TargetFramework)'=='net5.0'">
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
                    [NuGetFramework.Parse("net5.0")],
                    null,
                    null
                ),
                new PackageMetadata(
                    NuGetVersion.Parse("2.0.0"),
                    [NuGetFramework.Parse("net5.0")],
                    null,
                    null
                ),
            ]
        );

        // Act
        var result = await csproj.UpdatePackagesAsync(
            new Dictionary<string, NuGetPackage?> { ["Some.Package"] = package },
            dryRun: false,
            usePrerelease: false,
            logger: new FakeLogger()
        );

        // Assert - net6.0 never references the package at all, so it must not block the update.
        Assert.NotNull(result);
        Assert.Single(result.UpdatedPackages);

        Assert.Contains(
            result.UpdatedPackages,
            x =>
                x.PackageId == "Some.Package"
                && x.From == NuGetVersion.Parse("1.0.0")
                && x.To == NuGetVersion.Parse("2.0.0")
        );

        Assert.Contains("""Version="2.0.0" """.Trim(), File.ReadAllText(_csprojPath));
    }

    [Fact]
    public async Task UpdatePackagesAppliesAlignWithTfmUsingEachConditionedCandidatesOwnTfm()
    {
        // Arrange - a multi-targeted (net6.0;net8.0) project where a Condition on
        // $(TargetFramework) means Runtime.Aligned.Package is referenced at a different starting
        // version per framework (mirroring a real Microsoft.Extensions.* upgrade). All available
        // versions target netstandard2.0, so they're compatible with both net6.0 and net8.0 -
        // alignWithTfm is the only thing capping the major version, and it must cap each
        // conditioned occurrence to its own framework's major, not the lowest major across every
        // target framework of the whole project.
        File.WriteAllText(
            _csprojPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net6.0;net8.0</TargetFrameworks>
              </PropertyGroup>
              <ItemGroup Condition="'$(TargetFramework)'=='net6.0'">
                <PackageReference Include="Runtime.Aligned.Package" Version="6.0.0" />
              </ItemGroup>
              <ItemGroup Condition="'$(TargetFramework)'=='net8.0'">
                <PackageReference Include="Runtime.Aligned.Package" Version="8.0.0" />
              </ItemGroup>
            </Project>
            """
        );

        var csproj = Csproj.Create(_csprojPath);

        var package = new NuGetPackage(
            "Runtime.Aligned.Package",
            [
                new PackageMetadata(
                    NuGetVersion.Parse("6.0.0"),
                    [NuGetFramework.Parse("netstandard2.0")],
                    null,
                    null
                ),
                new PackageMetadata(
                    NuGetVersion.Parse("6.1.0"),
                    [NuGetFramework.Parse("netstandard2.0")],
                    null,
                    null
                ),
                new PackageMetadata(
                    NuGetVersion.Parse("8.0.0"),
                    [NuGetFramework.Parse("netstandard2.0")],
                    null,
                    null
                ),
                new PackageMetadata(
                    NuGetVersion.Parse("8.1.0"),
                    [NuGetFramework.Parse("netstandard2.0")],
                    null,
                    null
                ),
                new PackageMetadata(
                    NuGetVersion.Parse("9.0.0"),
                    [NuGetFramework.Parse("netstandard2.0")],
                    null,
                    null
                ),
            ]
        );

        // Act
        var result = await csproj.UpdatePackagesAsync(
            new Dictionary<string, NuGetPackage?> { ["Runtime.Aligned.Package"] = package },
            dryRun: false,
            usePrerelease: false,
            logger: new FakeLogger(),
            alignWithTfm: ["Runtime.Aligned.*"]
        );

        // Assert - the net6.0 branch is capped at major 6 (=> 6.1.0), the net8.0 branch is capped
        // at major 8 (=> 8.1.0), neither reaching the overall latest, 9.0.0.
        Assert.Equal(2, result!.UpdatedPackages.Count());

        Assert.Contains(
            result.UpdatedPackages,
            x =>
                x.PackageId == "Runtime.Aligned.Package"
                && x.From == NuGetVersion.Parse("6.0.0")
                && x.To == NuGetVersion.Parse("6.1.0")
        );

        Assert.Contains(
            result.UpdatedPackages,
            x =>
                x.PackageId == "Runtime.Aligned.Package"
                && x.From == NuGetVersion.Parse("8.0.0")
                && x.To == NuGetVersion.Parse("8.1.0")
        );

        var content = File.ReadAllText(_csprojPath);

        Assert.Contains("""Version="6.1.0" """.Trim(), content);
        Assert.Contains("""Version="8.1.0" """.Trim(), content);
        Assert.DoesNotContain("""Version="9.0.0" """.Trim(), content);
    }

    [Fact]
    public async Task UpdatePackagesSkipsFloatingVersionWithoutWarningWhenAlreadyLatest()
    {
        // Arrange - a floating version like "4.8.*" isn't a NuGetVersion, but NuGet already
        // resolves it to the latest matching version on restore. If nothing newer than that is
        // available, there's nothing to update.
        File.WriteAllText(
            _csprojPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Some.Package" Version="4.8.*" />
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
                    NuGetVersion.Parse("4.8.5"),
                    [NuGetFramework.Parse("net10.0")],
                    null,
                    null
                ),
            ]
        );

        // Act
        var result = await csproj.UpdatePackagesAsync(
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
        Assert.Contains("""Version="4.8.*" """.Trim(), File.ReadAllText(_csprojPath));
    }

    [Fact]
    public async Task UpdatePackagesReportsDeprecationForFloatingVersionWithoutBumpingIt()
    {
        // Arrange - 1.5.0 is both deprecated and the highest version matching "1.*" (no newer
        // version at all exists), so nothing should be rewritten, but the deprecation should
        // still be reported.
        File.WriteAllText(
            _csprojPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Some.Package" Version="1.*" />
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
                    NuGetVersion.Parse("1.5.0"),
                    [NuGetFramework.Parse("net10.0")],
                    new PackageDeprecationMetadata("deprecated", ["Legacy"], null),
                    null
                ),
            ]
        );

        // Act
        var result = await csproj.UpdatePackagesAsync(
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
        Assert.Contains("""Version="1.*" """.Trim(), File.ReadAllText(_csprojPath));
    }

    [Fact]
    public async Task UpdatePackagesWarnsAndReportsFixedVersionRangeThatCannotBeRewritten()
    {
        // Arrange - "[1.0,2.0)" has no floating segment, so UpdatR doesn't know how to safely
        // rewrite it even though a newer, non-matching version (2.5.0) is available. This should
        // be surfaced clearly (a warning, and a report entry) instead of silently being skipped.
        File.WriteAllText(
            _csprojPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Some.Package" Version="[1.0,2.0)" />
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
                    NuGetVersion.Parse("1.5.0"),
                    [NuGetFramework.Parse("net10.0")],
                    null,
                    null
                ),
                new UpdatR.Internals.PackageMetadata(
                    NuGetVersion.Parse("2.5.0"),
                    [NuGetFramework.Parse("net10.0")],
                    null,
                    null
                ),
            ]
        );

        // Act
        var result = await csproj.UpdatePackagesAsync(
            new Dictionary<string, NuGetPackage?> { ["Some.Package"] = package },
            dryRun: true,
            usePrerelease: false,
            logger: logger
        );

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.UpdatedPackages);

        var unsupported = Assert.Single(result.UnsupportedRangePackages);

        Assert.Equal("Some.Package", unsupported.PackageId);
        Assert.Equal("[1.0,2.0)", unsupported.VersionRange);

        var (level, message) = Assert.Single(logger.Logs);

        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains("[1.0,2.0)", message);
        Assert.Contains(
            """<PackageReference Include="Some.Package" Version="[1.0,2.0)" />""",
            message
        );

        Assert.Contains("""Version="[1.0,2.0)" """.Trim(), File.ReadAllText(_csprojPath));
    }

    [Theory]
    [InlineData(".fsproj")]
    [InlineData(".vbproj")]
    public void CreateAcceptsFsprojAndVbprojFiles(string extension)
    {
        // Arrange - UpdatR has no C#-specific logic; .fsproj/.vbproj use the same SDK-style
        // <PackageReference> item shape as .csproj, so Csproj.Create should accept them too.
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}{extension}");

        File.WriteAllText(
            path,
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

        try
        {
            // Act
            var csproj = Csproj.Create(path);

            // Assert
            Assert.Equal(NuGetVersion.Parse("1.0.0"), csproj.Packages["Some.Package"]);
        }
        finally
        {
            File.Delete(path);
        }
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
