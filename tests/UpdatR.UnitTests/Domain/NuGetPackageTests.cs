using NuGet.Frameworks;
using NuGet.Versioning;
using UpdatR.Domain;
using UpdatR.Internals;

namespace UpdatR.UnitTests.Domain;

public class NuGetPackageTests
{
    [Theory]
    [InlineData("0.0.1", false, "1.0.0")]
    [InlineData("0.0.1", true, "1.1.0-beta0")]
    [InlineData("1.0.0", false)]
    [InlineData("1.0.0", true, "1.1.0-beta0")]
    public void TryGetLatestComparedTo(
        string comparedTo,
        bool usePrerelease,
        string? expectedNewResult = null
    )
    {
        // Arrange
        var package = new NuGetPackage(
            "package-id",
            [
                new PackageMetadata(
                    NuGetVersion.Parse("0.0.1"),
                    [NuGetFramework.Parse("net5.0"), NuGetFramework.Parse("net6.0")],
                    null,
                    null
                ),
                new PackageMetadata(
                    NuGetVersion.Parse("1.0.0"),
                    [NuGetFramework.Parse("net6.0")],
                    null,
                    null
                ),
                new PackageMetadata(
                    NuGetVersion.Parse("1.1.0-beta0"),
                    [NuGetFramework.Parse("net6.0")],
                    null,
                    null
                ),
                new PackageMetadata(
                    NuGetVersion.Parse("2.0.0"),
                    [NuGetFramework.Parse("net7.0")],
                    null,
                    null
                ),
            ]
        );

        // Act
        var newerVersionIsAvailable = package.TryGetLatestComparedTo(
            version: NuGetVersion.Parse(comparedTo),
            targetFramework: NuGetFramework.Parse("net6.0"),
            usePrerelease: usePrerelease,
            package: out var packageMetadata
        );

        // Assert
        if (expectedNewResult is null)
        {
            Assert.False(newerVersionIsAvailable);
        }
        else
        {
            Assert.True(newerVersionIsAvailable);
            Assert.Equal(expectedNewResult, packageMetadata?.Version.ToString());
        }
    }

    [Theory]
    [InlineData("net6.0", "2.0.0")]
    [InlineData("net7.0", "2.0.0")]
    [InlineData("net9.0", "2.0.0")]
    [InlineData("netstandard2.0", "2.0.0")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Naming",
        "CA1707:Identifiers should not contain underscores",
        Justification = "Test name"
    )]
    public void TryGetLatestComparedTo_NetStandard_OnlySupport(string tfm, string? expected = null)
    {
        // Arrange
        var package = new NuGetPackage(
            "package-id",
            [
                new PackageMetadata(
                    NuGetVersion.Parse("1.0.0"),
                    [NuGetFramework.Parse("netstandard2.0")],
                    null,
                    null
                ),
                new PackageMetadata(
                    NuGetVersion.Parse("2.0.0"),
                    [NuGetFramework.Parse("netstandard2.0")],
                    null,
                    null
                ),
            ]
        );

        // Act
        var newerVersionIsAvailable = package.TryGetLatestComparedTo(
            version: NuGetVersion.Parse("1.0.0"),
            targetFramework: NuGetFramework.Parse(tfm),
            usePrerelease: false,
            package: out var packageMetadata
        );

        // Assert
        if (expected is null)
        {
            Assert.False(newerVersionIsAvailable);
        }
        else
        {
            Assert.True(newerVersionIsAvailable);
            Assert.Equal(expected, packageMetadata?.Version.ToString());
        }
    }

    [Theory]
    [InlineData("net6.0")]
    [InlineData("net7.0", "2.0.0")]
    [InlineData("net9.0", "3.0.0")]
    [InlineData("netstandard2.0", "3.0.0")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Naming",
        "CA1707:Identifiers should not contain underscores",
        Justification = "Test name"
    )]
    public void TryGetLatestComparedTo_Ignore_NetStandard(string tfm, string? expected = null)
    {
        // Arrange
        var package = new NuGetPackage(
            "package-id",
            [
                new PackageMetadata(
                    NuGetVersion.Parse("1.0.0"),
                    [
                        NuGetFramework.Parse("netstandard2.0"),
                        NuGetFramework.Parse("net6.0"),
                        NuGetFramework.Parse("net7.0"),
                    ],
                    null,
                    null
                ),
                new PackageMetadata(
                    NuGetVersion.Parse("2.0.0"),
                    [
                        NuGetFramework.Parse("netstandard2.0"),
                        NuGetFramework.Parse("net7.0"),
                        NuGetFramework.Parse("net8.0"),
                    ],
                    null,
                    null
                ),
                new PackageMetadata(
                    NuGetVersion.Parse("3.0.0"),
                    [NuGetFramework.Parse("netstandard2.0"), NuGetFramework.Parse("net9.0")],
                    null,
                    null
                ),
            ]
        );

        // Act
        var newerVersionIsAvailable = package.TryGetLatestComparedTo(
            version: NuGetVersion.Parse("1.0.0"),
            targetFramework: NuGetFramework.Parse(tfm),
            usePrerelease: false,
            package: out var packageMetadata
        );

        // Assert
        if (expected is null)
        {
            Assert.False(newerVersionIsAvailable);
        }
        else
        {
            Assert.True(newerVersionIsAvailable);
            Assert.Equal(expected, packageMetadata?.Version.ToString());
        }
    }
}
