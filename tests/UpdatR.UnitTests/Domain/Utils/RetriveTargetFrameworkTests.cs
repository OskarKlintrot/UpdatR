using UpdatR.Domain.Utils;

namespace UpdatR.UnitTests;

public class RetriveTargetFrameworkTests
{
    [Fact]
    public void GetTargetFrameworksReturnsSingleTargetFramework()
    {
        // Arrange
        var path = CreateTempCsproj(
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """
        );

        // Act
        var targetFrameworks = RetriveTargetFramework.GetTargetFrameworks(path);

        // Assert
        Assert.Equal(["net9.0"], targetFrameworks);
    }

    [Fact]
    public void GetTargetFrameworksReturnsMultipleDistinctTargetFrameworks()
    {
        // Arrange
        var path = CreateTempCsproj(
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net8.0;net9.0;net9.0</TargetFrameworks>
              </PropertyGroup>
            </Project>
            """
        );

        // Act
        var targetFrameworks = RetriveTargetFramework.GetTargetFrameworks(path);

        // Assert
        Assert.Equal(["net8.0", "net9.0"], targetFrameworks);
    }

    [Fact]
    public void GetTargetFrameworksPrefersPluralOverSingular()
    {
        // Arrange - not valid MSBuild in practice, but exercises the "prefer plural" fallback
        // logic explicitly.
        var path = CreateTempCsproj(
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net8.0;net9.0</TargetFrameworks>
                <TargetFramework>net7.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """
        );

        // Act
        var targetFrameworks = RetriveTargetFramework.GetTargetFrameworks(path);

        // Assert
        Assert.Equal(["net8.0", "net9.0"], targetFrameworks);
    }

    [Fact]
    public void GetTargetFrameworksReturnsNullWhenNeitherIsDeclared()
    {
        // Arrange
        var path = CreateTempCsproj(
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
              </PropertyGroup>
            </Project>
            """
        );

        // Act
        var targetFrameworks = RetriveTargetFramework.GetTargetFrameworks(path);

        // Assert
        Assert.Null(targetFrameworks);
    }

    [Fact]
    public void GetTargetFrameworkReturnsFirstOfMultiple()
    {
        // Arrange
        var path = CreateTempCsproj(
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net8.0;net9.0</TargetFrameworks>
              </PropertyGroup>
            </Project>
            """
        );

        // Act
        var targetFramework = RetriveTargetFramework.GetTargetFramework(path);

        // Assert
        Assert.Equal("net8.0", targetFramework);
    }

    [Fact]
    public void ImportsFromAboveReturnsTrueWhenDirectoryBuildPropsImportsParent()
    {
        // Arrange
        var path = CreateTempFile(
            "Directory.Build.props",
            """
            <Project>
              <Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />
            </Project>
            """
        );

        // Act
        var result = RetriveTargetFramework.ImportsFromAbove(new FileInfo(path));

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ImportsFromAboveReturnsFalseWhenDirectoryBuildPropsDoesNotImportParent()
    {
        // Arrange
        var path = CreateTempFile(
            "Directory.Build.props",
            """
            <Project>
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """
        );

        // Act
        var result = RetriveTargetFramework.ImportsFromAbove(new FileInfo(path));

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void GetTargetFrameworksFromDirectoryBuildPropsFindsDeclarationInParentDirectory()
    {
        // Arrange - a leaf directory with a Directory.Build.props that imports the one above,
        // which declares the target framework.
        var root = CreateTempDirectory();
        var leaf = Directory.CreateDirectory(Path.Combine(root, "src", "Project")).FullName;

        File.WriteAllText(
            Path.Combine(root, "Directory.Build.props"),
            """
            <Project>
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """
        );

        File.WriteAllText(
            Path.Combine(leaf, "Directory.Build.props"),
            """
            <Project>
              <Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />
            </Project>
            """
        );

        // Act
        var targetFrameworks = RetriveTargetFramework.GetTargetFrameworksFromDirectoryBuildProps(
            new DirectoryInfo(leaf)
        );

        // Assert
        Assert.Equal(["net9.0"], targetFrameworks);
    }

    [Fact]
    public void GetTargetFrameworksFromDirectoryBuildPropsReturnsNullWhenNoneDeclaresIt()
    {
        // Arrange
        var root = CreateTempDirectory();

        // Act
        var targetFrameworks = RetriveTargetFramework.GetTargetFrameworksFromDirectoryBuildProps(
            new DirectoryInfo(root)
        );

        // Assert
        Assert.Null(targetFrameworks);
    }

    private static string CreateTempCsproj(string content) =>
        CreateTempFile("Test.csproj", content);

    private static string CreateTempFile(string fileName, string content)
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, fileName);

        File.WriteAllText(path, content);

        return path;
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        Directory.CreateDirectory(path);

        return path;
    }
}
