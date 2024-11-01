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
}
