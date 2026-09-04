using NuGet.Frameworks;
using NuGet.Versioning;
using UpdatR.Domain;
using UpdatR.Domain.Utils;
using UpdatR.Internals;

namespace UpdatR.UnitTests;

public class TargetFrameworkCompatibilityTests
{
    [Fact]
    public void TryGetLatestCompatibleWithAllTfmsReturnsLowestCommonUpdate()
    {
        // Arrange - net6.0 can go all the way to 3.0.0, but net5.0 can't use a version that only
        // targets net6.0 (net5.0 is older, so it can't consume net6.0-targeted assets), meaning
        // its own latest compatible update is 2.0.0. The lowest of the two per-framework results
        // (2.0.0) is what's guaranteed to be compatible with every tfm.
        var package = new NuGetPackage(
            "package-id",
            [
                new PackageMetadata(
                    NuGetVersion.Parse("1.0.0"),
                    [NuGetFramework.Parse("net5.0"), NuGetFramework.Parse("net6.0")],
                    null,
                    null
                ),
                new PackageMetadata(
                    NuGetVersion.Parse("2.0.0"),
                    [NuGetFramework.Parse("net5.0"), NuGetFramework.Parse("net6.0")],
                    null,
                    null
                ),
                new PackageMetadata(
                    NuGetVersion.Parse("3.0.0"),
                    [NuGetFramework.Parse("net6.0")],
                    null,
                    null
                ),
            ]
        );

        // Act
        var found = TargetFrameworkCompatibility.TryGetLatestCompatibleWithAllTfms(
            package,
            NuGetVersion.Parse("1.0.0"),
            [NuGetFramework.Parse("net5.0"), NuGetFramework.Parse("net6.0")],
            usePrerelease: false,
            allowedLicenses: null,
            out var updateTo
        );

        // Assert
        Assert.True(found);
        Assert.Equal("2.0.0", updateTo?.Version.ToString());
    }

    [Fact]
    public void TryGetLatestCompatibleWithAllTfmsReturnsFalseWhenAnyTfmHasNoUpdate()
    {
        // Arrange - the only newer version (2.0.0) targets net6.0 only, so it isn't a valid
        // update for a net5.0 project (older frameworks can't consume newer-targeted assets) -
        // meaning no version is compatible with every tfm.
        var package = new NuGetPackage(
            "package-id",
            [
                new PackageMetadata(
                    NuGetVersion.Parse("1.0.0"),
                    [NuGetFramework.Parse("net5.0"), NuGetFramework.Parse("net6.0")],
                    null,
                    null
                ),
                new PackageMetadata(
                    NuGetVersion.Parse("2.0.0"),
                    [NuGetFramework.Parse("net6.0")],
                    null,
                    null
                ),
            ]
        );

        // Act
        var found = TargetFrameworkCompatibility.TryGetLatestCompatibleWithAllTfms(
            package,
            NuGetVersion.Parse("1.0.0"),
            [NuGetFramework.Parse("net5.0"), NuGetFramework.Parse("net6.0")],
            usePrerelease: false,
            allowedLicenses: null,
            out var updateTo
        );

        // Assert
        Assert.False(found);
        Assert.Null(updateTo);
    }

    [Fact]
    public void TryGetLatestCompatibleWithAllTfmsHonorsMaxMajor()
    {
        // Arrange
        var package = new NuGetPackage(
            "package-id",
            [
                new PackageMetadata(
                    NuGetVersion.Parse("1.0.0"),
                    [NuGetFramework.Parse("net9.0")],
                    null,
                    null
                ),
                new PackageMetadata(
                    NuGetVersion.Parse("2.0.0"),
                    [NuGetFramework.Parse("net9.0")],
                    null,
                    null
                ),
            ]
        );

        // Act
        var found = TargetFrameworkCompatibility.TryGetLatestCompatibleWithAllTfms(
            package,
            NuGetVersion.Parse("1.0.0"),
            [NuGetFramework.Parse("net9.0")],
            usePrerelease: false,
            allowedLicenses: null,
            out var updateTo,
            maxMajor: 1
        );

        // Assert
        Assert.False(found);
        Assert.Null(updateTo);
    }

    [Theory]
    [InlineData(false, "1.0.0", "3.0.0")]
    [InlineData(true, "1.0.0", "3.1.0-beta")]
    [InlineData(false, "3.0.0", null)]
    public void TryGetLatestIgnoringTfmCompatibilityIgnoresFrameworkCompatibility(
        bool usePrerelease,
        string from,
        string? expected
    )
    {
        // Arrange - 3.0.0 and 3.1.0-beta both only target net10.0, but the method should still
        // find them when checking from a net5.0-only installed version, ignoring tfm entirely.
        var package = new NuGetPackage(
            "package-id",
            [
                new PackageMetadata(
                    NuGetVersion.Parse("1.0.0"),
                    [NuGetFramework.Parse("net5.0")],
                    null,
                    null
                ),
                new PackageMetadata(
                    NuGetVersion.Parse("3.0.0"),
                    [NuGetFramework.Parse("net10.0")],
                    null,
                    null
                ),
                new PackageMetadata(
                    NuGetVersion.Parse("3.1.0-beta"),
                    [NuGetFramework.Parse("net10.0")],
                    null,
                    null
                ),
            ]
        );

        // Act
        var found = TargetFrameworkCompatibility.TryGetLatestIgnoringTfmCompatibility(
            package,
            NuGetVersion.Parse(from),
            usePrerelease,
            allowedLicenses: null,
            maxMajor: null,
            out var updateTo
        );

        // Assert
        if (expected is null)
        {
            Assert.False(found);
        }
        else
        {
            Assert.True(found);
            Assert.Equal(expected, updateTo?.Version.ToString());
        }
    }

    [Fact]
    public void TryGetLatestIgnoringTfmCompatibilityHonorsMaxMajor()
    {
        // Arrange
        var package = new NuGetPackage(
            "package-id",
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
                new PackageMetadata(
                    NuGetVersion.Parse("3.0.0"),
                    [NuGetFramework.Parse("net5.0")],
                    null,
                    null
                ),
            ]
        );

        // Act
        var found = TargetFrameworkCompatibility.TryGetLatestIgnoringTfmCompatibility(
            package,
            NuGetVersion.Parse("1.0.0"),
            usePrerelease: false,
            allowedLicenses: null,
            maxMajor: 2,
            out var updateTo
        );

        // Assert
        Assert.True(found);
        Assert.Equal("2.0.0", updateTo?.Version.ToString());
    }

    [Fact]
    public void TryGetLatestIgnoringTfmCompatibilityHonorsAllowedLicenses()
    {
        // Arrange
        var package = new NuGetPackage(
            "package-id",
            [
                new PackageMetadata(
                    NuGetVersion.Parse("1.0.0"),
                    [NuGetFramework.Parse("net5.0")],
                    null,
                    null,
                    "MIT"
                ),
                new PackageMetadata(
                    NuGetVersion.Parse("2.0.0"),
                    [NuGetFramework.Parse("net5.0")],
                    null,
                    null,
                    "GPL-3.0"
                ),
            ]
        );

        // Act
        var found = TargetFrameworkCompatibility.TryGetLatestIgnoringTfmCompatibility(
            package,
            NuGetVersion.Parse("1.0.0"),
            usePrerelease: false,
            allowedLicenses: ["MIT"],
            maxMajor: null,
            out var updateTo
        );

        // Assert
        Assert.False(found);
        Assert.Null(updateTo);
    }

    [Fact]
    public void TryGetLatestIgnoringTfmCompatibilityFallsBackToPrereleaseWhenInstalledIsPrerelease()
    {
        // Arrange - no stable version newer than the installed prerelease exists, but a newer
        // prerelease does, and should be found since the installed version is itself prerelease.
        var package = new NuGetPackage(
            "package-id",
            [
                new PackageMetadata(
                    NuGetVersion.Parse("1.0.0-beta"),
                    [NuGetFramework.Parse("net5.0")],
                    null,
                    null
                ),
                new PackageMetadata(
                    NuGetVersion.Parse("1.0.0-rc"),
                    [NuGetFramework.Parse("net5.0")],
                    null,
                    null
                ),
            ]
        );

        // Act
        var found = TargetFrameworkCompatibility.TryGetLatestIgnoringTfmCompatibility(
            package,
            NuGetVersion.Parse("1.0.0-beta"),
            usePrerelease: false,
            allowedLicenses: null,
            maxMajor: null,
            out var updateTo
        );

        // Assert
        Assert.True(found);
        Assert.Equal("1.0.0-rc", updateTo?.Version.ToString());
    }
}
