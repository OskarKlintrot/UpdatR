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
    [InlineData("net9.0", "3.0.0")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Naming",
        "CA1707:Identifiers should not contain underscores",
        Justification = "Test name"
    )]
    public void TryGetLatestComparedTo_HtmlAgilityPack(string tfm, string? expected = null)
    {
        // Arrange
        var package = new NuGetPackage(
            "package-id",
            [
                new PackageMetadata(
                    NuGetVersion.Parse("3.0.0"),
                    [
                        NuGetFramework.Parse("netstandard1.3"),
                        NuGetFramework.Parse("v4.0"),
                        NuGetFramework.Parse("v4.5"),
                        NuGetFramework.Parse("netstandard1.6"),
                        NuGetFramework.Parse("netstandard2.0"),
                    ],
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

    [Theory]
    [InlineData(null, "1.0.0", "2.0.0")]
    [InlineData(new string[0], "1.0.0", "2.0.0")]
    [InlineData(new[] { "MIT" }, "1.0.0", null)]
    [InlineData(new[] { "mit" }, "1.0.0", null)]
    [InlineData(new[] { "Apache-2.0" }, "1.0.0", "2.0.0")]
    [InlineData(new[] { "Apache-2.0" }, "2.0.0", null)]
    [InlineData(new[] { "GPL-3.0" }, "1.0.0", null)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Naming",
        "CA1707:Identifiers should not contain underscores",
        Justification = "Test name"
    )]
    public void TryGetLatestComparedTo_With_AllowedLicenses(
        string[]? allowedLicenses,
        string comparedTo,
        string? expected
    )
    {
        // Arrange
        var package = new NuGetPackage(
            "package-id",
            [
                new PackageMetadata(
                    NuGetVersion.Parse("1.0.0"),
                    [NuGetFramework.Parse("net9.0")],
                    null,
                    null,
                    "MIT"
                ),
                new PackageMetadata(
                    NuGetVersion.Parse("2.0.0"),
                    [NuGetFramework.Parse("net9.0")],
                    null,
                    null,
                    "Apache-2.0"
                ),
            ]
        );

        // Act
        var newerVersionIsAvailable = package.TryGetLatestComparedTo(
            version: NuGetVersion.Parse(comparedTo),
            targetFramework: NuGetFramework.Parse("net9.0"),
            usePrerelease: false,
            package: out var packageMetadata,
            allowedLicenses: allowedLicenses
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

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Naming",
        "CA1707:Identifiers should not contain underscores",
        Justification = "Test name"
    )]
    public void TryGetLatestComparedTo_With_AllowedLicenses_NoLicenseInfo_IsAlwaysAllowed()
    {
        // Arrange - fail-open: a version without any license information should be updatable to,
        // even though it doesn't match any of the allowed licenses.
        var package = new NuGetPackage(
            "package-id",
            [
                new PackageMetadata(
                    NuGetVersion.Parse("1.0.0"),
                    [NuGetFramework.Parse("net9.0")],
                    null,
                    null,
                    "Apache-2.0"
                ),
                new PackageMetadata(
                    NuGetVersion.Parse("2.0.0"),
                    [NuGetFramework.Parse("net9.0")],
                    null,
                    null,
                    null
                ),
            ]
        );

        // Act
        var newerVersionIsAvailable = package.TryGetLatestComparedTo(
            version: NuGetVersion.Parse("1.0.0"),
            targetFramework: NuGetFramework.Parse("net9.0"),
            usePrerelease: false,
            package: out var packageMetadata,
            allowedLicenses: ["MIT"]
        );

        // Assert
        Assert.True(newerVersionIsAvailable);
        Assert.Equal("2.0.0", packageMetadata?.Version.ToString());
    }

    [Theory]
    [InlineData(null, "MIT", true)]
    [InlineData(new string[0], "MIT", true)]
    [InlineData(new[] { "MIT" }, "MIT", true)]
    [InlineData(new[] { "MIT" }, "mit or apache-2.0", true)]
    [InlineData(new[] { "MIT" }, "Apache-2.0", false)]
    [InlineData(new[] { "MIT" }, null, true)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Naming",
        "CA1707:Identifiers should not contain underscores",
        Justification = "Test name"
    )]
    public void IsLicenseAllowed_Static(
        string[]? allowedLicenses,
        string? licenseExpression,
        bool expected
    )
    {
        // Arrange
        var metadata = new PackageMetadata(
            NuGetVersion.Parse("1.0.0"),
            [NuGetFramework.Parse("net9.0")],
            null,
            null,
            licenseExpression
        );

        // Act
        var result = NuGetPackage.IsLicenseAllowed(metadata, allowedLicenses);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null, "1.0.0", true)]
    [InlineData(new[] { "MIT" }, "1.0.0", true)]
    [InlineData(new[] { "MIT" }, "2.0.0", false)]
    [InlineData(new[] { "MIT" }, "3.0.0", true)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Naming",
        "CA1707:Identifiers should not contain underscores",
        Justification = "Test name"
    )]
    public void IsLicenseAllowed_Instance(string[]? allowedLicenses, string version, bool expected)
    {
        // Arrange
        var package = new NuGetPackage(
            "package-id",
            [
                new PackageMetadata(
                    NuGetVersion.Parse("1.0.0"),
                    [NuGetFramework.Parse("net9.0")],
                    null,
                    null,
                    "MIT"
                ),
                new PackageMetadata(
                    NuGetVersion.Parse("2.0.0"),
                    [NuGetFramework.Parse("net9.0")],
                    null,
                    null,
                    "Apache-2.0"
                ),
                new PackageMetadata(
                    NuGetVersion.Parse("3.0.0"),
                    [NuGetFramework.Parse("net9.0")],
                    null,
                    null,
                    null
                ),
            ]
        );

        // Act
        var result = package.IsLicenseAllowed(NuGetVersion.Parse(version), allowedLicenses);

        // Assert
        Assert.Equal(expected, result);
    }
}
