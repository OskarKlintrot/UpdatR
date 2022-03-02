using System.Diagnostics.CodeAnalysis;
using static UpdatR.Update.IntegrationTests.FileCreationUtils;

namespace UpdatR.Update.IntegrationTests;

[UsesVerify]
[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test methods")]
public class UpdateTests
{
    [Theory]
    [InlineData("0.0.1")]
    [InlineData("0.0.2")]
    public async Task Given_UpToDate_When_Update_Then_DoNothing(string version)
    {
        // Arrange
        var temp = Path.Combine(Paths.Temporary.Root, nameof(Given_UpToDate_When_Update_Then_DoNothing));
        var tempCsproj = Path.Combine(temp, "Dummy.App.csproj");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);

        var csprojOriginal = await CreateTempCsprojAsync(
            tempCsproj,
            new KeyValuePair<string, string>("Dummy", version));

        CreateNuGetConfig(tempNuget);

        var update = new Update();

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
        var temp = Path.Combine(Paths.Temporary.Root, nameof(Given_DirectoryAsTarget_When_SingleCsproj_Then_Update));
        var tempCsproj = Path.Combine(temp, "Dummy.App.csproj");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);

        var csprojOriginal = await CreateTempCsprojAsync(
            tempCsproj,
            new KeyValuePair<string, string>("Dummy", "0.0.1"));

        CreateNuGetConfig(tempNuget);

        var update = new Update();

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
    [InlineData("0.0.1")]
    [InlineData("0.0.2")]
    public async Task Given_UpToDate_When_UpdateDotnetConfig_Then_DoNothing(string version)
    {
        // Arrange
        var temp = Path.Combine(Paths.Temporary.Root, nameof(Given_UpToDate_When_UpdateDotnetConfig_Then_DoNothing));
        var tempDotnetConfig = Path.Combine(temp, ".config", "dotnet-tools.json");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(new FileInfo(tempDotnetConfig).DirectoryName!);

        var original = await CreateToolsConfigAsync(
            path: tempDotnetConfig,
            packageId: "Dummy",
            version: version,
            command: "dummy");

        CreateNuGetConfig(tempNuget);

        var update = new Update();

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
        var temp = Path.Combine(Paths.Temporary.Root, nameof(Given_DirectoryAsTarget_When_SingleDotnetConfig_Then_Update));
        var tempDotnetConfig = Path.Combine(temp, ".config", "dotnet-tools.json");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(new FileInfo(tempDotnetConfig).DirectoryName!);

        var original = await CreateToolsConfigAsync(
            path: tempDotnetConfig,
            packageId: "Dummy",
            version: "0.0.1",
            command: "dummy");

        CreateNuGetConfig(tempNuget);

        var update = new Update();

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

        var slnOriginal = await CreateSlnAsync(
            tempSln,
            "Dummy.App.csproj",
            tempCsproj);

        var csprojOriginal = await CreateTempCsprojAsync(
            tempCsproj,
            new KeyValuePair<string, string>("Dummy", "0.0.1"));

        var toolsOriginal = await CreateToolsConfigAsync(
            path: tempDotnetConfig,
            packageId: "Dummy",
            version: "0.0.1",
            command: "dummy");

        CreateNuGetConfig(tempNuget);

        var update = new Update();

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

        var slnOriginal = await CreateSlnAsync(
            tempSln,
            "Dummy.App.csproj",
            tempCsproj);

        var csprojOriginal = await CreateTempCsprojAsync(
            tempCsproj,
            new KeyValuePair<string, string>("Dummy", "0.0.1"));

        var toolsOriginal = await CreateToolsConfigAsync(
            path: tempDotnetConfig,
            packageId: "Dummy",
            version: "0.0.1",
            command: "dummy");

        CreateNuGetConfig(tempNuget);

        var update = new Update();

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
    [InlineData("")]
    [InlineData("Dummy.sln")]
    public async Task Given_CsprojNotAddedToSln_When_TargetSln_Then_DoNothing(string target)
    {
        // Arrange
        var temp = Path.Combine(Paths.Temporary.Root, nameof(Given_CsprojNotAddedToSln_When_TargetSln_Then_DoNothing));
        var tempSln = Path.Combine(temp, "Dummy.sln");
        var tempDotnetConfig = Path.Combine(temp, "src", ".config", "dotnet-tools.json");
        var tempCsproj1 = Path.Combine(temp, "src", "Dummy.App.csproj");
        var tempCsproj2 = Path.Combine(temp, "src", "Dummy.Lib.csproj");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(new FileInfo(tempDotnetConfig).DirectoryName!);

        var slnOriginal = await CreateSlnAsync(
            tempSln,
            "Dummy.App.csproj",
            tempCsproj1);

        var csproj1Original = await CreateTempCsprojAsync(
            tempCsproj1,
            new KeyValuePair<string, string>("Dummy", "0.0.1"));

        var csproj2Original = await CreateTempCsprojAsync(
            tempCsproj2,
            new KeyValuePair<string, string>("Dummy", "0.0.1"));

        var toolsOriginal = await CreateToolsConfigAsync(
            path: tempDotnetConfig,
            packageId: "Dummy",
            version: "0.0.1",
            command: "dummy");

        CreateNuGetConfig(tempNuget);

        var update = new Update();

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

    [Fact]
    public Task Given_OutdatedStable_When_LatestIsPrerelease_Then_UpdateToLatestStable()
    {
        throw new NotImplementedException();
    }

    [Fact]
    public Task Given_OutdatedPrerelease_When_LatestIsPrereleaseWithStableInBetween_Then_UpdateToLatestStable()
    {
        throw new NotImplementedException();
    }

    [Fact]
    public Task Given_OutdatedPrerelease_When_LatestIsPrerelease_Then_UpdateToLatestPrerelease()
    {
        throw new NotImplementedException();
    }

    [Fact]
    public Task Given_UnknownPackageId_When_Updating_Then_DoNothing()
    {
        throw new NotImplementedException();
    }
}
