using UpdatR.Internals;

namespace UpdatR.UnitTests;

public class ResultTests
{
    [Fact]
    public void TryAddProjectNormalizesPathToRelativeWithForwardSlashes()
    {
        // Arrange - reported paths should always use '/' regardless of OS, and be relative to
        // the root, even on Windows where Path.Combine would otherwise produce '\'-separated
        // paths.
        var root = CreateTempDirectory();
        var projectPath = Path.Combine(root, "src", "Project", "Project.csproj");

        Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
        File.WriteAllText(projectPath, string.Empty);

        var result = new Result(root);

        // Act
        var added = result.TryAddProject(new ProjectWithPackages(projectPath));

        // Assert
        Assert.True(added);

        var project = Assert.Single(result.Projects);

        Assert.Equal("src/Project/Project.csproj", project.Path);
        Assert.DoesNotContain('\\', project.Path);
    }

    [Fact]
    public void TryAddProjectReturnsFalseForDuplicatePath()
    {
        // Arrange
        var root = CreateTempDirectory();
        var projectPath = Path.Combine(root, "Project.csproj");

        File.WriteAllText(projectPath, string.Empty);

        var result = new Result(root);

        result.TryAddProject(new ProjectWithPackages(projectPath));

        // Act
        var added = result.TryAddProject(new ProjectWithPackages(projectPath));

        // Assert
        Assert.False(added);
        Assert.Single(result.Projects);
    }

    [Fact]
    public void TryAddProjectCollectsUnknownPackagesAcrossProjects()
    {
        // Arrange
        var root = CreateTempDirectory();
        var project1Path = Path.Combine(root, "Project1.csproj");
        var project2Path = Path.Combine(root, "Project2.csproj");

        File.WriteAllText(project1Path, string.Empty);
        File.WriteAllText(project2Path, string.Empty);

        var project1 = new ProjectWithPackages(project1Path);
        project1.AddUnknownPackage("Unknown.Package");

        var project2 = new ProjectWithPackages(project2Path);
        project2.AddUnknownPackage("Unknown.Package");

        var result = new Result(root);

        // Act
        result.TryAddProject(project1);
        result.TryAddProject(project2);

        // Assert
        var projects = Assert.Single(result.UnknownPackages).Value.ToArray();

        Assert.Equal(["Project1.csproj", "Project2.csproj"], projects);
    }

    [Fact]
    public void ConstructorAcceptsFilePathAndUsesItsDirectoryAsRoot()
    {
        // Arrange
        var root = CreateTempDirectory();
        var filePath = Path.Combine(root, "Project.csproj");

        File.WriteAllText(filePath, string.Empty);

        // Act
        var result = new Result(filePath);

        result.TryAddProject(new ProjectWithPackages(filePath));

        // Assert
        Assert.Equal("Project.csproj", Assert.Single(result.Projects).Path);
    }

    [Fact]
    public void ConstructorThrowsWhenRootDoesNotExist()
    {
        // Arrange
        var nonExistentPath = Path.Combine(
            Path.GetTempPath(),
            Path.GetRandomFileName(),
            "does-not-exist"
        );

        // Act & Assert
        Assert.Throws<ArgumentException>(() => new Result(nonExistentPath));
    }

    [Fact]
    public void TryAddUnauthorizedSourceReturnsFalseForDuplicateName()
    {
        // Arrange
        var result = new Result(CreateTempDirectory());

        result.TryAddUnauthorizedSource("MySource", "https://example.com/v1");

        // Act
        var added = result.TryAddUnauthorizedSource("MySource", "https://example.com/v2");

        // Assert
        Assert.False(added);
        Assert.Single(result.UnauthorizedSources);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        Directory.CreateDirectory(path);

        return path;
    }
}
