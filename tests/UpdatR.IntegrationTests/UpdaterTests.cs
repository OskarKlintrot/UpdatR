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
        var summary = await update.UpdateAsync(
            tempCsproj,
            cancellationToken: TestContext.Current.CancellationToken
        );

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
        var summary = await update.UpdateAsync(
            tempCsproj,
            cancellationToken: TestContext.Current.CancellationToken
        );

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
        var summary = await update.UpdateAsync(
            tempCsproj,
            new UpdateOptions { TargetFrameworkMoniker = tfm },
            cancellationToken: TestContext.Current.CancellationToken
        );

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
        var summary = await update.UpdateAsync(
            tempDotnetConfig,
            cancellationToken: TestContext.Current.CancellationToken
        );

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
        var summary = await update.UpdateAsync(
            tempDotnetConfig,
            cancellationToken: TestContext.Current.CancellationToken
        );

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
        var summary = await update.UpdateAsync(
            tempDotnetConfig,
            cancellationToken: TestContext.Current.CancellationToken
        );

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
        var summary = await update.UpdateAsync(
            target,
            new UpdateOptions { DryRun = true },
            cancellationToken: TestContext.Current.CancellationToken
        );

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
        var summary = await update.UpdateAsync(
            target,
            cancellationToken: TestContext.Current.CancellationToken
        );

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
        var summary = await update.UpdateAsync(
            temp,
            new UpdateOptions { Packages = packages },
            cancellationToken: TestContext.Current.CancellationToken
        );

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
        var summary = await update.UpdateAsync(
            temp,
            new UpdateOptions { ExcludePackages = excludedPackages },
            cancellationToken: TestContext.Current.CancellationToken
        );

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
        var summary = await update.UpdateAsync(
            temp,
            cancellationToken: TestContext.Current.CancellationToken
        );

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
        var summary = await update.UpdateAsync(
            temp,
            new UpdateOptions { ExcludePackages = ["Has.*"] },
            cancellationToken: TestContext.Current.CancellationToken
        );

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
    public async Task Given_UpdatRRcFileWithExcludeFiles_When_Update_Then_ExcludedFileIsUntouched()
    {
        // Arrange
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(Given_UpdatRRcFileWithExcludeFiles_When_Update_Then_ExcludedFileIsUntouched)
        );
        var tempCsproj = Path.Combine(temp, "src", "Dummy.App.csproj");
        var excludedCsproj = Path.Combine(
            temp,
            "tests",
            "Resources",
            "Templates",
            "Dummy.App.csproj"
        );
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(Path.GetDirectoryName(tempCsproj)!);
        Directory.CreateDirectory(Path.GetDirectoryName(excludedCsproj)!);

        var csprojOriginal = await CreateTempCsprojAsync(
            tempCsproj,
            new KeyValuePair<string, string>("Dummy.Tool", "0.0.1")
        );

        var excludedCsprojOriginal = await CreateTempCsprojAsync(
            excludedCsproj,
            new KeyValuePair<string, string>("Dummy.Tool", "0.0.1")
        );

        CreateNuGetConfig(tempNuget);

        await CreateUpdatRConfigAsync(
            Path.Combine(temp, ".updatrrc"),
            """{ "excludeFiles": ["tests/**"] }"""
        );

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(
            temp,
            cancellationToken: TestContext.Current.CancellationToken
        );

        // Assert
        await Verify(GetVerifyObjects());

        async IAsyncEnumerable<object> GetVerifyObjects()
        {
            yield return summary.UpdatedPackages;

            yield return csprojOriginal;
            yield return await File.ReadAllTextAsync(tempCsproj);

            yield return excludedCsprojOriginal;
            yield return await File.ReadAllTextAsync(excludedCsproj);
        }
    }

    [Fact]
    public async Task Given_UpdatRRcFileWithPath_When_PathIsCurrentDirectory_Then_UseConfigPath()
    {
        // Arrange
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(Given_UpdatRRcFileWithPath_When_PathIsCurrentDirectory_Then_UseConfigPath)
        );
        var tempCsproj = Path.Combine(temp, "src", "Dummy.App.csproj");
        var otherCsproj = Path.Combine(temp, "tests", "Dummy.App.csproj");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(Path.GetDirectoryName(tempCsproj)!);
        Directory.CreateDirectory(Path.GetDirectoryName(otherCsproj)!);

        var csprojOriginal = await CreateTempCsprojAsync(
            tempCsproj,
            new KeyValuePair<string, string>("Dummy.Tool", "0.0.1")
        );

        var otherCsprojOriginal = await CreateTempCsprojAsync(
            otherCsproj,
            new KeyValuePair<string, string>("Dummy.Tool", "0.0.1")
        );

        CreateNuGetConfig(tempNuget);

        await CreateUpdatRConfigAsync(
            Path.Combine(temp, ".updatrrc"),
            """{ "path": "src/Dummy.App.csproj" }"""
        );

        var update = new Updater();
        var originalCurrentDirectory = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(temp);

            // Act
            var summary = await update.UpdateAsync(
                cancellationToken: TestContext.Current.CancellationToken
            );

            // Assert
            await Verify(GetVerifyObjects());

            async IAsyncEnumerable<object> GetVerifyObjects()
            {
                yield return summary.UpdatedPackages;

                yield return csprojOriginal;
                yield return await File.ReadAllTextAsync(tempCsproj);

                yield return otherCsprojOriginal;
                yield return await File.ReadAllTextAsync(otherCsproj);
            }
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCurrentDirectory);
        }
    }

    [Fact]
    public async Task Given_UpdatRRcFileWithNonExistentPath_When_PathIsCurrentDirectory_Then_ThrowWithClearMessage()
    {
        // Arrange
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(
                Given_UpdatRRcFileWithNonExistentPath_When_PathIsCurrentDirectory_Then_ThrowWithClearMessage
            )
        );

        Directory.CreateDirectory(temp);

        await CreateUpdatRConfigAsync(
            Path.Combine(temp, ".updatrrc"),
            """{ "path": "does-not-exist" }"""
        );

        var update = new Updater();
        var originalCurrentDirectory = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(temp);

            // Act
            var exception = await Assert.ThrowsAsync<InvalidUpdateTargetException>(() =>
                update.UpdateAsync(cancellationToken: TestContext.Current.CancellationToken)
            );

            // Assert
            Assert.Contains("path", exception.Message);
            Assert.Contains(".updatrrc", exception.Message);
            Assert.Contains("does not exist", exception.Message);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCurrentDirectory);
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
            new UpdateOptions
            {
                ExcludePackages = excludedPackages is null ? null : [excludedPackages],
                Packages = [packages],
            },
            cancellationToken: TestContext.Current.CancellationToken
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
        var summary = await update.UpdateAsync(
            Path.Combine(temp, target),
            cancellationToken: TestContext.Current.CancellationToken
        );

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
        var summary = await update.UpdateAsync(
            temp,
            cancellationToken: TestContext.Current.CancellationToken
        );

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
        var summary = await update.UpdateAsync(
            temp,
            cancellationToken: TestContext.Current.CancellationToken
        );

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
    public async Task Given_PackageSourceMappingExcludesSource_When_Update_Then_DoNotUpdate()
    {
        // Arrange - the "local" source (the only one with a newer version of "Dummy") is mapped
        // to a pattern that doesn't match "Dummy", so the package must be left untouched even
        // though a newer version exists on disk.
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(Given_PackageSourceMappingExcludesSource_When_Update_Then_DoNotUpdate)
        );
        var tempCsproj = Path.Combine(temp, "Dummy.App.csproj");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);

        var csprojOriginal = await CreateTempCsprojAsync(
            tempCsproj,
            new KeyValuePair<string, string>("Dummy", "0.0.1")
        );

        CreateNuGetConfig(
            tempNuget,
            packageSourceMappingPatternsBySource: new Dictionary<string, string[]>
            {
                ["local"] = ["DoesNotMatch.*"],
            }
        );

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(
            tempCsproj,
            cancellationToken: TestContext.Current.CancellationToken
        );

        // Assert
        Assert.Empty(summary.UpdatedPackages);
        Assert.Equal(
            csprojOriginal,
            await File.ReadAllTextAsync(tempCsproj, TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task Given_PackageSourceMappingIncludesSource_When_Update_Then_Update()
    {
        // Arrange - same setup as
        // Given_PackageSourceMappingExcludesSource_When_Update_Then_DoNotUpdate, but the pattern
        // does match "Dummy", so the update should proceed as if mapping wasn't configured at
        // all.
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(Given_PackageSourceMappingIncludesSource_When_Update_Then_Update)
        );
        var tempCsproj = Path.Combine(temp, "Dummy.App.csproj");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);

        await CreateTempCsprojAsync(tempCsproj, new KeyValuePair<string, string>("Dummy", "0.0.1"));

        CreateNuGetConfig(
            tempNuget,
            packageSourceMappingPatternsBySource: new Dictionary<string, string[]>
            {
                ["local"] = ["Dummy*"],
            }
        );

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(
            tempCsproj,
            cancellationToken: TestContext.Current.CancellationToken
        );

        // Assert
        Assert.NotEmpty(summary.UpdatedPackages);
    }

    [Fact]
    public async Task Given_UpdatedPackage_When_FailOnOutdated_Then_ShouldFail()
    {
        // Arrange
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(Given_UpdatedPackage_When_FailOnOutdated_Then_ShouldFail)
        );
        var tempCsproj = Path.Combine(temp, "Dummy.App.csproj");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);

        await CreateTempCsprojAsync(tempCsproj, new KeyValuePair<string, string>("Dummy", "0.0.1"));

        CreateNuGetConfig(tempNuget);

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(
            tempCsproj,
            new UpdateOptions { FailOn = FailOn.Outdated },
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.NotEmpty(summary.UpdatedPackages);
        Assert.True(summary.ShouldFail);
    }

    [Fact]
    public async Task Given_UpdatedPackage_When_FailOnVulnerable_Then_ShouldNotFail()
    {
        // Arrange - an update happened, but nothing is deprecated or vulnerable, so
        // FailOn.Vulnerable (and FailOn.Deprecated) should not trigger.
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(Given_UpdatedPackage_When_FailOnVulnerable_Then_ShouldNotFail)
        );
        var tempCsproj = Path.Combine(temp, "Dummy.App.csproj");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);

        await CreateTempCsprojAsync(tempCsproj, new KeyValuePair<string, string>("Dummy", "0.0.1"));

        CreateNuGetConfig(tempNuget);

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(
            tempCsproj,
            new UpdateOptions { FailOn = FailOn.Vulnerable },
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.NotEmpty(summary.UpdatedPackages);
        Assert.False(summary.ShouldFail);
    }

    [Theory]
    [InlineData(FailOn.Outdated)]
    [InlineData(null)]
    public async Task Given_UpToDate_When_FailOnOutdated_Then_ShouldNotFail(FailOn? failOn)
    {
        // Arrange - default FailOn.None never fails (null case); an up-to-date package doesn't
        // trip FailOn.Outdated either.
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(Given_UpToDate_When_FailOnOutdated_Then_ShouldNotFail),
            failOn?.ToString() ?? "none"
        );
        var tempCsproj = Path.Combine(temp, "Dummy.App.csproj");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);

        await CreateTempCsprojAsync(tempCsproj, new KeyValuePair<string, string>("Dummy", "0.0.2"));

        CreateNuGetConfig(tempNuget);

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(
            tempCsproj,
            new UpdateOptions { FailOn = failOn },
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.Empty(summary.UpdatedPackages);
        Assert.False(summary.ShouldFail);
    }

    [Fact]
    public async Task Given_UpdatRRcFileWithFailOn_When_Update_Then_UsesConfigValue()
    {
        // Arrange - FailOn isn't given via UpdateOptions, so the .updatrrc value is used.
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(Given_UpdatRRcFileWithFailOn_When_Update_Then_UsesConfigValue)
        );
        var tempCsproj = Path.Combine(temp, "Dummy.App.csproj");
        var tempNuget = Path.Combine(temp, "nuget.config");
        var tempUpdatRRc = Path.Combine(temp, UpdatRConfig.FileName);

        Directory.CreateDirectory(temp);

        await CreateTempCsprojAsync(tempCsproj, new KeyValuePair<string, string>("Dummy", "0.0.1"));

        CreateNuGetConfig(tempNuget);

        await CreateUpdatRConfigAsync(tempUpdatRRc, """{ "failOn": "outdated" }""");

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(
            tempCsproj,
            cancellationToken: TestContext.Current.CancellationToken
        );

        // Assert
        Assert.NotEmpty(summary.UpdatedPackages);
        Assert.Equal(FailOn.Outdated, summary.FailOn);
        Assert.True(summary.ShouldFail);
    }

    [Fact]
    public async Task Given_UpdatRRcFileWithPackagePolicies_When_Update_Then_CapsToConfigValue()
    {
        // Arrange - PackagePolicies isn't given via UpdateOptions, so the .updatrrc value is
        // used: it caps Has.Newer.Tfm at major 5, so the net5.0 project updates to 5.0.0 (not
        // 6.0.0) and the skip is reported for 6.0.0 with reason PackageVersionPolicy.
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(Given_UpdatRRcFileWithPackagePolicies_When_Update_Then_CapsToConfigValue)
        );
        var tempCsproj = Path.Combine(temp, "Dummy.App.csproj");
        var tempNuget = Path.Combine(temp, "nuget.config");
        var tempUpdatRRc = Path.Combine(temp, UpdatRConfig.FileName);

        Directory.CreateDirectory(temp);

        await CreateTempCsprojAsync(
            tempCsproj,
            "net5.0",
            new KeyValuePair<string, string>("Has.Newer.Tfm", "3.1.0")
        );

        CreateNuGetConfig(tempNuget);

        await CreateUpdatRConfigAsync(
            tempUpdatRRc,
            """{ "packagePolicies": [{ "package": "Has.Newer.Tfm", "maxMajor": 5 }] }"""
        );

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(
            tempCsproj,
            cancellationToken: TestContext.Current.CancellationToken
        );

        // Assert
        var updatedPackage = Assert.Single(summary.UpdatedPackages);
        var updated = Assert.Single(updatedPackage.Updates);

        Assert.Equal("5.0.0", updated.To.ToString());

        var skippedPackage = Assert.Single(summary.SkippedUpdatePackages);
        var skippedVersion = Assert.Single(skippedPackage.Versions);

        Assert.Equal("Has.Newer.Tfm", skippedPackage.PackageId);
        Assert.Equal("6.0.0", skippedVersion.Version.NuGetVersion.ToString());
        Assert.Equal(SkippedUpdateReason.PackageVersionPolicy, skippedVersion.Version.Reason);
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
        var summary = await update.UpdateAsync(
            tempCsproj,
            cancellationToken: TestContext.Current.CancellationToken
        );

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
    public async Task Given_LatestPackageHasUnsupportedTfm_When_Update_Then_ReportSkippedUpdate()
    {
        // Arrange - same fixture as Given_LatestPackageHasUnsupportedTfm_When_Update_Then_PickLatestSupportedTfm:
        // Has.Newer.Tfm 6.0.0 only targets net6.0, so a net5.0 project updates to 5.0.0 instead,
        // but 6.0.0 existing (and being TFM-incompatible) should now be visible in the summary.
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(Given_LatestPackageHasUnsupportedTfm_When_Update_Then_ReportSkippedUpdate)
        );
        var tempCsproj = Path.Combine(temp, "Dummy.App.csproj");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);

        await CreateTempCsprojAsync(
            tempCsproj,
            "net5.0",
            new KeyValuePair<string, string>("Has.Newer.Tfm", "3.1.0")
        );

        CreateNuGetConfig(tempNuget);

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(
            tempCsproj,
            cancellationToken: TestContext.Current.CancellationToken
        );

        // Assert
        var updatedPackage = Assert.Single(summary.UpdatedPackages);
        var updated = Assert.Single(updatedPackage.Updates);

        Assert.Equal("5.0.0", updated.To.ToString());

        var skippedPackage = Assert.Single(summary.SkippedUpdatePackages);
        var skippedVersion = Assert.Single(skippedPackage.Versions);

        Assert.Equal("Has.Newer.Tfm", skippedPackage.PackageId);
        Assert.Equal("6.0.0", skippedVersion.Version.NuGetVersion.ToString());
        Assert.Equal(
            SkippedUpdateReason.IncompatibleTargetFramework,
            skippedVersion.Version.Reason
        );
        Assert.Equal("Dummy.App.csproj", Assert.Single(skippedVersion.Projects));
    }

    [Fact]
    public async Task Given_AlignWithTfmCapsBelowIncompatibleVersion_When_Update_Then_ReportSkippedUpdateAsAlignedWithTfm()
    {
        // Arrange - Has.Newer.Tfm 6.0.0 is the absolute latest, but it's both incompatible with
        // net5.0 *and* has a major (6) beyond what alignWithTfm allows for a net5.0 project (5).
        // Since alignWithTfm is what's configured here, the skip should be attributed to it.
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(
                Given_AlignWithTfmCapsBelowIncompatibleVersion_When_Update_Then_ReportSkippedUpdateAsAlignedWithTfm
            )
        );
        var tempCsproj = Path.Combine(temp, "Dummy.App.csproj");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);

        await CreateTempCsprojAsync(
            tempCsproj,
            "net5.0",
            new KeyValuePair<string, string>("Has.Newer.Tfm", "3.1.0")
        );

        CreateNuGetConfig(tempNuget);

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(
            tempCsproj,
            new UpdateOptions { AlignWithTfm = ["Has.Newer.Tfm"] },
            cancellationToken: TestContext.Current.CancellationToken
        );

        // Assert
        var updatedPackage = Assert.Single(summary.UpdatedPackages);
        var updated = Assert.Single(updatedPackage.Updates);

        Assert.Equal("5.0.0", updated.To.ToString());

        var skippedPackage = Assert.Single(summary.SkippedUpdatePackages);
        var skippedVersion = Assert.Single(skippedPackage.Versions);

        Assert.Equal("Has.Newer.Tfm", skippedPackage.PackageId);
        Assert.Equal("6.0.0", skippedVersion.Version.NuGetVersion.ToString());
        Assert.Equal(SkippedUpdateReason.AlignedWithTfm, skippedVersion.Version.Reason);
    }

    [Fact]
    public async Task Given_PackagePolicyCapsBelowAvailableUpdate_When_Update_Then_UpdateToCapAndReportSkippedUpdate()
    {
        // Arrange - Has.Newer.Tfm 5.0.0 and 6.0.0 are both compatible with net5.0 (6.0.0 only
        // targets net6.0 so it's excluded by TFM anyway), but a PackageVersionPolicy capping the
        // package at maxMajor 5 should still let the 5.0.0 update through while reporting 6.0.0
        // as skipped for the new PackageVersionPolicy reason (not AlignedWithTfm, since
        // alignWithTfm isn't configured here).
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(
                Given_PackagePolicyCapsBelowAvailableUpdate_When_Update_Then_UpdateToCapAndReportSkippedUpdate
            )
        );
        var tempCsproj = Path.Combine(temp, "Dummy.App.csproj");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);

        await CreateTempCsprojAsync(
            tempCsproj,
            "net5.0",
            new KeyValuePair<string, string>("Has.Newer.Tfm", "3.1.0")
        );

        CreateNuGetConfig(tempNuget);

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(
            tempCsproj,
            new UpdateOptions { PackagePolicies = [new PackageVersionPolicy("Has.Newer.Tfm", 5)] },
            cancellationToken: TestContext.Current.CancellationToken
        );

        // Assert
        var updatedPackage = Assert.Single(summary.UpdatedPackages);
        var updated = Assert.Single(updatedPackage.Updates);

        Assert.Equal("5.0.0", updated.To.ToString());

        var skippedPackage = Assert.Single(summary.SkippedUpdatePackages);
        var skippedVersion = Assert.Single(skippedPackage.Versions);

        Assert.Equal("Has.Newer.Tfm", skippedPackage.PackageId);
        Assert.Equal("6.0.0", skippedVersion.Version.NuGetVersion.ToString());
        Assert.Equal(SkippedUpdateReason.PackageVersionPolicy, skippedVersion.Version.Reason);
    }

    [Fact]
    public async Task Given_PackagePolicyAndAlignWithTfmBothApply_When_Update_Then_MoreRestrictiveCapWins()
    {
        // Arrange - alignWithTfm allows up to major 5 for this net5.0 project, but a
        // PackageVersionPolicy further restricts the same package to major 3. The more
        // restrictive of the two (3) must win, so no update happens at all (the installed 3.1.0
        // is already at the cap), and the skip for 6.0.0 is attributed to PackageVersionPolicy
        // since that's the cap actually exceeded.
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(
                Given_PackagePolicyAndAlignWithTfmBothApply_When_Update_Then_MoreRestrictiveCapWins
            )
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
        var summary = await update.UpdateAsync(
            tempCsproj,
            new UpdateOptions
            {
                AlignWithTfm = ["Has.Newer.Tfm"],
                PackagePolicies = [new PackageVersionPolicy("Has.Newer.Tfm", 3)],
            },
            cancellationToken: TestContext.Current.CancellationToken
        );

        // Assert
        Assert.Empty(summary.UpdatedPackages);
        Assert.Equal(
            csprojOriginal,
            await File.ReadAllTextAsync(tempCsproj, TestContext.Current.CancellationToken)
        );

        var skippedPackage = Assert.Single(summary.SkippedUpdatePackages);
        var skippedVersion = Assert.Single(skippedPackage.Versions);

        Assert.Equal("Has.Newer.Tfm", skippedPackage.PackageId);
        Assert.Equal("6.0.0", skippedVersion.Version.NuGetVersion.ToString());
        Assert.Equal(SkippedUpdateReason.PackageVersionPolicy, skippedVersion.Version.Reason);
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
        var summary = await update.UpdateAsync(
            target,
            cancellationToken: TestContext.Current.CancellationToken
        );

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
        var summary = await update.UpdateAsync(
            target,
            cancellationToken: TestContext.Current.CancellationToken
        );

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

    [Fact]
    public async Task Given_OutdatedDotnetEf_When_UnrelatedSiblingCsprojIsPinned_Then_OnlyAffectedByCsprojsInScope()
    {
        // Arrange
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(
                Given_OutdatedDotnetEf_When_UnrelatedSiblingCsprojIsPinned_Then_OnlyAffectedByCsprojsInScope
            )
        );
        var tempDotnetConfig = Path.Combine(
            temp,
            "src",
            "ProjectA",
            ".config",
            "dotnet-tools.json"
        );
        var tempCsprojA = Path.Combine(temp, "src", "ProjectA", "A.csproj");
        var tempCsprojB = Path.Combine(temp, "src", "ProjectB", "B.csproj");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(new FileInfo(tempDotnetConfig).DirectoryName!);
        Directory.CreateDirectory(new FileInfo(tempCsprojB).DirectoryName!);

        var csprojAOriginal = await CreateTempCsprojAsync(
            tempCsprojA,
            "net5.0",
            new KeyValuePair<string, string>("Microsoft.EntityFrameworkCore", "5.0.12")
        );

        // ProjectB is an unrelated sibling project (not in ProjectA's scope) whose EF Core
        // version is pinned and can never update. It should not affect ProjectA's own
        // dotnet-tools.json, which is only scoped to ProjectA.
        var csprojBOriginal = await CreateTempCsprojAsync(
            tempCsprojB,
            "net5.0",
            new KeyValuePair<string, string>("Microsoft.EntityFrameworkCore", "[5.0.12]")
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
        var summary = await update.UpdateAsync(
            Path.Combine(temp, "src"),
            cancellationToken: TestContext.Current.CancellationToken
        );

        // Assert
        await Verify(GetVerifyObjects());

        async IAsyncEnumerable<object> GetVerifyObjects()
        {
            yield return summary.UpdatedPackages;

            yield return toolsOriginal;
            yield return await File.ReadAllTextAsync(tempDotnetConfig);

            yield return csprojAOriginal;
            yield return await File.ReadAllTextAsync(tempCsprojA);

            yield return csprojBOriginal;
            yield return await File.ReadAllTextAsync(tempCsprojB);
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
        var summary = await update.UpdateAsync(
            target,
            cancellationToken: TestContext.Current.CancellationToken
        );

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

    [Fact]
    public async Task Given_DotnetEfAlreadyAtPinnedVersion_When_Update_Then_DoNotReportAnUpdate()
    {
        // Arrange
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(Given_DotnetEfAlreadyAtPinnedVersion_When_Update_Then_DoNotReportAnUpdate)
        );
        var target = Path.Combine(temp, "src", ".config");
        var tempDotnetConfig = Path.Combine(temp, "src", ".config", "dotnet-tools.json");
        var tempCsproj = Path.Combine(temp, "src", "Dummy.App.csproj");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(new FileInfo(tempDotnetConfig).DirectoryName!);

        // The feed's newest dotnet-ef is 5.0.17, but the project pins it to 5.0.16 - which is
        // exactly where the tool already is. There is nothing to do.
        await CreateTempCsprojAsync(
            tempCsproj,
            "net5.0",
            new KeyValuePair<string, string>("Microsoft.EntityFrameworkCore", "5.0.16")
        );

        var toolsOriginal = await CreateToolsConfigAsync(
            path: tempDotnetConfig,
            packageId: "dotnet-ef",
            version: "5.0.16",
            command: "dotnet"
        );

        CreateNuGetConfig(tempNuget);

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(
            target,
            new UpdateOptions { FailOn = FailOn.Outdated },
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.Empty(
            summary.UpdatedPackages.SelectMany(x =>
                x.Updates.Select(u => $"{x.PackageId} {u.From}->{u.To} in {u.Project}")
            )
        );
        Assert.Equal(
            toolsOriginal,
            await File.ReadAllTextAsync(tempDotnetConfig, TestContext.Current.CancellationToken)
        );
        Assert.False(summary.ShouldFail);
    }

    [Fact]
    public async Task Given_DotnetEfAheadOfPinnedVersion_When_Update_Then_DoNotDowngrade()
    {
        // Arrange
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(Given_DotnetEfAheadOfPinnedVersion_When_Update_Then_DoNotDowngrade)
        );
        var target = Path.Combine(temp, "src", ".config");
        var tempDotnetConfig = Path.Combine(temp, "src", ".config", "dotnet-tools.json");
        var tempCsproj = Path.Combine(temp, "src", "Dummy.App.csproj");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(new FileInfo(tempDotnetConfig).DirectoryName!);

        // Microsoft.EntityFrameworkCore is held at 5.0.12 while the tool is already at 5.0.16 and
        // the feed's newest dotnet-ef is 5.0.17. The pin must not drag the tool back to 5.0.12.
        await CreateTempCsprojAsync(
            tempCsproj,
            "net5.0",
            new KeyValuePair<string, string>("Microsoft.EntityFrameworkCore", "5.0.12")
        );

        var toolsOriginal = await CreateToolsConfigAsync(
            path: tempDotnetConfig,
            packageId: "dotnet-ef",
            version: "5.0.16",
            command: "dotnet"
        );

        CreateNuGetConfig(tempNuget);

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(
            target,
            new UpdateOptions { ExcludePackages = ["Microsoft.EntityFrameworkCore"] },
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.Empty(
            summary.UpdatedPackages.SelectMany(x =>
                x.Updates.Select(u => $"{x.PackageId} {u.From}->{u.To} in {u.Project}")
            )
        );
        Assert.Equal(
            toolsOriginal,
            await File.ReadAllTextAsync(tempDotnetConfig, TestContext.Current.CancellationToken)
        );
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
        var summary = await update.UpdateAsync(
            tempApp,
            cancellationToken: TestContext.Current.CancellationToken
        );

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
        var summary = await update.UpdateAsync(
            tempApp,
            cancellationToken: TestContext.Current.CancellationToken
        );

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
        var summary = await update.UpdateAsync(
            temp,
            cancellationToken: TestContext.Current.CancellationToken
        );

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
        var summary = await update.UpdateAsync(
            tempApp,
            new UpdateOptions { DryRun = true },
            cancellationToken: TestContext.Current.CancellationToken
        );

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
    public async Task Given_CsFileWithoutPackageDirective_When_CreateRootDir_Then_ThrowWithPath()
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
        var ex = await Assert.ThrowsAsync<InvalidUpdateTargetException>(() =>
            RootDir.CreateAsync(tempApp, cancellationToken: TestContext.Current.CancellationToken)
        );

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
        var summary = await update.UpdateAsync(
            tempApp,
            cancellationToken: TestContext.Current.CancellationToken
        );

        // Assert
        Assert.True(summary.UnknownPackages.TryGetValue("Unknown.Package", out var projects));
        Assert.Contains("Build.cs", projects.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Given_UnknownPackage_When_FailOnIncomplete_Then_ShouldFail()
    {
        // Arrange
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(Given_UnknownPackage_When_FailOnIncomplete_Then_ShouldFail)
        );
        var tempApp = Path.Combine(temp, "Build.cs");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);

        await CreateTempFileBasedAppAsync(tempApp, packages: [new("Unknown.Package", "1.0.0")]);
        CreateNuGetConfig(tempNuget);

        var update = new Updater();

        // Act
        var withFlag = await update.UpdateAsync(
            tempApp,
            new UpdateOptions { FailOnIncomplete = true },
            TestContext.Current.CancellationToken
        );

        var withoutFlag = await update.UpdateAsync(
            tempApp,
            new UpdateOptions { FailOn = FailOn.Vulnerable },
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.True(withFlag.ShouldFail);
        Assert.False(withoutFlag.ShouldFail);
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
        var summary = await update.UpdateAsync(
            tempSlnx,
            cancellationToken: TestContext.Current.CancellationToken
        );

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
        var summary = await update.UpdateAsync(
            temp,
            cancellationToken: TestContext.Current.CancellationToken
        );

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
        var summary = await update.UpdateAsync(
            temp,
            cancellationToken: TestContext.Current.CancellationToken
        );

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
        var summary = await update.UpdateAsync(
            temp,
            cancellationToken: TestContext.Current.CancellationToken
        );

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
        var summary = await update.UpdateAsync(
            temp,
            cancellationToken: TestContext.Current.CancellationToken
        );

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
        var summary = await update.UpdateAsync(
            temp,
            cancellationToken: TestContext.Current.CancellationToken
        );

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
        var summary = await update.UpdateAsync(
            temp,
            cancellationToken: TestContext.Current.CancellationToken
        );

        // Assert
        var updatedPackage = Assert.Single(summary.UpdatedPackages);
        var updated = Assert.Single(updatedPackage.Updates);

        Assert.Equal("Has.Newer.Tfm", updatedPackage.PackageId);
        Assert.Equal("3.1.0", updated.From.ToString());
        Assert.Equal("5.0.0", updated.To.ToString());

        var content = await File.ReadAllTextAsync(tempProps, TestContext.Current.CancellationToken);

        Assert.Contains("Version=\"5.0.0\"", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Given_AlignWithTfm_When_DotnetToolPackageHasNewerMajor_Then_CapToProjectTfmMajor()
    {
        // Arrange - Has.Newer.Tfm 3.1.0/5.0.0/6.0.0 are all otherwise valid updates for a tool
        // entry (dotnet-tools.json isn't tied to a specific TFM when resolving compatibility),
        // but with "Has.Newer.Tfm" matched by alignWithTfm, the update must be capped to the
        // affected csproj's TFM major (net5.0 => 5), even though 6.0.0 is newer and otherwise
        // picked.
        var temp = Path.Combine(
            Paths.Temporary.Root,
            nameof(Given_AlignWithTfm_When_DotnetToolPackageHasNewerMajor_Then_CapToProjectTfmMajor)
        );
        var tempCsproj = Path.Combine(temp, "Dummy.App.csproj");
        var tempDotnetConfig = Path.Combine(temp, ".config", "dotnet-tools.json");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(new FileInfo(tempDotnetConfig).DirectoryName!);

        await CreateTempCsprojAsync(tempCsproj, "net5.0");

        var toolsOriginal = await CreateToolsConfigAsync(
            path: tempDotnetConfig,
            packageId: "Has.Newer.Tfm",
            version: "3.1.0",
            command: "dummy"
        );

        CreateNuGetConfig(tempNuget);

        var update = new Updater();

        // Act
        var summary = await update.UpdateAsync(
            temp,
            new UpdateOptions { AlignWithTfm = ["Has.Newer.Tfm"] },
            cancellationToken: TestContext.Current.CancellationToken
        );

        // Assert
        var updatedPackage = Assert.Single(summary.UpdatedPackages);
        var updated = Assert.Single(updatedPackage.Updates);

        Assert.Equal("Has.Newer.Tfm", updatedPackage.PackageId);
        Assert.Equal("3.1.0", updated.From.ToString());
        Assert.Equal("5.0.0", updated.To.ToString());

        var content = await File.ReadAllTextAsync(
            tempDotnetConfig,
            TestContext.Current.CancellationToken
        );

        Assert.NotEqual(toolsOriginal, content);
        Assert.Contains("\"version\": \"5.0.0\"", content, StringComparison.Ordinal);
    }
}
