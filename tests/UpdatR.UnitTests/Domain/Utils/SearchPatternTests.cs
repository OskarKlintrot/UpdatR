using UpdatR.Domain.Utils;

namespace UpdatR.UnitTests;

public class SearchPatternTests
{
    [Theory]
    [InlineData("System.Text.Json", "System.Text.Json", true)]
    [InlineData("System.Text.Json", "SystemXTextYJson", false)]
    [InlineData("System.*", "System.Text.Json", true)]
    [InlineData("System.*", "Newtonsoft.Json", false)]
    [InlineData("*.Json", "Newtonsoft.Json", true)]
    [InlineData("*Test*", "MyTestPackage", true)]
    public void CreateSearchMatchesLiteralDotAsLiteral(string pattern, string input, bool expected)
    {
        // Act
        var search = SearchPattern.CreateSearch([pattern], treatNullOrEmptyAs: false);

        // Assert
        Assert.Equal(expected, search(input));
    }

    [Theory]
    [InlineData("Foo(Bar)")]
    [InlineData("Foo[Bar]")]
    [InlineData("Foo+Bar")]
    [InlineData("Foo?Bar")]
    [InlineData("Foo$Bar")]
    [InlineData("Foo^Bar")]
    [InlineData("Foo\\Bar")]
    public void ConvertToRegexDoesNotThrowForRegexMetaCharacters(string pattern)
    {
        // Act
        var exception = Record.Exception(() => SearchPattern.ConvertToRegex(pattern));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void ConvertToRegexTreatsMetaCharactersAsLiterals()
    {
        // Arrange
        var regex = SearchPattern.ConvertToRegex("Foo(Bar)");

        // Act
        var matchesLiteral = regex.IsMatch("Foo(Bar)");
        var matchesWithoutParens = regex.IsMatch("FooBar");

        // Assert
        Assert.True(matchesLiteral);
        Assert.False(matchesWithoutParens);
    }

    [Fact]
    public void CreateSearchIsCaseInsensitive()
    {
        // Act
        var search = SearchPattern.CreateSearch(["system.text.json"], treatNullOrEmptyAs: false);

        // Assert
        Assert.True(search("System.Text.Json"));
    }

    [Fact]
    public void CreateSearchReturnsTreatNullOrEmptyAsWhenPatternsIsNull()
    {
        // Act
        var search = SearchPattern.CreateSearch(null, treatNullOrEmptyAs: true);

        // Assert
        Assert.True(search("Anything"));
    }

    [Fact]
    public void CreateSearchReturnsTreatNullOrEmptyAsWhenPatternsIsEmpty()
    {
        // Act
        var search = SearchPattern.CreateSearch([], treatNullOrEmptyAs: true);

        // Assert
        Assert.True(search("Anything"));
    }

    [Fact]
    public void CreateSearchMatchesAnyOfMultiplePatterns()
    {
        // Act
        var search = SearchPattern.CreateSearch(["Foo", "Bar*"], treatNullOrEmptyAs: false);

        // Assert
        Assert.True(search("Foo"));
        Assert.True(search("BarBaz"));
        Assert.False(search("Baz"));
    }

    [Fact]
    public void ConvertToRegexMatchesExactStringWithoutWildcard()
    {
        // Arrange
        var regex = SearchPattern.ConvertToRegex("Exact.Match");

        // Act
        var matchesExact = regex.IsMatch("Exact.Match");
        var matchesWithSuffix = regex.IsMatch("Exact.Match.Extra");
        var matchesWithoutDot = regex.IsMatch("ExactAMatch");

        // Assert
        Assert.True(matchesExact);
        Assert.False(matchesWithSuffix);
        Assert.False(matchesWithoutDot);
    }

    [Fact]
    public void ConvertToRegexWithOnlyWildcardMatchesEverything()
    {
        // Arrange
        var regex = SearchPattern.ConvertToRegex("*");

        // Act
        var matchesAnything = regex.IsMatch("Anything");
        var matchesEmpty = regex.IsMatch(string.Empty);

        // Assert
        Assert.True(matchesAnything);
        Assert.True(matchesEmpty);
    }
}
