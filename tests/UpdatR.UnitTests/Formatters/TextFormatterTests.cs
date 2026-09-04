using UpdatR.Formatters;
using UpdatR.Internals;

namespace UpdatR.UnitTests;

public class TextFormatterTests
{
    [Fact]
    public Task EmptyResult()
    {
        // Arrange
        var summary = Summary.Create(new Result(Path.GetTempPath()));

        // Act
        var text = TextFormatter.PlainText(summary);

        // Assert
        return Verify(text);
    }

    [Fact]
    public Task KitchenSink()
    {
        // Arrange
        var root = Path.GetTempPath();

        var project = new ProjectBuilder(root)
            .WithUpdatedPackage("Updated.Package", "1.0.0", "2.0.0")
            .WithUnknownPackages("Unknown.Package")
            .WithDeprecatedPackage("Deprecated.Package", "1.2.3", "Old and deprecated package.")
            .WithVulnerablePackage(
                "Vulnerable.Package",
                "1.2.3",
                new PackageVulnerabilityMetadata(new Uri("https://google.com"), 1)
            )
            .WithSkippedUpdatePackage(
                "Skipped.Package",
                "2.0.0",
                SkippedUpdateReason.AlignedWithTfm
            )
            .Build();

        var result = new ResultBuilder(root)
            .WithProject(project)
            .WithUnauthorizedSources("Unauthorized source", "https://google.com")
            .Build();

        var summary = Summary.Create(result, FailOn.Outdated);

        // Act
        var text = TextFormatter.PlainText(summary);

        // Assert
        return Verify(text);
    }
}
