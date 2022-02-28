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
        var temp = Path.Combine(Paths.Temporary.Root, nameof(Given_UpToDate_When_Update_Then_DoNothing));
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
        var temp = Path.Combine(Paths.Temporary.Root, nameof(Given_UpToDate_When_UpdateDotnetConfig_Then_DoNothing));
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
}
