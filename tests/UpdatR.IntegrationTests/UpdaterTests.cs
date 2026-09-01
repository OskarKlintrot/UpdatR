using System;
using System.Diagnostics.CodeAnalysis;
using UpdatR.Domain;
using static UpdatR.IntegrationTests.FileCreationUtils;

namespace UpdatR.IntegrationTests;

[SuppressMessage(
    "Naming",
    "CA1707:Identifiers should not contain underscores",
    Justification = "Test methods"
)]
[SuppressMessage(
    "Usage",
    "xUnit1012:Null should only be used for nullable parameters",
    Justification = "https://github.com/xunit/xunit/issues/2973"
)]
public class UpdaterTests
{
    [Theory]
    [InlineData("0.0.1")]
    [InlineData("0.0.2")]
    public async Task Given_UpToDate_When_Update_Then_DoNothing(string version)
    {
        // Arrange
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(Given_UpToDate_When_Update_Then_DoNothing)
        );
        var tempCsproj = Path.Combine(temp, "Dummy.App.csproj");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);

        var csprojOriginal = await CreateTempCsprojAsync(
            tempCsproj,
            new KeyValuePair<string, string>("Dummy", version)
        );

        CreateNuGetConfig(tempNuget);

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(tempCsproj);

        // Assert
        await Verify(GetVerifyObjects()).UseParameters(version);

        async IAsyncEnumerable<object> GetVerifyObjects()
        {
            yield return summary.UpdatedPackages;
            yield return csprojOriginal;
            yield return await File.ReadAllTextAsync(tempCsproj);
        }
    }

    [Fact]
    public async Task Given_DirectoryAsTarget_When_SingleCsproj_Then_Update()
    {
        // Arrange
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(Given_DirectoryAsTarget_When_SingleCsproj_Then_Update)
        );
        var tempCsproj = Path.Combine(temp, "Dummy.App.csproj");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);

        var csprojOriginal = await CreateTempCsprojAsync(
            tempCsproj,
            new KeyValuePair<string, string>("Dummy", "0.0.1")
        );

        CreateNuGetConfig(tempNuget);

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(tempCsproj);

        // Assert
        await Verify(GetVerifyObjects());

        async IAsyncEnumerable<object> GetVerifyObjects()
        {
            yield return summary.UpdatedPackages;
            yield return csprojOriginal;
            yield return await File.ReadAllTextAsync(tempCsproj);
        }
    }

    [Theory]
    [InlineData("net5.0")] // Unsupported in 0.0.2
    [InlineData("net6.0")] // Current TFM
    [InlineData("net7.0")] // Future TFM
    public async Task Given_TFM_When_UnsupportedInNewerVersions_Then_DoNothing(string tfm)
    {
        // Arrange
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(Given_TFM_When_UnsupportedInNewerVersions_Then_DoNothing)
        );
        var tempCsproj = Path.Combine(temp, "Dummy.App.csproj");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);

        var csprojOriginal = await CreateTempCsprojAsync(
            tempCsproj,
            new KeyValuePair<string, string>("Dummy", "0.0.1")
        );

        CreateNuGetConfig(tempNuget);

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(tempCsproj, targetFrameworkMoniker: tfm);

        // Assert
        await Verify(GetVerifyObjects()).UseParameters(tfm);

        async IAsyncEnumerable<object> GetVerifyObjects()
        {
            yield return summary.UpdatedPackages;
            yield return csprojOriginal;
            yield return await File.ReadAllTextAsync(tempCsproj);
        }
    }

    [Theory]
    [InlineData("0.0.1")]
    [InlineData("0.0.2")]
    public async Task Given_UpToDate_When_UpdateDotnetConfig_Then_DoNothing(string version)
    {
        // Arrange
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(Given_UpToDate_When_UpdateDotnetConfig_Then_DoNothing)
        );
        var tempDotnetConfig = Path.Combine(temp, ".config", "dotnet-tools.json");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(new FileInfo(tempDotnetConfig).DirectoryName!);

        var original = await CreateToolsConfigAsync(
            path: tempDotnetConfig,
            packageId: "Dummy.Tool",
            version: version,
            command: "dummy"
        );

        CreateNuGetConfig(tempNuget);

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(tempDotnetConfig);

        // Assert
        await Verify(GetVerifyObjects()).UseParameters(version);

        async IAsyncEnumerable<object> GetVerifyObjects()
        {
            yield return summary.UpdatedPackages;
            yield return original;
            yield return await File.ReadAllTextAsync(tempDotnetConfig);
        }
    }

    [Fact]
    public async Task Given_DirectoryAsTarget_When_SingleDotnetConfig_Then_Update()
    {
        // Arrange
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(Given_DirectoryAsTarget_When_SingleDotnetConfig_Then_Update)
        );
        var tempDotnetConfig = Path.Combine(temp, ".config", "dotnet-tools.json");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(new FileInfo(tempDotnetConfig).DirectoryName!);

        var original = await CreateToolsConfigAsync(
            path: tempDotnetConfig,
            packageId: "Dummy.Tool",
            version: "0.0.1",
            command: "dummy"
        );

        CreateNuGetConfig(tempNuget);

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(tempDotnetConfig);

        // Assert
        await Verify(GetVerifyObjects());

        async IAsyncEnumerable<object> GetVerifyObjects()
        {
            yield return summary.UpdatedPackages;
            yield return original;
            yield return await File.ReadAllTextAsync(tempDotnetConfig);
        }
    }

    [Fact]
    public async Task Given_DotnetToolsHasTemplateComments_When_Update_Then_PreserveComments()
    {
        // Arrange
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(Given_DotnetToolsHasTemplateComments_When_Update_Then_PreserveComments)
        );
        var tempDotnetConfig = Path.Combine(temp, ".config", "dotnet-tools.json");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(new FileInfo(tempDotnetConfig).DirectoryName!);

        var original = await CreateToolsConfigWithCommentsAsync(
            path: tempDotnetConfig,
            packageId: "Dummy.Tool",
            version: "0.0.1",
            command: "dummy"
        );

        CreateNuGetConfig(tempNuget);

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(tempDotnetConfig);

        // Assert
        var updated = await File.ReadAllTextAsync(
            tempDotnetConfig,
            TestContext.Current.CancellationToken
        );

        Assert.Contains("//#if (mode != \"proxy\")", updated, StringComparison.Ordinal);
        Assert.Contains("//#endif", updated, StringComparison.Ordinal);

        await Verify(GetVerifyObjects());

        async IAsyncEnumerable<object> GetVerifyObjects()
        {
            yield return summary.UpdatedPackages;
            yield return original;
            yield return updated;
        }
    }

    [Fact]
    public async Task Given_Target_When_DryRun_Then_DoNothing()
    {
        // Arrange
        var temp = Path.Combine(Paths.Temporary.Root, nameof(Given_Target_When_Valid_Then_Update));
        var target = Path.Combine(temp, "Dummy.sln");
        var tempSln = Path.Combine(temp, "Dummy.sln");
        var tempDotnetConfig = Path.Combine(temp, "src", ".config", "dotnet-tools.json");
        var tempCsproj = Path.Combine(temp, "src", "Dummy.App.csproj");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(new FileInfo(tempDotnetConfig).DirectoryName!);

        var slnOriginal = await CreateSlnAsync(tempSln, "Dummy.App.csproj", tempCsproj);

        var csprojOriginal = await CreateTempCsprojAsync(
            tempCsproj,
            new KeyValuePair<string, string>("Dummy", "0.0.1")
        );

        var toolsOriginal = await CreateToolsConfigAsync(
            path: tempDotnetConfig,
            packageId: "Dummy.Tool",
            version: "0.0.1",
            command: "dummy"
        );

        CreateNuGetConfig(tempNuget);

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(target, dryRun: true);

        // Assert
        Assert.Equal(
            slnOriginal,
            await File.ReadAllTextAsync(tempSln, TestContext.Current.CancellationToken)
        );
        await Verify(GetVerifyObjects());

        async IAsyncEnumerable<object> GetVerifyObjects()
        {
            yield return summary.UpdatedPackages;

            yield return toolsOriginal;
            yield return await File.ReadAllTextAsync(tempDotnetConfig);

            yield return csprojOriginal;
            yield return await File.ReadAllTextAsync(tempCsproj);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("Dummy.sln")]
    [InlineData("src")]
    [InlineData("src", "Dummy.App.csproj")]
    [InlineData("src", ".config")]
    [InlineData("src", ".config", "dotnet-tools.json")]
    public async Task Given_Target_When_Valid_Then_Update(params string[] paths)
    {
        // Arrange
        var temp = Path.Combine(Paths.Temporary.Root, nameof(Given_Target_When_Valid_Then_Update));
        var target = Path.Combine(temp, Path.Combine(paths));
        var tempSln = Path.Combine(temp, "Dummy.sln");
        var tempDotnetConfig = Path.Combine(temp, "src", ".config", "dotnet-tools.json");
        var tempCsproj = Path.Combine(temp, "src", "Dummy.App.csproj");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(new FileInfo(tempDotnetConfig).DirectoryName!);

        var slnOriginal = await CreateSlnAsync(tempSln, "Dummy.App.csproj", tempCsproj);

        var csprojOriginal = await CreateTempCsprojAsync(
            tempCsproj,
            new KeyValuePair<string, string>("Dummy", "0.0.1")
        );

        var toolsOriginal = await CreateToolsConfigAsync(
            path: tempDotnetConfig,
            packageId: "Dummy.Tool",
            version: "0.0.1",
            command: "dummy"
        );

        CreateNuGetConfig(tempNuget);

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(target);

        // Assert
        Assert.Equal(
            slnOriginal,
            await File.ReadAllTextAsync(tempSln, TestContext.Current.CancellationToken)
        );
        await Verify(GetVerifyObjects()).UseParameters(string.Join('/', paths));

        async IAsyncEnumerable<object> GetVerifyObjects()
        {
            yield return summary.UpdatedPackages;

            yield return toolsOriginal;
            yield return await File.ReadAllTextAsync(tempDotnetConfig);

            yield return csprojOriginal;
            yield return await File.ReadAllTextAsync(tempCsproj);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Microsoft.*")]
    [InlineData("Dummy.*")]
    [InlineData("Dummy.*", "Microsoft.*")]
    [InlineData("Dummy.*", "has.*")]
    public async Task Given_Packages_When_Update_Then_OnlyUpdateThatPackages(
        params string[]? packages
    )
    {
        // Arrange
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(Given_Packages_When_Update_Then_OnlyUpdateThatPackages)
        );
        var tempCsproj = Path.Combine(temp, "src", "Dummy.App.csproj");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(Path.GetDirectoryName(tempCsproj)!);

        var csprojOriginal = await CreateTempCsprojAsync(
            tempCsproj,
            new KeyValuePair<string, string>("Dummy.Tool", "0.0.1"),
            new KeyValuePair<string, string>("Has.Previews", "0.0.1")
        );

        CreateNuGetConfig(tempNuget);

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(temp, null, packages);

        // Assert
        await Verify(GetVerifyObjects()).UseParameters(string.Join('/', packages ?? []));

        async IAsyncEnumerable<object> GetVerifyObjects()
        {
            yield return summary.UpdatedPackages;

            yield return csprojOriginal;
            yield return await File.ReadAllTextAsync(tempCsproj);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Microsoft.*")]
    [InlineData("Dummy.*")]
    [InlineData("Dummy.*", "Microsoft.*")]
    [InlineData("Dummy.*", "has.*")]
    public async Task Given_ExcludedPackage_When_Update_Then_DoNotUpdate(
        params string[]? excludedPackages
    )
    {
        // Arrange
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(Given_ExcludedPackage_When_Update_Then_DoNotUpdate)
        );
        var tempCsproj = Path.Combine(temp, "src", "Dummy.App.csproj");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(Path.GetDirectoryName(tempCsproj)!);

        var csprojOriginal = await CreateTempCsprojAsync(
            tempCsproj,
            new KeyValuePair<string, string>("Dummy.Tool", "0.0.1"),
            new KeyValuePair<string, string>("Has.Previews", "0.0.1")
        );

        CreateNuGetConfig(tempNuget);

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(temp, excludedPackages);

        // Assert
        await Verify(GetVerifyObjects()).UseParameters(string.Join('/', excludedPackages ?? []));

        async IAsyncEnumerable<object> GetVerifyObjects()
        {
            yield return summary.UpdatedPackages;

            yield return csprojOriginal;
            yield return await File.ReadAllTextAsync(tempCsproj);
        }
    }

    [Fact]
    public async Task Given_UpdatRRcFile_When_ExcludesPackage_Then_DoNotUpdate()
    {
        // Arrange
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(Given_UpdatRRcFile_When_ExcludesPackage_Then_DoNotUpdate)
        );
        var tempCsproj = Path.Combine(temp, "src", "Dummy.App.csproj");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(Path.GetDirectoryName(tempCsproj)!);

        var csprojOriginal = await CreateTempCsprojAsync(
            tempCsproj,
            new KeyValuePair<string, string>("Dummy.Tool", "0.0.1"),
            new KeyValuePair<string, string>("Has.Previews", "0.0.1")
        );

        CreateNuGetConfig(tempNuget);

        await CreateUpdatRConfigAsync(
            Path.Combine(temp, ".updatrrc"),
            """{ "excludePackages": ["Dummy.*"] }"""
        );

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(temp);

        // Assert
        await Verify(GetVerifyObjects());

        async IAsyncEnumerable<object> GetVerifyObjects()
        {
            yield return summary.UpdatedPackages;

            yield return csprojOriginal;
            yield return await File.ReadAllTextAsync(tempCsproj);
        }
    }

    [Fact]
    public async Task Given_UpdatRRcFileAndCliExcludePackage_When_Update_Then_ExcludesAreMerged()
    {
        // Arrange
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(Given_UpdatRRcFileAndCliExcludePackage_When_Update_Then_ExcludesAreMerged)
        );
        var tempCsproj = Path.Combine(temp, "src", "Dummy.App.csproj");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(Path.GetDirectoryName(tempCsproj)!);

        var csprojOriginal = await CreateTempCsprojAsync(
            tempCsproj,
            new KeyValuePair<string, string>("Dummy.Tool", "0.0.1"),
            new KeyValuePair<string, string>("Has.Previews", "0.0.1")
        );

        CreateNuGetConfig(tempNuget);

        // Config excludes Dummy.*, CLI excludes Has.*: both should end up excluded (union).
        await CreateUpdatRConfigAsync(
            Path.Combine(temp, ".updatrrc"),
            """{ "excludePackages": ["Dummy.*"] }"""
        );

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(temp, ["Has.*"]);

        // Assert
        await Verify(GetVerifyObjects());

        async IAsyncEnumerable<object> GetVerifyObjects()
        {
            yield return summary.UpdatedPackages;

            yield return csprojOriginal;
            yield return await File.ReadAllTextAsync(tempCsproj);
        }
    }

    [Theory]
    [InlineData("Dummy.*", "Microsoft.*")]
    [InlineData("Dummy.*", "Dummy.Tool")]
    public async Task Given_PackageAndExcludedPackage_When_Update_Then_ExcludeWins(
        string packages,
        string? excludedPackages
    )
    {
        // Arrange
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(Given_Packages_When_Update_Then_OnlyUpdateThatPackages)
        );
        var tempCsproj = Path.Combine(temp, "src", "Dummy.App.csproj");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(Path.GetDirectoryName(tempCsproj)!);

        var csprojOriginal = await CreateTempCsprojAsync(
            tempCsproj,
            new KeyValuePair<string, string>("Dummy.Tool", "0.0.1"),
            new KeyValuePair<string, string>("Has.Previews", "0.0.1")
        );

        CreateNuGetConfig(tempNuget);

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(
            temp,
            excludedPackages is null ? null : [excludedPackages],
            [packages]
        );

        // Assert
        await Verify(GetVerifyObjects()).UseParameters(packages + ' ' + excludedPackages);

        async IAsyncEnumerable<object> GetVerifyObjects()
        {
            yield return summary.UpdatedPackages;

            yield return csprojOriginal;
            yield return await File.ReadAllTextAsync(tempCsproj);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("Dummy.sln")]
    public async Task Given_CsprojNotAddedToSln_When_TargetSln_Then_DoNothing(string target)
    {
        // Arrange
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(Given_CsprojNotAddedToSln_When_TargetSln_Then_DoNothing)
        );
        var tempSln = Path.Combine(temp, "Dummy.sln");
        var tempDotnetConfig = Path.Combine(temp, "src", ".config", "dotnet-tools.json");
        var tempCsproj1 = Path.Combine(temp, "src", "Dummy.App.csproj");
        var tempCsproj2 = Path.Combine(temp, "src", "Dummy.Lib.csproj");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(new FileInfo(tempDotnetConfig).DirectoryName!);

        var slnOriginal = await CreateSlnAsync(tempSln, "Dummy.App.csproj", tempCsproj1);

        var csproj1Original = await CreateTempCsprojAsync(
            tempCsproj1,
            new KeyValuePair<string, string>("Dummy", "0.0.1")
        );

        var csproj2Original = await CreateTempCsprojAsync(
            tempCsproj2,
            new KeyValuePair<string, string>("Dummy", "0.0.1")
        );

        var toolsOriginal = await CreateToolsConfigAsync(
            path: tempDotnetConfig,
            packageId: "Dummy.Tool",
            version: "0.0.1",
            command: "dummy"
        );

        CreateNuGetConfig(tempNuget);

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(Path.Combine(temp, target));

        // Assert
        Assert.Equal(
            slnOriginal,
            await File.ReadAllTextAsync(tempSln, TestContext.Current.CancellationToken)
        );
        await Verify(GetVerifyObjects()).UseParameters(target);

        async IAsyncEnumerable<object> GetVerifyObjects()
        {
            yield return summary.UpdatedPackages;

            yield return toolsOriginal;
            yield return await File.ReadAllTextAsync(tempDotnetConfig);

            yield return csproj1Original;
            yield return await File.ReadAllTextAsync(tempCsproj1);

            yield return csproj2Original;
            yield return await File.ReadAllTextAsync(tempCsproj2);
        }
    }

    [Theory]
    [InlineData("0.0.1-preview")] // Upgrade to 0.0.2, the highest stable
    [InlineData("0.0.1")] // Upgrade to 0.0.2, the highest stable
    [InlineData("0.0.3-preview.0")] // Upgrade to 0.0.3-preview.1, there is no stable higher than 0.0.3-preview.0 so upgrade to higher prerelease instead
    public async Task Given_PackageWithPrerelease_When_Update_Then_StopAtStableIfPossible(
        string version
    )
    {
        // Arrange
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(Given_PackageWithPrerelease_When_Update_Then_StopAtStableIfPossible)
        );
        var tempDotnetConfig = Path.Combine(temp, "src", ".config", "dotnet-tools.json");
        var tempCsproj = Path.Combine(temp, "src", "Dummy.App.csproj");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(new FileInfo(tempDotnetConfig).DirectoryName!);

        var csprojOriginal = await CreateTempCsprojAsync(
            tempCsproj,
            new KeyValuePair<string, string>("Has.Previews", version)
        );

        var toolsOriginal = await CreateToolsConfigAsync(
            path: tempDotnetConfig,
            packageId: "Has.Previews",
            version: version,
            command: "previews"
        );

        CreateNuGetConfig(tempNuget);

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(temp);

        // Assert
        await Verify(GetVerifyObjects()).UseParameters(version);

        async IAsyncEnumerable<object> GetVerifyObjects()
        {
            yield return summary.UpdatedPackagesCount;

            yield return toolsOriginal;
            yield return await File.ReadAllTextAsync(tempDotnetConfig);

            yield return csprojOriginal;
            yield return await File.ReadAllTextAsync(tempCsproj);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Given_UnknownPackageId_When_Updating_Then_DoNothing(bool hasNugetConfig)
    {
        // Arrange
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(Given_UnknownPackageId_When_Updating_Then_DoNothing),
            hasNugetConfig.ToString()
        );

        var tempDotnetConfig = Path.Combine(temp, "src", ".config", "dotnet-tools.json");
        var tempCsproj = Path.Combine(temp, "src", "Dummy.App.csproj");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(new FileInfo(tempDotnetConfig).DirectoryName!);

        var csprojOriginal = await CreateTempCsprojAsync(
            tempCsproj,
            new KeyValuePair<string, string>("Dummy", "0.0.1")
        );

        var toolsOriginal = await CreateToolsConfigAsync(
            path: tempDotnetConfig,
            packageId: "Dummy.Tool",
            version: "0.0.1",
            command: "dummy"
        );

        if (hasNugetConfig)
        {
            CreateNuGetConfig(tempNuget);
        }

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(temp);

        // Assert
        await Verify(GetVerifyObjects()).UseParameters(hasNugetConfig);

        async IAsyncEnumerable<object> GetVerifyObjects()
        {
            yield return summary.UpdatedPackagesCount;

            yield return toolsOriginal;
            yield return await File.ReadAllTextAsync(tempDotnetConfig);

            yield return csprojOriginal;
            yield return await File.ReadAllTextAsync(tempCsproj);
        }
    }

    [Fact]
    public async Task Given_LatestPackageHasUnsupportedTfm_When_Update_Then_PickLatestSupportedTfm()
    {
        // Arrange
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(Given_LatestPackageHasUnsupportedTfm_When_Update_Then_PickLatestSupportedTfm)
        );
        var tempCsproj = Path.Combine(temp, "Dummy.App.csproj");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);

        var csprojOriginal = await CreateTempCsprojAsync(
            tempCsproj,
            "net5.0",
            new KeyValuePair<string, string>("Has.Newer.Tfm", "3.1.0")
        );

        CreateNuGetConfig(tempNuget);

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(tempCsproj);

        // Assert
        await Verify(GetVerifyObjects());

        async IAsyncEnumerable<object> GetVerifyObjects()
        {
            yield return summary.UpdatedPackages;
            yield return csprojOriginal;
            yield return await File.ReadAllTextAsync(tempCsproj);
        }
    }

    [Theory]
    [InlineData(".")]
    [InlineData("Dummy.sln")]
    public async Task Given_OutdatedDotnetEf_When_UpdatingCsprojToNewer_Then_UpdateToCsprojVersion(
        params string[] path
    )
    {
        // Arrange
        var targetPath = Path.Combine(path);
        var temp = Path.Combine(Paths.Temporary.Root, "kjsdfj");
        var target = Path.Combine(temp, "src", targetPath);
        var tempSln = Path.Combine(temp, "src", "Dummy.sln");
        var tempDotnetConfig = Path.Combine(temp, "src", ".config", "dotnet-tools.json");
        var tempCsproj = Path.Combine(temp, "src", "Dummy.App.csproj");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(new FileInfo(tempDotnetConfig).DirectoryName!);

        var csprojOriginal = await CreateTempCsprojAsync(
            tempCsproj,
            "net5.0",
            new KeyValuePair<string, string>("Microsoft.EntityFrameworkCore", "5.0.5")
        );

        var toolsOriginal = await CreateToolsConfigAsync(
            path: tempDotnetConfig,
            packageId: "dotnet-ef",
            version: "5.0.5",
            command: "dotnet"
        );

        CreateNuGetConfig(tempNuget);

        var slnOriginal = await CreateSlnAsync(tempSln, "Dummy.App.csproj", tempCsproj);

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(target);

        // Assert
        Assert.Equal(
            slnOriginal,
            await File.ReadAllTextAsync(tempSln, TestContext.Current.CancellationToken)
        );
        await Verify(GetVerifyObjects()).UseParameters(targetPath);

        async IAsyncEnumerable<object> GetVerifyObjects()
        {
            yield return summary.UpdatedPackages;

            yield return toolsOriginal;
            yield return await File.ReadAllTextAsync(tempDotnetConfig);

            yield return csprojOriginal;
            yield return await File.ReadAllTextAsync(tempCsproj);
        }
    }

    [Theory]
    [InlineData(".")]
    [InlineData(".config")]
    [InlineData(".config", "dotnet-tools.json")]
    public async Task Given_OutdatedDotnetEf_When_CsprojHasNewer_Then_UpdateToCsprojVersion(
        params string[] path
    )
    {
        // Arrange
        var targetPath = Path.Combine(path);
        var temp = Path.Combine(Paths.Temporary.Root, "kjsdfj");
        var target = Path.Combine(temp, "src", targetPath);
        var tempDotnetConfig = Path.Combine(temp, "src", ".config", "dotnet-tools.json");
        var tempCsproj = Path.Combine(temp, "src", "Dummy.App.csproj");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(new FileInfo(tempDotnetConfig).DirectoryName!);

        var csprojOriginal = await CreateTempCsprojAsync(
            tempCsproj,
            "net5.0",
            new KeyValuePair<string, string>("Microsoft.EntityFrameworkCore", "5.0.12")
        );

        var toolsOriginal = await CreateToolsConfigAsync(
            path: tempDotnetConfig,
            packageId: "dotnet-ef",
            version: "5.0.5",
            command: "dotnet"
        );

        CreateNuGetConfig(tempNuget);

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(target);

        // Assert
        await Verify(GetVerifyObjects()).UseParameters(targetPath);

        async IAsyncEnumerable<object> GetVerifyObjects()
        {
            yield return summary.UpdatedPackages;

            yield return toolsOriginal;
            yield return await File.ReadAllTextAsync(tempDotnetConfig);

            yield return csprojOriginal;
            yield return await File.ReadAllTextAsync(tempCsproj);
        }
    }

    [Theory]
    [InlineData(".config")]
    [InlineData(".config", "dotnet-tools.json")]
    public async Task Given_MultiplePackagesInDotnetTools_When_OneOutdated_Then_UpdateThatOne(
        params string[] path
    )
    {
        // Arrange
        var targetPath = Path.Combine(path);
        var temp = Path.Combine(Paths.Temporary.Root, "dfgdfg");
        var target = Path.Combine(temp, "src", targetPath);
        var tempDotnetConfig = Path.Combine(temp, "src", ".config", "dotnet-tools.json");
        var tempCsproj = Path.Combine(temp, "src", "Dummy.App.csproj");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(new FileInfo(tempDotnetConfig).DirectoryName!);

        var csprojOriginal = await CreateTempCsprojAsync(
            tempCsproj,
            "net5.0",
            new KeyValuePair<string, string>("Microsoft.EntityFrameworkCore", "5.0.12")
        );

        var toolsOriginal = await CreateToolsConfigAsync(
            path: tempDotnetConfig,
            packageId: "dotnet-ef",
            version: "5.0.5",
            command: "dotnet",
            packageId2: "Dummy.Tool",
            version2: "0.0.2",
            command2: "dummy"
        );

        CreateNuGetConfig(tempNuget);

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(target);

        // Assert
        await Verify(GetVerifyObjects()).UseParameters(targetPath);

        async IAsyncEnumerable<object> GetVerifyObjects()
        {
            yield return summary.UpdatedPackages;

            yield return toolsOriginal;
            yield return await File.ReadAllTextAsync(tempDotnetConfig);

            yield return csprojOriginal;
            yield return await File.ReadAllTextAsync(tempCsproj);
        }
    }

    [Theory]
    [InlineData("0.0.1")]
    [InlineData("0.0.2")]
    public async Task Given_UpToDate_When_UpdateFileBasedApp_Then_DoNothing(string version)
    {
        // Arrange
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(Given_UpToDate_When_UpdateFileBasedApp_Then_DoNothing)
        );
        var tempApp = Path.Combine(temp, "Build.cs");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);

        var appOriginal = await CreateTempFileBasedAppAsync(
            tempApp,
            packages: [new("Dummy", version)]
        );

        CreateNuGetConfig(tempNuget);

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(tempApp);

        // Assert
        await Verify(GetVerifyObjects()).UseParameters(version);

        async IAsyncEnumerable<object> GetVerifyObjects()
        {
            yield return summary.UpdatedPackages;
            yield return appOriginal;
            yield return await File.ReadAllTextAsync(tempApp);
        }
    }

    [Fact]
    public async Task Given_FileBasedAppAsTarget_When_PackageOutdated_Then_Update()
    {
        // Arrange
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(Given_FileBasedAppAsTarget_When_PackageOutdated_Then_Update)
        );
        var tempApp = Path.Combine(temp, "Build.cs");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);

        var appOriginal = await CreateTempFileBasedAppAsync(
            tempApp,
            packages: [new("Dummy", "0.0.1")]
        );

        CreateNuGetConfig(tempNuget);

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(tempApp);

        // Assert
        await Verify(GetVerifyObjects());

        async IAsyncEnumerable<object> GetVerifyObjects()
        {
            yield return summary.UpdatedPackages;
            yield return appOriginal;
            yield return await File.ReadAllTextAsync(tempApp);
        }
    }

    [Fact]
    public async Task Given_DirectoryAsTarget_When_SingleFileBasedApp_Then_Update()
    {
        // Arrange
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(Given_DirectoryAsTarget_When_SingleFileBasedApp_Then_Update)
        );
        var tempApp = Path.Combine(temp, "Build.cs");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);

        var appOriginal = await CreateTempFileBasedAppAsync(
            tempApp,
            packages: [new("Dummy", "0.0.1")]
        );

        CreateNuGetConfig(tempNuget);

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(temp);

        // Assert
        await Verify(GetVerifyObjects());

        async IAsyncEnumerable<object> GetVerifyObjects()
        {
            yield return summary.UpdatedPackages;
            yield return appOriginal;
            yield return await File.ReadAllTextAsync(tempApp);
        }
    }

    [Fact]
    public async Task Given_FileBasedApp_When_DryRun_Then_DoNothing()
    {
        // Arrange
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(Given_FileBasedApp_When_DryRun_Then_DoNothing)
        );
        var tempApp = Path.Combine(temp, "Build.cs");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);

        var appOriginal = await CreateTempFileBasedAppAsync(
            tempApp,
            packages: [new("Dummy", "0.0.1")]
        );

        CreateNuGetConfig(tempNuget);

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(tempApp, dryRun: true);

        // Assert
        Assert.Equal(
            appOriginal,
            await File.ReadAllTextAsync(tempApp, TestContext.Current.CancellationToken)
        );
        await Verify(summary.UpdatedPackages);
    }

    [Fact]
    public async Task Given_FileBasedAppWithoutPackageDirective_When_Create_Then_Throw()
    {
        // Arrange
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(Given_FileBasedAppWithoutPackageDirective_When_Create_Then_Throw)
        );
        var tempApp = Path.Combine(temp, "Build.cs");

        Directory.CreateDirectory(temp);
        await File.WriteAllTextAsync(
            tempApp,
            "Console.WriteLine(\"Hello, world!\");",
            TestContext.Current.CancellationToken
        );

        // Act
        var ex = Assert.Throws<ArgumentException>(() => FileBasedApp.Create(tempApp));

        // Assert
        Assert.Contains(tempApp, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Given_CsFileWithoutPackageDirective_When_CreateRootDir_Then_ThrowWithPath()
    {
        // Arrange
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(Given_CsFileWithoutPackageDirective_When_CreateRootDir_Then_ThrowWithPath)
        );
        var tempApp = Path.Combine(temp, "Build.cs");

        Directory.CreateDirectory(temp);
        File.WriteAllText(tempApp, "Console.WriteLine(\"Hello, world!\");");

        // Act
        var ex = Assert.Throws<ArgumentException>(() => RootDir.Create(tempApp));

        // Assert
        Assert.Contains(tempApp, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Given_FileBasedAppWithUnknownPackage_When_Update_Then_ReportUnknownPackage()
    {
        // Arrange
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(Given_FileBasedAppWithUnknownPackage_When_Update_Then_ReportUnknownPackage)
        );
        var tempApp = Path.Combine(temp, "Build.cs");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);

        await CreateTempFileBasedAppAsync(tempApp, packages: [new("Unknown.Package", "1.0.0")]);
        CreateNuGetConfig(tempNuget);

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(tempApp);

        // Assert
        Assert.True(summary.UnknownPackages.TryGetValue("Unknown.Package", out var projects));
        Assert.Contains("Build.cs", projects.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Given_SlnxAsTarget_When_HasCsprojDotnetToolsAndFileBasedApp_Then_UpdateAll()
    {
        // Arrange
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(Given_SlnxAsTarget_When_HasCsprojDotnetToolsAndFileBasedApp_Then_UpdateAll)
        );
        var tempSlnx = Path.Combine(temp, "Dummy.slnx");
        var tempCsproj = Path.Combine(temp, "src", "Dummy.App.csproj");
        var tempDotnetConfig = Path.Combine(temp, "tools", ".config", "dotnet-tools.json");
        var tempApp = Path.Combine(temp, "tools", "Build.cs");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(new FileInfo(tempCsproj).DirectoryName!);
        Directory.CreateDirectory(new FileInfo(tempDotnetConfig).DirectoryName!);

        var csprojOriginal = await CreateTempCsprojAsync(
            tempCsproj,
            new KeyValuePair<string, string>("Dummy", "0.0.1")
        );

        var toolsOriginal = await CreateToolsConfigAsync(
            path: tempDotnetConfig,
            packageId: "Dummy.Tool",
            version: "0.0.1",
            command: "dummy"
        );

        var appOriginal = await CreateTempFileBasedAppAsync(
            tempApp,
            packages: [new("Dummy", "0.0.1")]
        );

        await File.WriteAllTextAsync(
            tempSlnx,
            $"""
            <Solution>
              <Folder Name="/.Build/">
                <File Path="tools/Build.cs" />
                <File Path="tools/.config/dotnet-tools.json" />
              </Folder>
              <Project Path="src/Dummy.App.csproj" />
            </Solution>
            """,
            TestContext.Current.CancellationToken
        );

        CreateNuGetConfig(tempNuget);

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(tempSlnx);

        // Assert
        await Verify(GetVerifyObjects());

        async IAsyncEnumerable<object> GetVerifyObjects()
        {
            yield return summary.UpdatedPackages;

            yield return csprojOriginal;
            yield return await File.ReadAllTextAsync(tempCsproj);

            yield return toolsOriginal;
            yield return await File.ReadAllTextAsync(tempDotnetConfig);

            yield return appOriginal;
            yield return await File.ReadAllTextAsync(tempApp);
        }
    }

    [Fact]
    public async Task Given_DirectoryBuildProps_When_Update_Then_UpdatePropsFile()
    {
        // Arrange
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(Given_DirectoryBuildProps_When_Update_Then_UpdatePropsFile)
        );
        var tempCsproj = Path.Combine(temp, "src", "Dummy.App.csproj");
        var tempProps = Path.Combine(temp, "Directory.Build.props");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(Path.GetDirectoryName(tempCsproj)!);

        var csprojOriginal = await CreateTempCsprojAsync(tempCsproj);

        var propsOriginal = """
            <Project>
              <ItemGroup>
                <PackageReference Include="Dummy" Version="0.0.1" />
              </ItemGroup>
            </Project>
            """;

        await File.WriteAllTextAsync(
            tempProps,
            propsOriginal,
            TestContext.Current.CancellationToken
        );

        CreateNuGetConfig(tempNuget);

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(temp);

        // Assert
        await Verify(GetVerifyObjects());

        async IAsyncEnumerable<object> GetVerifyObjects()
        {
            yield return summary.UpdatedPackages;

            yield return csprojOriginal;
            yield return await File.ReadAllTextAsync(tempCsproj);

            yield return propsOriginal;
            yield return await File.ReadAllTextAsync(tempProps);
        }
    }

    [Fact]
    public async Task Given_DirectoryPackagesProps_When_Update_Then_UpdatePackageVersion()
    {
        // Arrange - central package management: versions live in Directory.Packages.props as
        // PackageVersion items, while the csproj itself only has version-less PackageReference
        // items.
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(Given_DirectoryPackagesProps_When_Update_Then_UpdatePackageVersion)
        );
        var tempCsproj = Path.Combine(temp, "src", "Dummy.App.csproj");
        var tempProps = Path.Combine(temp, "Directory.Packages.props");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(Path.GetDirectoryName(tempCsproj)!);

        var csprojOriginal = await CreateTempCsprojAsync(tempCsproj);

        await File.WriteAllTextAsync(
            tempCsproj,
            (
                await File.ReadAllTextAsync(tempCsproj, TestContext.Current.CancellationToken)
            ).Replace(
                "<ItemGroup></ItemGroup>",
                """<ItemGroup><PackageReference Include="Dummy" /></ItemGroup>"""
            ),
            TestContext.Current.CancellationToken
        );

        csprojOriginal = await File.ReadAllTextAsync(
            tempCsproj,
            TestContext.Current.CancellationToken
        );

        var propsOriginal = """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
              </PropertyGroup>
              <ItemGroup>
                <PackageVersion Include="Dummy" Version="0.0.1" />
              </ItemGroup>
            </Project>
            """;

        await File.WriteAllTextAsync(
            tempProps,
            propsOriginal,
            TestContext.Current.CancellationToken
        );

        CreateNuGetConfig(tempNuget);

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(temp);

        // Assert
        await Verify(GetVerifyObjects());

        async IAsyncEnumerable<object> GetVerifyObjects()
        {
            yield return summary.UpdatedPackages;

            yield return csprojOriginal;
            yield return await File.ReadAllTextAsync(tempCsproj);

            yield return propsOriginal;
            yield return await File.ReadAllTextAsync(tempProps);
        }
    }

    [Fact]
    public async Task Given_DirectoryBuildPropsSharedByMultipleCsproj_When_Update_Then_UpdateOnce()
    {
        // Arrange - a single Directory.Build.props imported by two csproj must only be discovered
        // and updated once, not once per importing csproj.
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(Given_DirectoryBuildPropsSharedByMultipleCsproj_When_Update_Then_UpdateOnce)
        );
        var tempCsprojOne = Path.Combine(temp, "src", "One", "Dummy.App.csproj");
        var tempCsprojTwo = Path.Combine(temp, "src", "Two", "Dummy.App.csproj");
        var tempProps = Path.Combine(temp, "Directory.Build.props");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(Path.GetDirectoryName(tempCsprojOne)!);
        Directory.CreateDirectory(Path.GetDirectoryName(tempCsprojTwo)!);

        await CreateTempCsprojAsync(tempCsprojOne);
        await CreateTempCsprojAsync(tempCsprojTwo);

        var propsOriginal = """
            <Project>
              <ItemGroup>
                <PackageReference Include="Dummy" Version="0.0.1" />
              </ItemGroup>
            </Project>
            """;

        await File.WriteAllTextAsync(
            tempProps,
            propsOriginal,
            TestContext.Current.CancellationToken
        );

        CreateNuGetConfig(tempNuget);

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(temp);

        // Assert
        var updatedPackage = Assert.Single(summary.UpdatedPackages);
        var update1 = Assert.Single(updatedPackage.Updates);

        Assert.Equal("Dummy", updatedPackage.PackageId);
        Assert.Contains("Directory.Build.props", update1.Project, StringComparison.Ordinal);

        var content = await File.ReadAllTextAsync(tempProps, TestContext.Current.CancellationToken);

        Assert.Contains("0.0.2", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Given_DirectoryBuildPropsInheritanceChain_When_Update_Then_UpdateBothLevels()
    {
        // Arrange - src/Directory.Build.props explicitly imports the root Directory.Build.props
        // (the .NET SDK only auto-imports the *nearest* Directory.Build.props for a project; a
        // multi-level chain requires each level to explicitly import the next one up). Both
        // files declare a different package, so we can confirm both are discovered and updated
        // through the inheritance chain, no matter how many levels deep.
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(Given_DirectoryBuildPropsInheritanceChain_When_Update_Then_UpdateBothLevels)
        );
        var tempCsproj = Path.Combine(temp, "src", "Dummy.App.csproj");
        var tempRootProps = Path.Combine(temp, "Directory.Build.props");
        var tempSrcProps = Path.Combine(temp, "src", "Directory.Build.props");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(Path.GetDirectoryName(tempCsproj)!);

        await CreateTempCsprojAsync(tempCsproj);

        var rootPropsOriginal = """
            <Project>
              <ItemGroup>
                <PackageReference Include="Dummy" Version="0.0.1" />
              </ItemGroup>
            </Project>
            """;

        var srcPropsOriginal = """
            <Project>
              <Import Project="$(MSBuildThisFileDirectory)..\Directory.Build.props" />
              <ItemGroup>
                <PackageReference Include="Dummy.Tool" Version="0.0.1" />
              </ItemGroup>
            </Project>
            """;

        await File.WriteAllTextAsync(
            tempRootProps,
            rootPropsOriginal,
            TestContext.Current.CancellationToken
        );
        await File.WriteAllTextAsync(
            tempSrcProps,
            srcPropsOriginal,
            TestContext.Current.CancellationToken
        );

        CreateNuGetConfig(tempNuget);

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(temp);

        // Assert
        Assert.Equal(2, summary.UpdatedPackages.Count());
        Assert.Contains(summary.UpdatedPackages, x => x.PackageId == "Dummy");
        Assert.Contains(summary.UpdatedPackages, x => x.PackageId == "Dummy.Tool");

        var rootContent = await File.ReadAllTextAsync(
            tempRootProps,
            TestContext.Current.CancellationToken
        );
        var srcContent = await File.ReadAllTextAsync(
            tempSrcProps,
            TestContext.Current.CancellationToken
        );

        Assert.Contains("Version=\"0.0.2\"", rootContent, StringComparison.Ordinal);
        Assert.Contains("Version=\"0.0.2\"", srcContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Given_NestedDirectoryBuildPropsWithoutExplicitImport_When_Update_Then_OuterPropsFileIsIgnored()
    {
        // Arrange - the .NET SDK stops at the *nearest* Directory.Build.props for a project; it
        // does not automatically keep walking further up unless that file explicitly imports the
        // next one. src/Directory.Build.props here does NOT import the root one, so the root
        // file's package reference must not be discovered/updated for this project - otherwise
        // the wrong (possibly TFM-incompatible) file could be picked up.
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(
                Given_NestedDirectoryBuildPropsWithoutExplicitImport_When_Update_Then_OuterPropsFileIsIgnored
            )
        );
        var tempCsproj = Path.Combine(temp, "src", "Dummy.App.csproj");
        var tempRootProps = Path.Combine(temp, "Directory.Build.props");
        var tempSrcProps = Path.Combine(temp, "src", "Directory.Build.props");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(Path.GetDirectoryName(tempCsproj)!);

        await CreateTempCsprojAsync(tempCsproj);

        var rootPropsOriginal = """
            <Project>
              <ItemGroup>
                <PackageReference Include="Dummy" Version="0.0.1" />
              </ItemGroup>
            </Project>
            """;

        var srcPropsOriginal = """
            <Project>
              <ItemGroup>
                <PackageReference Include="Dummy.Tool" Version="0.0.1" />
              </ItemGroup>
            </Project>
            """;

        await File.WriteAllTextAsync(
            tempRootProps,
            rootPropsOriginal,
            TestContext.Current.CancellationToken
        );
        await File.WriteAllTextAsync(
            tempSrcProps,
            srcPropsOriginal,
            TestContext.Current.CancellationToken
        );

        CreateNuGetConfig(tempNuget);

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(temp);

        // Assert - only Dummy.Tool (from src/Directory.Build.props) is updated, the root
        // Directory.Build.props (never imported by this project) is left untouched.
        var updatedPackage = Assert.Single(summary.UpdatedPackages);

        Assert.Equal("Dummy.Tool", updatedPackage.PackageId);

        var rootContent = await File.ReadAllTextAsync(
            tempRootProps,
            TestContext.Current.CancellationToken
        );
        var srcContent = await File.ReadAllTextAsync(
            tempSrcProps,
            TestContext.Current.CancellationToken
        );

        Assert.Contains("Version=\"0.0.1\"", rootContent, StringComparison.Ordinal);
        Assert.Contains("Version=\"0.0.2\"", srcContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Given_DirectoryBuildPropsSharedByProjectsWithDifferentTfm_When_Update_Then_UpdateToVersionSupportedByLowerTfm()
    {
        // Arrange - Has.Newer.Tfm 3.1.0 targets netcoreapp3.1, 5.0.0 targets net5.0 and 6.0.0
        // targets net6.0 only (not usable from a net5.0 project). A root Directory.Build.props
        // shared by a net5.0 and a net6.0 project must only be updated to 5.0.0 - the newest
        // version still usable by both - not 6.0.0, which would break the net5.0 project.
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(
                Given_DirectoryBuildPropsSharedByProjectsWithDifferentTfm_When_Update_Then_UpdateToVersionSupportedByLowerTfm
            )
        );
        var tempCsprojNet5 = Path.Combine(temp, "src", "Net5", "Dummy.App.csproj");
        var tempCsprojNet6 = Path.Combine(temp, "src", "Net6", "Dummy.App.csproj");
        var tempProps = Path.Combine(temp, "Directory.Build.props");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(Path.GetDirectoryName(tempCsprojNet5)!);
        Directory.CreateDirectory(Path.GetDirectoryName(tempCsprojNet6)!);

        await CreateTempCsprojAsync(tempCsprojNet5, "net5.0");
        await CreateTempCsprojAsync(tempCsprojNet6, "net6.0");

        var propsOriginal = """
            <Project>
              <ItemGroup>
                <PackageReference Include="Has.Newer.Tfm" Version="3.1.0" />
              </ItemGroup>
            </Project>
            """;

        await File.WriteAllTextAsync(
            tempProps,
            propsOriginal,
            TestContext.Current.CancellationToken
        );

        CreateNuGetConfig(tempNuget);

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(temp);

        // Assert
        var updatedPackage = Assert.Single(summary.UpdatedPackages);
        var updated = Assert.Single(updatedPackage.Updates);

        Assert.Equal("Has.Newer.Tfm", updatedPackage.PackageId);
        Assert.Equal("3.1.0", updated.From.ToString());
        Assert.Equal("5.0.0", updated.To.ToString());

        var content = await File.ReadAllTextAsync(tempProps, TestContext.Current.CancellationToken);

        Assert.Contains("Version=\"5.0.0\"", content, StringComparison.Ordinal);
    }
}
