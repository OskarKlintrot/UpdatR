using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;
using NuGet.Configuration;
using static UpdatR.IntegrationTests.FileCreationUtils;

namespace UpdatR.IntegrationTests;

/// <summary>
/// Minimum publish age ("cooldown"). Each test gets its own local feed, since a local feed's
/// publish date is its .nupkg's last write time, which the tests control explicitly.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1707:Identifiers should not contain underscores",
    Justification = "Test methods"
)]
public class MinPublishAgeTests
{
    private static readonly DateTime s_old = DateTime.UtcNow.AddDays(-30);
    private static readonly DateTime s_recent = DateTime.UtcNow.AddHours(-1);

    [Fact]
    public async Task Given_UpdatRRcMinPublishAge_When_LatestIsTooRecent_Then_SkipUpdate()
    {
        // Arrange
        var temp = CreateTemp(
            nameof(Given_UpdatRRcMinPublishAge_When_LatestIsTooRecent_Then_SkipUpdate)
        );
        var tempCsproj = Path.Combine(temp, "Dummy.App.csproj");

        var csprojOriginal = await CreateTempCsprojAsync(
            tempCsproj,
            new KeyValuePair<string, string>("Dummy", "0.0.1")
        );

        CreateFeed(temp, ("Dummy.0.0.1.nupkg", s_old), ("Dummy.0.0.2.nupkg", s_recent));
        CreateCooldownNuGetConfig(temp, "72", "Dummy");

        await CreateUpdatRConfigAsync(
            Path.Combine(temp, UpdatRConfig.FileName),
            """{ "minPublishAgeHours": 48 }"""
        );

        // Act
        var summary = await new Updater().UpdateAsync(
            tempCsproj,
            new UpdateOptions { FailOn = FailOn.Outdated },
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.Empty(summary.UpdatedPackages);
        Assert.Empty(summary.UnknownPackages);
        Assert.False(summary.ShouldFail);

        var skippedPackage = Assert.Single(summary.SkippedUpdatePackages);
        var skippedVersion = Assert.Single(skippedPackage.Versions);

        Assert.Equal("Dummy", skippedPackage.PackageId);
        Assert.Equal("0.0.2", skippedVersion.Version.NuGetVersion.ToString());
        Assert.Equal(SkippedUpdateReason.MinimumPublishAge, skippedVersion.Version.Reason);
        Assert.Equal(
            csprojOriginal,
            await File.ReadAllTextAsync(tempCsproj, TestContext.Current.CancellationToken)
        );
    }

    [Theory]
    [InlineData(0, "0.0.2")]
    [InlineData(48, "0.0.1")]
    [InlineData(null, "0.0.1")]
    public async Task Given_OptionsMinPublishAge_When_Update_Then_OverrideUpdatRRc(
        int? optionHours,
        string expectedVersion
    )
    {
        // Arrange
        var temp = CreateTemp(
            $"{nameof(Given_OptionsMinPublishAge_When_Update_Then_OverrideUpdatRRc)}_{optionHours?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null"}"
        );
        var tempCsproj = Path.Combine(temp, "Dummy.App.csproj");

        await CreateTempCsprojAsync(tempCsproj, new KeyValuePair<string, string>("Dummy", "0.0.1"));

        CreateFeed(temp, ("Dummy.0.0.1.nupkg", s_old), ("Dummy.0.0.2.nupkg", s_recent));
        CreateCooldownNuGetConfig(temp);

        await CreateUpdatRConfigAsync(
            Path.Combine(temp, UpdatRConfig.FileName),
            """{ "minPublishAgeHours": 48 }"""
        );

        // Act
        await new Updater().UpdateAsync(
            tempCsproj,
            new UpdateOptions { MinPublishAgeHours = optionHours },
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.Contains(
            $"Include=\"Dummy\" Version=\"{expectedVersion}\"",
            await File.ReadAllTextAsync(tempCsproj, TestContext.Current.CancellationToken),
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData("0", "0.0.2")]
    [InlineData("48", "0.0.1")]
    public async Task UpdateAsync_NuGetMinPublishAge_UsesSourceSetting(
        string minPublishAgeHours,
        string expectedVersion
    )
    {
        // Arrange
        var temp = CreateTemp(
            $"{nameof(UpdateAsync_NuGetMinPublishAge_UsesSourceSetting)}_{minPublishAgeHours}"
        );
        var tempCsproj = Path.Combine(temp, "Dummy.App.csproj");

        await CreateTempCsprojAsync(tempCsproj, new KeyValuePair<string, string>("Dummy", "0.0.1"));

        CreateFeed(temp, ("Dummy.0.0.1.nupkg", s_old), ("Dummy.0.0.2.nupkg", s_recent));
        CreateCooldownNuGetConfig(temp, minPublishAgeHours);
        var configPath = Path.Combine(temp, "nuget.config");
        var configOriginal = await File.ReadAllTextAsync(
            configPath,
            TestContext.Current.CancellationToken
        );

        // Act
        var summary = await new Updater().UpdateAsync(
            tempCsproj,
            cancellationToken: TestContext.Current.CancellationToken
        );

        // Assert
        Assert.Contains(
            $"Include=\"Dummy\" Version=\"{expectedVersion}\"",
            await File.ReadAllTextAsync(tempCsproj, TestContext.Current.CancellationToken),
            StringComparison.Ordinal
        );
        Assert.Equal(
            expected: configOriginal,
            actual: await File.ReadAllTextAsync(configPath, TestContext.Current.CancellationToken)
        );
        if (minPublishAgeHours == "48")
        {
            var skipped = Assert.Single(Assert.Single(summary.SkippedUpdatePackages).Versions);
            Assert.Equal(
                expected: SkippedUpdateReason.MinimumPublishAge,
                actual: skipped.Version.Reason
            );
        }
    }

    [Theory]
    [InlineData("Dummy", "0.0.2")]
    [InlineData("dum*", "0.0.2")]
    [InlineData("*", "0.0.2")]
    [InlineData("Other.*", "0.0.1")]
    public async Task UpdateAsync_NuGetMinPublishAgeException_UsesSdkPatternMatching(
        string pattern,
        string expectedVersion
    )
    {
        // Arrange
        var temp = CreateTemp(
            $"{nameof(UpdateAsync_NuGetMinPublishAgeException_UsesSdkPatternMatching)}_{pattern.Replace('*', '_')}"
        );
        var tempCsproj = Path.Combine(temp, "Dummy.App.csproj");
        await CreateTempCsprojAsync(tempCsproj, new KeyValuePair<string, string>("Dummy", "0.0.1"));
        CreateFeed(temp, ("Dummy.0.0.1.nupkg", s_old), ("Dummy.0.0.2.nupkg", s_recent));
        CreateCooldownNuGetConfig(temp, "48", pattern);

        // Act
        await new Updater().UpdateAsync(
            tempCsproj,
            cancellationToken: TestContext.Current.CancellationToken
        );

        // Assert
        Assert.Contains(
            $"Include=\"Dummy\" Version=\"{expectedVersion}\"",
            await File.ReadAllTextAsync(tempCsproj, TestContext.Current.CancellationToken),
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData(0, true, null, "0.0.2")]
    [InlineData(48, true, "Dummy", "0.0.1")]
    [InlineData(0, false, null, "0.0.2")]
    [InlineData(48, false, "Dummy", "0.0.1")]
    public async Task UpdateAsync_ExplicitMinPublishAge_OverridesNuGetSettingsAndExceptions(
        int hours,
        bool useOptions,
        string? exceptionPattern,
        string expectedVersion
    )
    {
        // Arrange
        var temp = CreateTemp(
            $"{nameof(UpdateAsync_ExplicitMinPublishAge_OverridesNuGetSettingsAndExceptions)}_{hours}_{useOptions}"
        );
        var tempCsproj = Path.Combine(temp, "Dummy.App.csproj");
        await CreateTempCsprojAsync(tempCsproj, new KeyValuePair<string, string>("Dummy", "0.0.1"));
        CreateFeed(temp, ("Dummy.0.0.1.nupkg", s_old), ("Dummy.0.0.2.nupkg", s_recent));
        CreateCooldownNuGetConfig(temp, "72", exceptionPattern);
        if (!useOptions)
        {
            await CreateUpdatRConfigAsync(
                Path.Combine(temp, UpdatRConfig.FileName),
                $$"""{ "minPublishAgeHours": {{hours}} }"""
            );
        }

        // Act
        await new Updater().UpdateAsync(
            tempCsproj,
            new UpdateOptions { MinPublishAgeHours = useOptions ? hours : null },
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.Contains(
            $"Include=\"Dummy\" Version=\"{expectedVersion}\"",
            await File.ReadAllTextAsync(tempCsproj, TestContext.Current.CancellationToken),
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("256204779")]
    public async Task UpdateAsync_InvalidNuGetMinPublishAge_FailsBeforeChangingProject(string hours)
    {
        // Arrange
        var temp = CreateTemp(
            $"{nameof(UpdateAsync_InvalidNuGetMinPublishAge_FailsBeforeChangingProject)}_{hours}"
        );
        var tempCsproj = Path.Combine(temp, "Dummy.App.csproj");
        var original = await CreateTempCsprojAsync(
            tempCsproj,
            new KeyValuePair<string, string>("Dummy", "0.0.1")
        );
        CreateFeed(temp, ("Dummy.0.0.1.nupkg", s_old), ("Dummy.0.0.2.nupkg", s_recent));
        CreateCooldownNuGetConfig(temp, hours);

        // Act
        var exception = await Record.ExceptionAsync(() =>
            new Updater().UpdateAsync(
                tempCsproj,
                cancellationToken: TestContext.Current.CancellationToken
            )
        );

        // Assert
        Assert.IsType<NuGetConfigurationException>(exception);
        Assert.Contains("minPublishAgeHours", exception.Message, StringComparison.Ordinal);
        Assert.Equal(
            expected: original,
            actual: await File.ReadAllTextAsync(tempCsproj, TestContext.Current.CancellationToken)
        );
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task UpdateAsync_DifferentSourceMinPublishAges_AppliesEachSourcesPolicy(
        bool reverseSources,
        bool eligibleSourceHasNewVersion
    )
    {
        // Arrange
        var temp = CreateTemp(
            $"{nameof(UpdateAsync_DifferentSourceMinPublishAges_AppliesEachSourcesPolicy)}_{reverseSources}_{eligibleSourceHasNewVersion}"
        );
        var tempCsproj = Path.Combine(temp, "Dummy.App.csproj");
        await CreateTempCsprojAsync(tempCsproj, new KeyValuePair<string, string>("Dummy", "0.0.1"));
        var restrictedFeed = CreateFeed(
            Path.Combine(temp, "restricted"),
            ("Dummy.0.0.1.nupkg", s_old),
            ("Dummy.0.0.2.nupkg", s_recent)
        );
        var eligibleFeed = eligibleSourceHasNewVersion
            ? CreateFeed(
                Path.Combine(temp, "eligible"),
                ("Dummy.0.0.1.nupkg", s_old),
                ("Dummy.0.0.2.nupkg", s_recent)
            )
            : CreateFeed(Path.Combine(temp, "eligible"), ("Dummy.0.0.1.nupkg", s_old));
        var restrictedSource = new XElement(
            "add",
            new XAttribute("key", "restricted"),
            new XAttribute("value", restrictedFeed),
            new XAttribute("minPublishAgeHours", "48")
        );
        var eligibleSource = new XElement(
            "add",
            new XAttribute("key", "eligible"),
            new XAttribute("value", eligibleFeed)
        );
        new XDocument(
            new XElement(
                "configuration",
                new XElement(
                    "packageSources",
                    new XElement("clear"),
                    reverseSources ? eligibleSource : restrictedSource,
                    reverseSources ? restrictedSource : eligibleSource
                )
            )
        ).Save(Path.Combine(temp, "nuget.config"));

        // Act
        var summary = await new Updater().UpdateAsync(
            tempCsproj,
            cancellationToken: TestContext.Current.CancellationToken
        );

        // Assert
        var expectedVersion = eligibleSourceHasNewVersion ? "0.0.2" : "0.0.1";
        Assert.Contains(
            $"Include=\"Dummy\" Version=\"{expectedVersion}\"",
            await File.ReadAllTextAsync(tempCsproj, TestContext.Current.CancellationToken),
            StringComparison.Ordinal
        );
        if (eligibleSourceHasNewVersion)
        {
            Assert.Empty(summary.SkippedUpdatePackages);
        }
        else
        {
            var skipped = Assert.Single(Assert.Single(summary.SkippedUpdatePackages).Versions);
            Assert.Equal(
                expected: SkippedUpdateReason.MinimumPublishAge,
                actual: skipped.Version.Reason
            );
        }
    }

    [Fact]
    public async Task Given_MinPublishAge_When_OlderUpdateIsOldEnough_Then_UpdateToItAndReportNewer()
    {
        // Arrange
        var temp = CreateTemp(
            nameof(Given_MinPublishAge_When_OlderUpdateIsOldEnough_Then_UpdateToItAndReportNewer)
        );
        var tempCsproj = Path.Combine(temp, "Dummy.App.csproj");

        await CreateTempCsprojAsync(
            tempCsproj,
            "net6.0",
            new KeyValuePair<string, string>("Has.Newer.Tfm", "3.1.0")
        );

        CreateFeed(
            temp,
            ("Has.Newer.Tfm.3.1.0.nupkg", s_old),
            ("Has.Newer.Tfm.5.0.0.nupkg", s_old),
            ("Has.Newer.Tfm.6.0.0.nupkg", s_recent)
        );
        CreateCooldownNuGetConfig(temp);

        // Act
        var summary = await new Updater().UpdateAsync(
            tempCsproj,
            new UpdateOptions { MinPublishAgeHours = 48 },
            TestContext.Current.CancellationToken
        );

        // Assert
        var updated = Assert.Single(Assert.Single(summary.UpdatedPackages).Updates);

        Assert.Equal("3.1.0", updated.From.ToString());
        Assert.Equal("5.0.0", updated.To.ToString());

        var skippedVersion = Assert.Single(Assert.Single(summary.SkippedUpdatePackages).Versions);

        Assert.Equal("6.0.0", skippedVersion.Version.NuGetVersion.ToString());
        Assert.Equal(SkippedUpdateReason.MinimumPublishAge, skippedVersion.Version.Reason);
    }

    [Fact]
    public async Task Given_MinPublishAge_When_InstalledVersionIsTooRecent_Then_DoNotDowngrade()
    {
        // Arrange
        var temp = CreateTemp(
            nameof(Given_MinPublishAge_When_InstalledVersionIsTooRecent_Then_DoNotDowngrade)
        );
        var tempCsproj = Path.Combine(temp, "Dummy.App.csproj");

        var csprojOriginal = await CreateTempCsprojAsync(
            tempCsproj,
            new KeyValuePair<string, string>("Dummy", "0.0.2")
        );

        CreateFeed(temp, ("Dummy.0.0.1.nupkg", s_old), ("Dummy.0.0.2.nupkg", s_recent));
        CreateCooldownNuGetConfig(temp);

        // Act
        var summary = await new Updater().UpdateAsync(
            tempCsproj,
            new UpdateOptions { MinPublishAgeHours = 48 },
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.Empty(summary.UpdatedPackages);
        Assert.Empty(summary.SkippedUpdatePackages);
        Assert.Empty(summary.UnknownPackages);
        Assert.Equal(
            csprojOriginal,
            await File.ReadAllTextAsync(tempCsproj, TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task Given_MinPublishAge_When_PreviewIsTooRecent_Then_PickOlderPreview()
    {
        // Arrange
        var temp = CreateTemp(
            nameof(Given_MinPublishAge_When_PreviewIsTooRecent_Then_PickOlderPreview)
        );
        var tempCsproj = Path.Combine(temp, "Dummy.App.csproj");

        await CreateTempCsprojAsync(
            tempCsproj,
            new KeyValuePair<string, string>("Has.Previews", "0.0.1-preview")
        );

        CreateFeed(
            temp,
            ("Has.Previews.0.0.1-preview.nupkg", s_old),
            ("Has.Previews.0.0.1.nupkg", s_old),
            ("Has.Previews.0.0.2.nupkg", s_old),
            ("Has.Previews.0.0.3-preview.0.nupkg", s_old),
            ("Has.Previews.0.0.3-preview.1.nupkg", s_recent)
        );
        CreateCooldownNuGetConfig(temp);

        // Act
        var summary = await new Updater().UpdateAsync(
            tempCsproj,
            new UpdateOptions { MinPublishAgeHours = 48, Prerelease = true },
            TestContext.Current.CancellationToken
        );

        // Assert
        var updated = Assert.Single(Assert.Single(summary.UpdatedPackages).Updates);

        Assert.Equal("0.0.3-preview.0", updated.To.ToString());
    }

    [Fact]
    public async Task Given_MinPublishAge_When_PinnedToolVersionIsTooRecent_Then_PickOlderEligibleVersion()
    {
        // Arrange
        var temp = CreateTemp(
            nameof(
                Given_MinPublishAge_When_PinnedToolVersionIsTooRecent_Then_PickOlderEligibleVersion
            )
        );
        var target = Path.Combine(temp, "src");
        var tempDotnetConfig = Path.Combine(target, ".config", "dotnet-tools.json");
        var tempCsproj = Path.Combine(target, "Dummy.App.csproj");

        Directory.CreateDirectory(Path.GetDirectoryName(tempDotnetConfig)!);

        // dotnet-ef is pinned to Microsoft.EntityFrameworkCore (5.0.16), but 5.0.16 is too recent.
        // The pin must not bypass the minimum publish age, so 5.0.12 is the highest eligible.
        await CreateTempCsprojAsync(
            tempCsproj,
            "net5.0",
            new KeyValuePair<string, string>("Microsoft.EntityFrameworkCore", "5.0.16")
        );

        await CreateToolsConfigAsync(
            path: tempDotnetConfig,
            packageId: "dotnet-ef",
            version: "5.0.5",
            command: "dotnet"
        );

        CreateFeed(
            temp,
            ("dotnet-ef.5.0.5.nupkg", s_old),
            ("dotnet-ef.5.0.12.nupkg", s_old),
            ("dotnet-ef.5.0.16.nupkg", s_recent),
            ("dotnet-ef.5.0.17.nupkg", s_old),
            ("Microsoft.EntityFrameworkCore.5.0.12.nupkg", s_old),
            ("Microsoft.EntityFrameworkCore.5.0.16.nupkg", s_old)
        );
        CreateCooldownNuGetConfig(temp);

        // Act
        var summary = await new Updater().UpdateAsync(
            target,
            new UpdateOptions { MinPublishAgeHours = 48 },
            TestContext.Current.CancellationToken
        );

        // Assert
        var updatedPackage = Assert.Single(
            summary.UpdatedPackages,
            x => x.PackageId == "dotnet-ef"
        );
        var updated = Assert.Single(updatedPackage.Updates);

        Assert.Equal("5.0.5", updated.From.ToString());
        Assert.Equal("5.0.12", updated.To.ToString());
    }

    [Fact]
    public async Task Given_MinPublishAge_When_PinnedToolHasNoEligibleVersion_Then_DoNotUpdate()
    {
        // Arrange
        var temp = CreateTemp(
            nameof(Given_MinPublishAge_When_PinnedToolHasNoEligibleVersion_Then_DoNotUpdate)
        );
        var target = Path.Combine(temp, "src");
        var tempDotnetConfig = Path.Combine(target, ".config", "dotnet-tools.json");
        var tempCsproj = Path.Combine(target, "Dummy.App.csproj");

        Directory.CreateDirectory(Path.GetDirectoryName(tempDotnetConfig)!);

        await CreateTempCsprojAsync(
            tempCsproj,
            "net5.0",
            new KeyValuePair<string, string>("Microsoft.EntityFrameworkCore", "5.0.16")
        );

        var toolsOriginal = await CreateToolsConfigAsync(
            path: tempDotnetConfig,
            packageId: "dotnet-ef",
            version: "5.0.12",
            command: "dotnet"
        );

        CreateFeed(
            temp,
            ("dotnet-ef.5.0.12.nupkg", s_old),
            ("dotnet-ef.5.0.16.nupkg", s_recent),
            ("dotnet-ef.5.0.17.nupkg", s_old),
            ("Microsoft.EntityFrameworkCore.5.0.16.nupkg", s_old)
        );
        CreateCooldownNuGetConfig(temp);

        // Act
        var summary = await new Updater().UpdateAsync(
            target,
            new UpdateOptions { MinPublishAgeHours = 48 },
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.DoesNotContain(summary.UpdatedPackages, x => x.PackageId == "dotnet-ef");
        Assert.Equal(
            toolsOriginal,
            await File.ReadAllTextAsync(tempDotnetConfig, TestContext.Current.CancellationToken)
        );
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public async Task Given_NegativeOptionsMinPublishAge_When_Update_Then_Throw(int hours)
    {
        // Arrange
        var temp = CreateTemp(
            $"{nameof(Given_NegativeOptionsMinPublishAge_When_Update_Then_Throw)}_{hours}"
        );
        var tempCsproj = Path.Combine(temp, "Dummy.App.csproj");

        await CreateTempCsprojAsync(tempCsproj, new KeyValuePair<string, string>("Dummy", "0.0.1"));

        CreateFeed(temp, ("Dummy.0.0.1.nupkg", s_old), ("Dummy.0.0.2.nupkg", s_old));
        CreateCooldownNuGetConfig(temp);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<MinimumPublishAgeException>(() =>
            new Updater().UpdateAsync(
                tempCsproj,
                new UpdateOptions { MinPublishAgeHours = hours },
                TestContext.Current.CancellationToken
            )
        );

        Assert.Contains("MinPublishAgeHours", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Given_InvalidUpdatRRcMinPublishAge_When_OptionsOverride_Then_StillThrow()
    {
        // Arrange
        var temp = CreateTemp(
            nameof(Given_InvalidUpdatRRcMinPublishAge_When_OptionsOverride_Then_StillThrow)
        );
        var tempCsproj = Path.Combine(temp, "Dummy.App.csproj");

        var csprojOriginal = await CreateTempCsprojAsync(
            tempCsproj,
            new KeyValuePair<string, string>("Dummy", "0.0.1")
        );

        CreateFeed(temp, ("Dummy.0.0.1.nupkg", s_old), ("Dummy.0.0.2.nupkg", s_old));
        CreateCooldownNuGetConfig(temp);

        await CreateUpdatRConfigAsync(
            Path.Combine(temp, UpdatRConfig.FileName),
            """{ "minPublishAgeHours": -5 }"""
        );

        // Act & Assert
        await Assert.ThrowsAnyAsync<UpdatRException>(() =>
            new Updater().UpdateAsync(
                tempCsproj,
                new UpdateOptions { MinPublishAgeHours = 0 },
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal(
            csprojOriginal,
            await File.ReadAllTextAsync(tempCsproj, TestContext.Current.CancellationToken)
        );
    }

    private static string CreateTemp(string name)
    {
        var temp = Path.Combine(Paths.Temporary.Root, nameof(MinPublishAgeTests), name);

        if (Directory.Exists(temp))
        {
            Directory.Delete(temp, true);
        }

        Directory.CreateDirectory(temp);

        return temp;
    }

    private static string CreateFeed(
        string dir,
        params (string File, DateTime Published)[] packages
    )
    {
        var feed = Path.Combine(dir, "feed");

        Directory.CreateDirectory(feed);

        foreach (var (file, published) in packages)
        {
            var destination = Path.Combine(feed, file);

            File.Copy(Path.Combine(Paths.Packages, file), destination, overwrite: true);
            File.SetLastWriteTimeUtc(destination, published);
        }

        return feed;
    }

    private static void CreateCooldownNuGetConfig(
        string dir,
        string? minPublishAgeHours = null,
        string? exceptionPattern = null
    )
    {
        var source = new XElement(
            "add",
            new XAttribute("key", "local"),
            new XAttribute("value", Path.Combine(dir, "feed"))
        );

        if (minPublishAgeHours is not null)
        {
            source.Add(new XAttribute("minPublishAgeHours", minPublishAgeHours));
        }

        new XDocument(
            new XElement(
                "configuration",
                new XElement("packageSources", new XElement("clear"), source),
                exceptionPattern is null
                    ? null
                    : new XElement(
                        "minPublishAgeExceptions",
                        new XElement("package", new XAttribute("pattern", exceptionPattern))
                    )
            )
        ).Save(Path.Combine(dir, "nuget.config"));
    }
}
