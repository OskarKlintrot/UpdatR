using UpdatR.Update.Formatters;

namespace UpdatR.Update.UnitTests;

[UsesVerify]
public class MarkdownFormatterTests
{
    [Fact]
    public Task NothingToReport()
    {
        // Arrange
        var summary = Summary.Create(new Internals.Result(Path.GetTempPath()));

        //Act
        var md = MarkdownFormatter.Generate(summary);

        // Assert
        return Verify(md);
    }
}
