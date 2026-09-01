using UpdatR.Internals;

namespace UpdatR.UnitTests;

public class UpdatRConfigTests
{
    [Fact]
    public void LoadReturnsNullWhenNoConfigFileExists()
    {
        // Arrange
        var temp = CreateTempDirectory();

        // Act
        var config = UpdatRConfig.Load(temp);

        // Assert
        Assert.Null(config);
    }

    [Fact]
    public void LoadReturnsConfigWhenFileIsNextToDirectoryPath()
    {
        // Arrange
        var temp = CreateTempDirectory();

        File.WriteAllText(
            Path.Combine(temp, UpdatRConfig.FileName),
            """
            {
              "excludePackages": ["Foo.*"],
              "allowedLicenses": ["MIT", "Apache-2.0"]
            }
            """
        );

        // Act
        var config = UpdatRConfig.Load(temp);

        // Assert
        Assert.NotNull(config);
        Assert.Equal(["Foo.*"], config.ExcludePackages ?? []);
        Assert.Equal(["MIT", "Apache-2.0"], config.AllowedLicenses ?? []);
    }

    [Fact]
    public void LoadReturnsConfigWhenFileIsNextToFilePath()
    {
        // Arrange
        var temp = CreateTempDirectory();
        var csproj = Path.Combine(temp, "Project.csproj");

        File.WriteAllText(csproj, "<Project />");
        File.WriteAllText(
            Path.Combine(temp, UpdatRConfig.FileName),
            """{ "excludePackages": ["Foo.*"] }"""
        );

        // Act
        var config = UpdatRConfig.Load(csproj);

        // Assert
        Assert.NotNull(config);
        Assert.Equal(["Foo.*"], config.ExcludePackages ?? []);
    }

    [Fact]
    public void LoadFallsBackToCurrentDirectoryWhenNoConfigNextToPath()
    {
        // Arrange
        var temp = CreateTempDirectory();
        var targetDir = Path.Combine(temp, "target");

        Directory.CreateDirectory(targetDir);

        File.WriteAllText(
            Path.Combine(temp, UpdatRConfig.FileName),
            """{ "allowedLicenses": ["MIT"] }"""
        );

        var originalCurrentDirectory = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(temp);

            // Act
            var config = UpdatRConfig.Load(targetDir);

            // Assert
            Assert.NotNull(config);
            Assert.Equal(["MIT"], config.AllowedLicenses ?? []);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCurrentDirectory);
        }
    }

    [Theory]
    [MemberData(nameof(MergeData))]
    public void Merge(string[]? fromArgs, string[]? fromConfig, string[]? expected)
    {
        // Act
        var result = UpdatRConfig.Merge(fromArgs, fromConfig);

        // Assert
        Assert.Equal(expected ?? [], result ?? []);
    }

    public static TheoryData<string[]?, string[]?, string[]?> MergeData =>
        new()
        {
            { null, null, null },
            { [], null, null },
            { null, ["Foo.*"], ["Foo.*"] },
            { ["Foo.*"], null, ["Foo.*"] },
            { ["Foo.*"], [], ["Foo.*"] },
            { ["Foo.*"], ["Bar.*"], ["Foo.*", "Bar.*"] },
            { ["Foo.*"], ["foo.*", "Bar.*"], ["Foo.*", "Bar.*"] },
        };

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "UpdatR-tests", Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(path);

        return path;
    }
}
