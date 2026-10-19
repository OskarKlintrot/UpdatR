using NuGet.Configuration;
using NuGet.Versioning;
using UpdatR.Internals;

namespace UpdatR.UnitTests;

public sealed class NuGetMinPublishAgeSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "dotnet-updatr",
        "unittests",
        nameof(NuGetMinPublishAgeSettingsTests),
        Guid.NewGuid().ToString("N")
    );

    public NuGetMinPublishAgeSettingsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Fact]
    public void ReadsMinPublishAgeHoursPerSource()
    {
        // Arrange
        Write(
            "",
            """
            <packageSources>
              <clear />
              <add key="strict" value="https://strict.example/v3/index.json" minPublishAgeHours="48" />
              <add key="off" value="https://off.example/v3/index.json" minPublishAgeHours="0" />
              <add key="none" value="https://none.example/v3/index.json" />
            </packageSources>
            """
        );

        // Act
        var result = NuGetMinPublishAgeSettings.GetMinPublishAgeBySourceName(Load(""));

        // Assert
        Assert.Equal(TimeSpan.FromHours(48), result["strict"]);
        Assert.Equal(TimeSpan.Zero, result["off"]);
        Assert.False(result.ContainsKey("none"));
    }

    [Fact]
    public void SourceRedefinedCloserWithoutAttributeHasNoMinPublishAge()
    {
        // Arrange
        Write(
            "",
            """
            <packageSources>
              <clear />
              <add key="feed" value="https://feed.example/v3/index.json" minPublishAgeHours="48" />
              <add key="other" value="https://other.example/v3/index.json" minPublishAgeHours="12" />
            </packageSources>
            """
        );
        Write(
            "child",
            """
            <packageSources>
              <add key="feed" value="https://feed.example/v3/index.json" />
            </packageSources>
            """
        );
        Write(
            Path.Combine("child", "deeper"),
            """
            <packageSources>
              <add key="other" value="https://other.example/v3/index.json" minPublishAgeHours="1" />
            </packageSources>
            """
        );

        // Act
        var root = NuGetMinPublishAgeSettings.GetMinPublishAgeBySourceName(Load(""));
        var child = NuGetMinPublishAgeSettings.GetMinPublishAgeBySourceName(Load("child"));
        var deeper = NuGetMinPublishAgeSettings.GetMinPublishAgeBySourceName(
            Load(Path.Combine("child", "deeper"))
        );

        // Assert
        Assert.Equal(TimeSpan.FromHours(48), root["feed"]);
        Assert.Equal(TimeSpan.FromHours(12), root["other"]);

        Assert.False(child.ContainsKey("feed"));
        Assert.Equal(TimeSpan.FromHours(12), child["other"]);

        Assert.False(deeper.ContainsKey("feed"));
        Assert.Equal(TimeSpan.FromHours(1), deeper["other"]);
    }

    [Fact]
    public void ClearedSourcesDoNotContribute()
    {
        // Arrange
        Write(
            "",
            """
            <packageSources>
              <clear />
              <add key="feed" value="https://feed.example/v3/index.json" minPublishAgeHours="48" />
            </packageSources>
            """
        );
        Write(
            "child",
            """
            <packageSources>
              <clear />
              <add key="new" value="https://new.example/v3/index.json" minPublishAgeHours="6" />
            </packageSources>
            """
        );

        // Act
        var result = NuGetMinPublishAgeSettings.GetMinPublishAgeBySourceName(Load("child"));

        // Assert
        Assert.False(result.ContainsKey("feed"));
        Assert.Equal(TimeSpan.FromHours(6), result["new"]);
    }

    [Fact]
    public void ExpandsEnvironmentVariables()
    {
        // Arrange
        var variable = $"UPDATR_TEST_{Guid.NewGuid():N}";

        Environment.SetEnvironmentVariable(variable, "36");

        try
        {
            Write(
                "",
                $"""
                <packageSources>
                  <clear />
                  <add key="feed" value="https://feed.example/v3/index.json" minPublishAgeHours="%{variable}%" />
                </packageSources>
                """
            );

            // Act
            var result = NuGetMinPublishAgeSettings.GetMinPublishAgeBySourceName(Load(""));

            // Assert
            Assert.Equal(TimeSpan.FromHours(36), result["feed"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData(" 1")]
    [InlineData("1.5")]
    [InlineData("1e3")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("4294967296")]
    [InlineData("256204779")]
    public void InvalidMinPublishAgeHoursThrows(string value)
    {
        // Arrange
        Write(
            "",
            $"""
            <packageSources>
              <clear />
              <add key="feed" value="https://feed.example/v3/index.json" minPublishAgeHours="{value}" />
            </packageSources>
            """
        );

        // Act & Assert
        var exception = Assert.Throws<NuGetConfigurationException>(() =>
            NuGetMinPublishAgeSettings.GetMinPublishAgeBySourceName(Load(""))
        );

        Assert.Contains("feed:minPublishAgeHours", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MaxMinPublishAgeHoursIsAllowed()
    {
        // Arrange
        Write(
            "",
            """
            <packageSources>
              <clear />
              <add key="feed" value="https://feed.example/v3/index.json" minPublishAgeHours="256204778" />
            </packageSources>
            """
        );

        // Act
        var result = NuGetMinPublishAgeSettings.GetMinPublishAgeBySourceName(Load(""));

        // Assert
        Assert.Equal(TimeSpan.FromHours(256204778), result["feed"]);
    }

    [Fact]
    public void ClosestExceptionsSectionReplacesSectionsFurtherAway()
    {
        // Arrange
        Write(
            "",
            """
            <packageSources><clear /></packageSources>
            <minPublishAgeExceptions>
              <package pattern="Contoso.*" />
            </minPublishAgeExceptions>
            """
        );
        Write(
            "child",
            """
            <minPublishAgeExceptions>
              <package pattern="Fabrikam" />
            </minPublishAgeExceptions>
            """
        );
        Write(Path.Combine("child", "deeper"), "");

        // Act
        var root = NuGetMinPublishAgeSettings.GetMinPublishAgeExceptions(Load(""));
        var deeper = NuGetMinPublishAgeSettings.GetMinPublishAgeExceptions(
            Load(Path.Combine("child", "deeper"))
        );

        // Assert
        Assert.Equal(["Contoso.*"], root.Patterns);
        Assert.True(root.HasException("Contoso.Package"));
        Assert.False(root.HasException("Contoso"));
        Assert.False(root.HasException("Fabrikam"));

        Assert.Equal(["Fabrikam"], deeper.Patterns);
        Assert.True(deeper.HasException("fabrikam"));
        Assert.False(deeper.HasException("Fabrikam.Other"));
        Assert.False(deeper.HasException("Contoso.Package"));
    }

    [Theory]
    [InlineData("<minPublishAgeExceptions />")]
    [InlineData(
        """<minPublishAgeExceptions><package pattern="Other" /><clear /></minPublishAgeExceptions>"""
    )]
    public void EmptyOrClearedExceptionsSectionClearsExceptions(string section)
    {
        // Arrange
        Write(
            "",
            """
            <packageSources><clear /></packageSources>
            <minPublishAgeExceptions>
              <package pattern="*" />
            </minPublishAgeExceptions>
            """
        );
        Write("child", section);

        // Act
        var result = NuGetMinPublishAgeSettings.GetMinPublishAgeExceptions(Load("child"));

        // Assert
        Assert.Empty(result.Patterns);
        Assert.False(result.IsEnabled);
        Assert.False(result.HasException("Any.Package"));
    }

    [Fact]
    public void WildcardExceptionExemptsEverything()
    {
        // Arrange
        Write(
            "",
            """
            <packageSources><clear /></packageSources>
            <minPublishAgeExceptions>
              <package pattern="*" />
            </minPublishAgeExceptions>
            """
        );

        // Act
        var result = NuGetMinPublishAgeSettings.GetMinPublishAgeExceptions(Load(""));

        // Assert
        Assert.True(result.HasException("Any.Package"));
    }

    [Theory]
    [InlineData("""<package />""")]
    [InlineData("""<package pattern="" />""")]
    [InlineData("""<package pattern=" " />""")]
    public void ExceptionWithoutPatternThrows(string package)
    {
        // Arrange
        Write(
            "",
            $"""
            <packageSources><clear /></packageSources>
            <minPublishAgeExceptions>{package}</minPublishAgeExceptions>
            """
        );

        // Act & Assert
        Assert.Throws<NuGetConfigurationException>(() =>
            NuGetMinPublishAgeSettings.GetMinPublishAgeExceptions(Load(""))
        );
    }

    private void Write(string relativeDir, string content)
    {
        var dir = Path.Combine(_root, relativeDir);

        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, "nuget.config"),
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
            {content}
            </configuration>
            """
        );
    }

    private ISettings Load(string relativeDir) =>
        Settings.LoadDefaultSettings(Path.Combine(_root, relativeDir));
}

public class MinPublishAgePolicyTests
{
    private static readonly DateTimeOffset s_now = new(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly NuGetVersion s_version = NuGetVersion.Parse("1.0.0");

    [Theory]
    [InlineData(-48 * 60 - 1, false)] // just before the cutoff
    [InlineData(-48 * 60, false)] // exactly at the cutoff
    [InlineData(-48 * 60 + 1, true)] // just after the cutoff
    [InlineData(-1, true)]
    [InlineData(60, true)] // in the future
    public void IsTooRecentComparesWithCutoff(int publishedOffsetMinutes, bool expected)
    {
        // Arrange
        var policy = new MinPublishAgePolicy(
            TimeSpan.FromHours(48),
            MinPublishAgeExceptionPatterns.Empty
        );

        // Act
        var result = policy.IsTooRecent(
            "feed",
            "Some.Package",
            s_version,
            s_now.AddMinutes(publishedOffsetMinutes),
            s_now
        );

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void IsTooRecentThrowsWhenPublishedIsMissingAndPolicyApplies()
    {
        // Arrange
        var policy = new MinPublishAgePolicy(
            TimeSpan.FromHours(1),
            MinPublishAgeExceptionPatterns.Empty
        );

        // Act & Assert
        var exception = Assert.Throws<MinimumPublishAgeException>(() =>
            policy.IsTooRecent("feed", "Some.Package", s_version, null, s_now)
        );

        Assert.Contains("'feed'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Some.Package 1.0.0", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IsTooRecentIgnoresMissingPublishedWhenDisabled()
    {
        // Act & Assert
        Assert.False(
            MinPublishAgePolicy.Disabled.IsTooRecent("feed", "Some.Package", s_version, null, s_now)
        );
        Assert.False(
            MinPublishAgePolicy.Disabled.IsTooRecent(
                "feed",
                "Some.Package",
                s_version,
                s_now.AddDays(1),
                s_now
            )
        );
    }

    [Theory]
    [InlineData("Some.Package")]
    [InlineData("Some.*")]
    [InlineData("*")]
    public void IsTooRecentIgnoresExemptPackages(string pattern)
    {
        // Arrange
        var policy = new MinPublishAgePolicy(
            TimeSpan.FromHours(48),
            new MinPublishAgeExceptionPatterns([pattern])
        );

        // Act & Assert
        Assert.False(policy.AppliesTo("Some.Package"));
        Assert.False(policy.IsTooRecent("feed", "Some.Package", s_version, null, s_now));
        Assert.False(policy.IsTooRecent("feed", "Some.Package", s_version, s_now, s_now));
        Assert.Equal(pattern != "*", policy.AppliesTo("Other.Package"));
    }

    [Fact]
    public void GetCutoffClampsInsteadOfOverflowing()
    {
        // Act
        var cutoff = MinPublishAgePolicy.GetCutoff(s_now, TimeSpan.FromHours(256204778));

        // Assert
        Assert.Equal(DateTimeOffset.MinValue, cutoff);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0, 0)]
    [InlineData(48, 48)]
    [InlineData(256204778, 256204778)]
    public void ParseHoursAcceptsValidValues(int? hours, int? expectedHours)
    {
        // Act
        var result = MinPublishAgePolicy.ParseHours(hours, "test");

        // Assert
        Assert.Equal(
            expectedHours is null ? null : TimeSpan.FromHours(expectedHours.Value),
            result
        );
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(256204779)]
    [InlineData(int.MaxValue)]
    public void ParseHoursRejectsInvalidValues(int hours)
    {
        // Act & Assert
        var exception = Assert.Throws<MinimumPublishAgeException>(() =>
            MinPublishAgePolicy.ParseHours(hours, "test-origin")
        );

        Assert.Contains("test-origin", exception.Message, StringComparison.Ordinal);
    }
}
