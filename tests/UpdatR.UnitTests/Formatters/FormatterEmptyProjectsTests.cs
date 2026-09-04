using NuGet.Versioning;
using UpdatR.Formatters;

namespace UpdatR.UnitTests;

/// <summary>
/// Regression tests for the empty-<c>Projects</c> guard in both formatters' deprecated-packages
/// column-padding calculation (previously <c>OrderByDescending(...).First()</c> on an empty
/// sequence, which throws <see cref="InvalidOperationException"/>).
/// </summary>
public class FormatterEmptyProjectsTests
{
    private static Summary CreateSummaryWithDeprecatedPackageWithoutProjects() =>
        new(
            unknownPackages: new Dictionary<string, IEnumerable<string>>(),
            unauthorizedSources: [],
            updatedPackages: [],
            deprecatedPackages:
            [
                new DeprecatedPackage(
                    "Deprecated.Package",
                    [
                        (
                            new DeprecatedVersion(
                                NuGetVersion.Parse("1.2.3"),
                                new PackageDeprecationMetadata(
                                    "Old and deprecated package.",
                                    ["Legacy"],
                                    null
                                )
                            ),
                            [] // No projects reported for this version.
                        ),
                    ]
                ),
            ],
            vulnerablePackages: [],
            licenseMismatchPackages: [],
            unsupportedRangePackages: [],
            skippedUpdatePackages: []
        );

    [Fact]
    public void MarkdownFormatterGenerateDoesNotThrowWhenDeprecatedPackageHasNoProjects()
    {
        // Arrange
        var summary = CreateSummaryWithDeprecatedPackageWithoutProjects();

        // Act
        var exception = Record.Exception(() => MarkdownFormatter.Generate(summary));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void TextFormatterGenerateDoesNotThrowWhenDeprecatedPackageHasNoProjects()
    {
        // Arrange
        var summary = CreateSummaryWithDeprecatedPackageWithoutProjects();

        // Act
        var exception = Record.Exception(() => TextFormatter.PlainText(summary));

        // Assert
        Assert.Null(exception);
    }
}
