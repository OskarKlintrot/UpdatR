using NuGet.Frameworks;
using NuGet.Versioning;
using UpdatR.Domain.Utils;

namespace UpdatR.UnitTests;

public class TfmAlignmentTests
{
    [Fact]
    public void ResolveAlignMajorReturnsLowestMajorAmongModernNetCoreAppTfms()
    {
        // Arrange
        var tfms = new[]
        {
            NuGetFramework.Parse("net10.0"),
            NuGetFramework.Parse("net9.0"),
            NuGetFramework.Parse("net8.0"),
        };

        // Act
        var result = TfmAlignment.ResolveAlignMajor(tfms);

        // Assert
        Assert.Equal(8, result);
    }

    [Fact]
    public void ResolveAlignMajorReturnsNullWhenNoModernNetCoreAppTfm()
    {
        // Arrange - only legacy .NET Framework / .NET Standard, plus pre-net5.0 .NETCoreApp.
        var tfms = new[]
        {
            NuGetFramework.Parse("net48"),
            NuGetFramework.Parse("netstandard2.0"),
            NuGetFramework.Parse("netcoreapp3.1"),
        };

        // Act
        var result = TfmAlignment.ResolveAlignMajor(tfms);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ResolveAlignMajorIgnoresNonNetCoreAppTfmsWhenModernTfmIsPresent()
    {
        // Arrange
        var tfms = new[] { NuGetFramework.Parse("net9.0"), NuGetFramework.Parse("net48") };

        // Act
        var result = TfmAlignment.ResolveAlignMajor(tfms);

        // Assert
        Assert.Equal(9, result);
    }

    [Fact]
    public void ResolveAlignMajorReturnsNullForEmptyCollection()
    {
        // Act
        var result = TfmAlignment.ResolveAlignMajor([]);

        // Assert
        Assert.Null(result);
    }

    [Theory]
    [InlineData(9, "9.0.0", 9)]
    [InlineData(9, "9.5.0", 9)]
    [InlineData(9, "10.0.0", null)]
    [InlineData(null, "9.0.0", null)]
    public void ResolveMaxMajorCapsOnlyWhenInstalledVersionIsNotAheadOfAlignMajor(
        int? alignMajor,
        string installedVersion,
        int? expected
    )
    {
        // Act
        var result = TfmAlignment.ResolveMaxMajor(alignMajor, NuGetVersion.Parse(installedVersion));

        // Assert
        Assert.Equal(expected, result);
    }
}
