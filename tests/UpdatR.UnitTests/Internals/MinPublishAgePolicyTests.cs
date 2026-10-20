using NuGet.Versioning;
using UpdatR.Internals;

namespace UpdatR.UnitTests;

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
        var policy = new MinPublishAgePolicy(TimeSpan.FromHours(48));

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
    public void IsTooRecentThrowsWhenPublishedIsMissingAndPolicyIsEnabled()
    {
        // Arrange
        var policy = new MinPublishAgePolicy(TimeSpan.FromHours(1));

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
