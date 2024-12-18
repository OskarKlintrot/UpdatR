﻿using System;
using System.Diagnostics.CodeAnalysis;
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
        Assert.Equal(slnOriginal, await File.ReadAllTextAsync(tempSln));
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
        Assert.Equal(slnOriginal, await File.ReadAllTextAsync(tempSln));
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
        Assert.Equal(slnOriginal, await File.ReadAllTextAsync(tempSln));
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
        Assert.Equal(slnOriginal, await File.ReadAllTextAsync(tempSln));
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
}
