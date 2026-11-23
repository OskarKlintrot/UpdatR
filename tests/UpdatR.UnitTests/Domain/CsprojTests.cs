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

    [Theory]
    [InlineData(
        """
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup>
                <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>
              </PropertyGroup>
              <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
            </Project>
            """
    )]
    [InlineData(
        """
            <Project>
              <PropertyGroup>
                <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
              </PropertyGroup>
              <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
            </Project>
            """
    )]
    [InlineData(
        """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net48</TargetFramework>
              </PropertyGroup>
            </Project>
            """
    )]
    [InlineData(
        """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net10.0;net472</TargetFrameworks>
              </PropertyGroup>
            </Project>
            """
    )]
    public async Task CreateAsyncRejectsEvaluatedNetFrameworkProject(string projectXml)
    {
        // Arrange
        File.WriteAllText(_csprojPath, projectXml);

        // Act
        var exception = await Record.ExceptionAsync(() =>
            RootDir.CreateAsync(
                _csprojPath,
                cancellationToken: TestContext.Current.CancellationToken
            )
        );

        // Assert
        Assert.IsType<InvalidUpdateTargetException>(exception);
        Assert.Equal(
            expected: $"'{_csprojPath}' targets .NET Framework, which UpdatR does not support.",
            actual: exception.Message
        );
        Assert.Equal(expected: projectXml, actual: File.ReadAllText(_csprojPath));
    }

    [Theory]
    [InlineData(
        """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup Condition="'false' == 'true'">
                <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
                <TargetFramework>net48</TargetFramework>
              </PropertyGroup>
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """
    )]
    [InlineData(
        """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <Target Name="Unused">
                <PropertyGroup>
                  <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
                </PropertyGroup>
              </Target>
            </Project>
            """
    )]
    [InlineData(
        """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net48</TargetFramework>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """
    )]
    [InlineData(
        """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup Condition="'false' == 'true'">
                <TargetFrameworks>net48;net10.0</TargetFrameworks>
              </PropertyGroup>
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """
    )]
    public void CreateIgnoresInactiveAndOverriddenFrameworkProperties(string projectXml)
    {
        // Arrange
        File.WriteAllText(_csprojPath, projectXml);

        // Act
        var project = Csproj.Create(_csprojPath);

        // Assert
        Assert.Equal(
            expected: NuGetFramework.Parse("net10.0"),
            actual: Assert.Single(project.TargetFrameworks)
        );
    }

    [Fact]
    public async Task CreateAsyncReportsEvaluationFailureWithoutClaimingNetFramework()
    {
        // Arrange
        File.WriteAllText(
            _csprojPath,
            """
            <Project>
              <PropertyGroup>
                <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
              </PropertyGroup>
              <Import Project="Missing.WebApplication.targets" />
            </Project>
            """
        );

        // Act
        var exception = await Record.ExceptionAsync(() =>
            RootDir.CreateAsync(
                _csprojPath,
                cancellationToken: TestContext.Current.CancellationToken
            )
        );

        // Assert
        Assert.IsType<InvalidUpdateTargetException>(exception);
        Assert.Contains(
            expectedSubstring: $"Could not evaluate project '{_csprojPath}' with MSBuild.",
            actualString: exception.Message,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "Missing.WebApplication.targets",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "targets .NET Framework",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.NotNull(exception.InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateAsyncHandlesInheritedNetFrameworkAndFileExclusion(bool excludeProject)
    {
        // Arrange
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        var projectPath = Path.Combine(directory, "Legacy.csproj");
        var propsPath = Path.Combine(directory, "Directory.Build.props");
        File.WriteAllText(projectPath, """<Project Sdk="Microsoft.NET.Sdk" />""");
        File.WriteAllText(
            propsPath,
            """
            <Project>
              <PropertyGroup>
                <TargetFramework>net48</TargetFramework>
              </PropertyGroup>
            </Project>
            """
        );

        try
        {
            // Act
            var exception = await Record.ExceptionAsync(() =>
                RootDir.CreateAsync(
                    directory,
                    shouldExcludeFile: path => excludeProject && path == projectPath,
                    cancellationToken: TestContext.Current.CancellationToken
                )
            );

            // Assert
            if (excludeProject)
            {
                Assert.Null(exception);
            }
            else
            {
                Assert.IsType<InvalidUpdateTargetException>(exception);
                Assert.Equal(
                    expected: $"'{projectPath}' targets .NET Framework, which UpdatR does not support.",
                    actual: exception.Message
                );
            }
        }
        finally
        {
            File.Delete(projectPath);
            File.Delete(propsPath);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task CreateAsyncReusesInitialEvaluationForPropsDiscovery()
    {
        // Arrange
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var configDirectory = Path.Combine(directory, ".config");
        Directory.CreateDirectory(configDirectory);
        var projectPath = Path.Combine(directory, "Example.csproj");
        var propsPath = Path.Combine(directory, "Directory.Build.props");
        var toolsPath = Path.Combine(configDirectory, "dotnet-tools.json");
        File.WriteAllText(
            projectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """
        );
        File.WriteAllText(
            propsPath,
            """
            <Project>
              <ItemGroup>
                <PackageReference Include="Imported.Package" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """
        );
        File.WriteAllText(toolsPath, """{"version":1,"isRoot":true,"tools":{}}""");
        var projectChanged = false;

        try
        {
            // Act
            var root = await RootDir.CreateAsync(
                projectPath,
                shouldExcludeFile: path =>
                {
                    if (path == toolsPath)
                    {
                        // Tool discovery runs after project loading, before props discovery.
                        File.WriteAllText(
                            projectPath,
                            """<Project><Import Project="Missing.targets" /></Project>"""
                        );
                        projectChanged = true;
                    }

                    return false;
                },
                cancellationToken: TestContext.Current.CancellationToken
            );

            // Assert
            Assert.True(projectChanged);
            Assert.Equal(expected: propsPath, actual: Assert.Single(root.PropsFiles).Path);
        }
        finally
        {
            File.Delete(toolsPath);
            File.Delete(propsPath);
            File.Delete(projectPath);
            Directory.Delete(configDirectory);
            Directory.Delete(directory);
        }
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
        // Arrange - version 2.0.0 only targets net8.0, so the net6.0 part of this multi-targeted
        // project can't use it. Nothing should be updated even though the net8.0 part could move
        // to 2.0.0, since that could break the build for net6.0.
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdatePackagesUpdatesConditionedPackageReferenceOnlyDeclaredForOneOfMultipleTfms(
        bool changeProjectAfterLoading
    )
    {
        // Arrange - only net8.0 references the package, whose newer version cannot be used by
        // net6.0. Changes on disk must not discard the initial per-framework evaluation.
        File.WriteAllText(
            _csprojPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net6.0;net8.0</TargetFrameworks>
              </PropertyGroup>
              <ItemGroup Condition="'$(TargetFramework)'=='net8.0'">
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
                    [NuGetFramework.Parse("net8.0")],
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

        if (changeProjectAfterLoading)
        {
            File.WriteAllText(
                _csprojPath,
                """<Project><Import Project="Missing.targets" /></Project>"""
            );
        }

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

    [Theory]
    [InlineData("5.0.16", "5.0.12", "Version")]
    [InlineData("5.0.12", "5.0.16", "Version")]
    [InlineData("5.0.16", "5.0.12", "VersionOverride")]
    public void GetPinnedVersionConditionedReferencesUsesLowestVersionPerFramework(
        string firstVersion,
        string secondVersion,
        string attribute
    )
    {
        // Arrange
        File.WriteAllText(
            _csprojPath,
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net6.0;net8.0</TargetFrameworks>
              </PropertyGroup>
              <ItemGroup Condition="'$(TargetFramework)' == 'net8.0'">
                <PackageReference Include="Microsoft.EntityFrameworkCore" {attribute}="{firstVersion}" />
              </ItemGroup>
              <ItemGroup Condition="'$(TargetFramework)' == 'net6.0'">
                <PackageReference Include="Microsoft.EntityFrameworkCore" {attribute}="{secondVersion}" />
              </ItemGroup>
            </Project>
            """
        );
        var csproj = Csproj.Create(_csprojPath);

        // Act
        var result = csproj.GetPinnedVersion("Microsoft.EntityFrameworkCore*");

        // Assert
        Assert.Equal(expected: NuGetVersion.Parse("5.0.12"), actual: result);
    }

    [Fact]
    public void GetPinnedVersionInactiveReferenceDoesNotContribute()
    {
        // Arrange
        File.WriteAllText(
            _csprojPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup Condition="'false' == 'true'">
                <PackageReference Include="Microsoft.EntityFrameworkCore" Version="5.0.12" />
              </ItemGroup>
            </Project>
            """
        );
        var csproj = Csproj.Create(_csprojPath);

        // Act
        var result = csproj.GetPinnedVersion("Microsoft.EntityFrameworkCore*");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetPinnedVersionSingleFrameworkVersionOverrideTakesPrecedence()
    {
        // Arrange
        File.WriteAllText(
            _csprojPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Some.Package" VersionOverride="1.0.0" />
                <PackageVersion Include="Some.Package" Version="2.0.0" />
              </ItemGroup>
            </Project>
            """
        );
        var csproj = Csproj.Create(_csprojPath);

        // Act
        var result = csproj.GetPinnedVersion("Some.Package");

        // Assert
        Assert.Equal(expected: NuGetVersion.Parse("1.0.0"), actual: result);
    }

    [Fact]
    public void GetPinnedVersionDifferentPackagesPerFrameworkDoesNotCauseAmbiguity()
    {
        // Arrange
        File.WriteAllText(
            _csprojPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net6.0;net8.0</TargetFrameworks>
              </PropertyGroup>
              <ItemGroup Condition="'$(TargetFramework)' == 'net6.0'">
                <PackageReference Include="Some.Package.A" Version="1.0.0" />
              </ItemGroup>
              <ItemGroup Condition="'$(TargetFramework)' == 'net8.0'">
                <PackageReference Include="Some.Package.B" Version="2.0.0" />
              </ItemGroup>
            </Project>
            """
        );
        var csproj = Csproj.Create(_csprojPath);

        // Act
        var result = csproj.GetPinnedVersion("Some.Package.*");

        // Assert
        Assert.Equal(expected: NuGetVersion.Parse("1.0.0"), actual: result);
    }

    [Theory]
    [InlineData(true, "Version")]
    [InlineData(false, "Version")]
    [InlineData(true, "VersionOverride")]
    public async Task GetPinnedVersionUpdatedConditionedReferencesUsesUpdatedVersions(
        bool dryRun,
        string attribute
    )
    {
        // Arrange
        var original = $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net6.0;net8.0</TargetFrameworks>
              </PropertyGroup>
              <ItemGroup Condition="'$(TargetFramework)' == 'net8.0'">
                <PackageReference Include="Some.Package" {attribute}="2.0.0" />
              </ItemGroup>
              <ItemGroup Condition="'$(TargetFramework)' == 'net6.0'">
                <PackageReference Include="Some.Package" {attribute}="1.0.0" />
              </ItemGroup>
            </Project>
            """;
        File.WriteAllText(_csprojPath, original);
        var csproj = Csproj.Create(_csprojPath);
        var package = new NuGetPackage(
            "Some.Package",
            [
                new PackageMetadata(
                    NuGetVersion.Parse("1.5.0"),
                    [NuGetFramework.Parse("net6.0")],
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
        await csproj.UpdatePackagesAsync(
            new Dictionary<string, NuGetPackage?> { ["Some.Package"] = package },
            dryRun: dryRun,
            usePrerelease: false,
            logger: new FakeLogger()
        );
        var result = csproj.GetPinnedVersion("Some.Package");

        // Assert
        Assert.Equal(expected: NuGetVersion.Parse("1.5.0"), actual: result);
        if (dryRun)
        {
            Assert.Equal(expected: original, actual: File.ReadAllText(_csprojPath));
        }
    }

    [Fact]
    public void GetPinnedVersionReturnsNullWhenNoPackageMatches()
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

        // Act
        var result = csproj.GetPinnedVersion("Microsoft.EntityFrameworkCore*");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetPinnedVersionReturnsVersionOfSingleMatchingPackage()
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
                <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="9.0.0" />
              </ItemGroup>
            </Project>
            """
        );

        var csproj = Csproj.Create(_csprojPath);

        // Act
        var result = csproj.GetPinnedVersion("Microsoft.EntityFrameworkCore*");

        // Assert
        Assert.Equal(NuGetVersion.Parse("9.0.0"), result);
    }

    [Fact]
    public void GetPinnedVersionReturnsVersionWhenAllMatchingPackagesShareTheSameVersion()
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
                <PackageReference Include="Microsoft.EntityFrameworkCore" Version="9.0.0" />
                <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="9.0.0" />
                <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="9.0.0" />
              </ItemGroup>
            </Project>
            """
        );

        var csproj = Csproj.Create(_csprojPath);

        // Act
        var result = csproj.GetPinnedVersion("Microsoft.EntityFrameworkCore*");

        // Assert
        Assert.Equal(NuGetVersion.Parse("9.0.0"), result);
    }

    [Fact]
    public void GetPinnedVersionOnlyMatchesExactPackageIdWhenPatternHasNoWildcard()
    {
        // Arrange - without a "*" the pattern must match the whole package id, not just a prefix,
        // e.g. "xunit" must not match "xunit.core".
        File.WriteAllText(
            _csprojPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="xunit" Version="2.9.0" />
                <PackageReference Include="xunit.core" Version="2.9.2" />
              </ItemGroup>
            </Project>
            """
        );

        var csproj = Csproj.Create(_csprojPath);

        // Act
        var result = csproj.GetPinnedVersion("xunit");

        // Assert
        Assert.Equal(NuGetVersion.Parse("2.9.0"), result);
    }

    [Fact]
    public void GetPinnedVersionThrowsWhenMatchingPackagesHaveDifferentVersions()
    {
        // Arrange - a too-broad pattern like "xunit*" matches unrelated packages that don't share
        // a version scheme, e.g. xunit.v3 and xunit.runner.visualstudio.
        File.WriteAllText(
            _csprojPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="xunit.v3" Version="4.0.0" />
                <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
              </ItemGroup>
            </Project>
            """
        );

        var csproj = Csproj.Create(_csprojPath);

        // Act
        var exception = Assert.Throws<AmbiguousToolPackagePinException>(() =>
            csproj.GetPinnedVersion("xunit*")
        );

        // Assert
        Assert.Contains("xunit*", exception.Message);
        Assert.Contains("xunit.v3 4.0.0", exception.Message);
        Assert.Contains("xunit.runner.visualstudio 2.8.2", exception.Message);
    }

    [Theory]
    [InlineData("1.0.0", false, "1.5.0")]
    [InlineData("1.0.0", true, null)]
    public async Task UpdatePackagesSkipsVersionsBlockedByMinPublishAge(
        string installed,
        bool blockOlderUpdate,
        string? expectedUpdate
    )
    {
        // Arrange - 2.0.0 is too recent. Whether or not an older update is eligible, 2.0.0 is
        // reported as skipped because of the minimum publish age, not as an incompatible target
        // framework or an unknown package.
        File.WriteAllText(
            _csprojPath,
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Some.Package" Version="{installed}" />
              </ItemGroup>
            </Project>
            """
        );

        var csproj = Csproj.Create(_csprojPath);

        var package = new NuGetPackage(
            "Some.Package",
            [
                Metadata("1.0.0"),
                Metadata("1.5.0", blockedByMinPublishAge: blockOlderUpdate),
                Metadata("2.0.0", blockedByMinPublishAge: true),
            ]
        );

        // Act
        var result = await csproj.UpdatePackagesAsync(
            new Dictionary<string, NuGetPackage?> { ["Some.Package"] = package },
            dryRun: false,
            usePrerelease: false,
            logger: new FakeLogger()
        );

        // Assert
        Assert.NotNull(result);

        var skipped = Assert.Single(result.SkippedUpdatePackages);

        Assert.Equal(NuGetVersion.Parse("2.0.0"), skipped.Version);
        Assert.Equal(SkippedUpdateReason.MinimumPublishAge, skipped.Reason);

        var content = File.ReadAllText(_csprojPath);

        if (expectedUpdate is null)
        {
            Assert.Empty(result.UpdatedPackages);
            Assert.Contains($"""Version="{installed}" """.Trim(), content);
        }
        else
        {
            var updated = Assert.Single(result.UpdatedPackages);

            Assert.Equal(NuGetVersion.Parse(expectedUpdate), updated.To);
            Assert.Contains($"""Version="{expectedUpdate}" """.Trim(), content);
        }
    }

    [Theory]
    [InlineData("1.*")]
    [InlineData("[1.0.0,2.0.0)")]
    public async Task UpdatePackagesDoesNotRewriteVersionRangeToVersionBlockedByMinPublishAge(
        string range
    )
    {
        // Arrange - the only version outside the range is too recent, so the range stays as is.
        File.WriteAllText(
            _csprojPath,
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Some.Package" Version="{range}" />
              </ItemGroup>
            </Project>
            """
        );

        var csproj = Csproj.Create(_csprojPath);

        var package = new NuGetPackage(
            "Some.Package",
            [Metadata("1.5.0"), Metadata("2.0.0", blockedByMinPublishAge: true)]
        );

        // Act
        var result = await csproj.UpdatePackagesAsync(
            new Dictionary<string, NuGetPackage?> { ["Some.Package"] = package },
            dryRun: false,
            usePrerelease: false,
            logger: new FakeLogger()
        );

        // Assert
        Assert.Empty(result?.UpdatedPackages ?? []);
        Assert.Contains($"""Version="{range}" """.Trim(), File.ReadAllText(_csprojPath));
    }

    private static PackageMetadata Metadata(string version, bool blockedByMinPublishAge = false) =>
        new(
            NuGetVersion.Parse(version),
            [NuGetFramework.Parse("net8.0")],
            null,
            null,
            BlockedByMinPublishAge: blockedByMinPublishAge
        );

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
