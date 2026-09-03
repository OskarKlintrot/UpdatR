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
              "allowedLicenses": ["MIT", "Apache-2.0"],
              "defaultTarget": "src/Foo.sln",
              "excludeFiles": ["tests/**/*"],
              "alignWithTfm": ["Microsoft.Extensions.*"]
            }
            """
        );

        // Act
        var config = UpdatRConfig.Load(temp);

        // Assert
        Assert.NotNull(config);
        Assert.Equal(["Foo.*"], config.ExcludePackages ?? []);
        Assert.Equal(["MIT", "Apache-2.0"], config.AllowedLicenses ?? []);
        Assert.Equal("src/Foo.sln", config.DefaultTarget);
        Assert.Equal(["tests/**/*"], config.ExcludeFiles ?? []);
        Assert.Equal(["Microsoft.Extensions.*"], config.AlignWithTfm ?? []);
    }

    [Fact]
    public void LoadReturnsConfigWhenFileContainsComments()
    {
        // Arrange
        var temp = CreateTempDirectory();

        File.WriteAllText(
            Path.Combine(temp, UpdatRConfig.FileName),
            """
            {
              // Line comment.
              "excludePackages": ["Foo.*"], // Trailing line comment.
              /* Block comment. */
              "allowedLicenses": ["MIT", "Apache-2.0"],
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

    [Fact]
    public void CreateFileCreatesFileWithAllPropertiesEmpty()
    {
        // Arrange
        var temp = CreateTempDirectory();

        // Act
        var filePath = UpdatRConfig.CreateFile(temp);

        // Assert
        Assert.Equal(Path.Combine(temp, UpdatRConfig.FileName), filePath);

        var config = UpdatRConfig.Load(temp);

        Assert.NotNull(config);
        Assert.Empty(config.ExcludePackages ?? []);
        Assert.Empty(config.AllowedLicenses ?? []);
        Assert.Null(config.DefaultTarget);
        Assert.Empty(config.ExcludeFiles ?? []);
        Assert.Empty(config.AlignWithTfm ?? []);
    }

    [Fact]
    public void CreateFileThrowsWhenFileAlreadyExistsAndNotOverwriting()
    {
        // Arrange
        var temp = CreateTempDirectory();

        UpdatRConfig.CreateFile(temp);

        // Act & Assert
        Assert.Throws<IOException>(() => UpdatRConfig.CreateFile(temp));
    }

    [Fact]
    public void CreateFileOverwritesWhenOverwriteIsTrue()
    {
        // Arrange
        var temp = CreateTempDirectory();

        var filePath = UpdatRConfig.CreateFile(temp);

        File.WriteAllText(filePath, """{ "excludePackages": ["Foo.*"] }""");

        // Act
        UpdatRConfig.CreateFile(temp, overwrite: true);

        // Assert
        var config = UpdatRConfig.Load(temp);

        Assert.NotNull(config);
        Assert.Empty(config.ExcludePackages ?? []);
    }

    [Theory]
    [InlineData(
        """{ "excludePackages": ["Foo.*"], "allowedLicenses": ["MIT"], "defaultTarget": "src/Foo.sln", "excludeFiles": ["tests/*"], "alignWithTfm": ["Microsoft.Extensions.*"] }"""
    )]
    [InlineData("{}")]
    [InlineData(
        """{ "excludePackages": null, "allowedLicenses": null, "defaultTarget": null, "excludeFiles": null, "alignWithTfm": null }"""
    )]
    public void ValidateReturnsNoErrorsForValidJson(string json)
    {
        // Act
        var errors = UpdatRConfig.Validate(json);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateReturnsNoErrorsForJsonWithCommentsAndTrailingCommas()
    {
        // Act
        var errors = UpdatRConfig.Validate(
            """
            {
              // Line comment.
              "excludePackages": ["Foo.*"], // Trailing line comment.
              /* Block comment. */
              "allowedLicenses": ["MIT"],
            }
            """
        );

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateReturnsErrorForInvalidJson()
    {
        // Act
        var errors = UpdatRConfig.Validate("{ invalid json");

        // Assert
        Assert.Single(errors);
        Assert.Contains("not valid JSON", errors[0]);
    }

    [Fact]
    public void ValidateReturnsErrorWhenRootIsNotAnObject()
    {
        // Act
        var errors = UpdatRConfig.Validate("[]");

        // Assert
        Assert.Single(errors);
        Assert.Contains("must contain a JSON object", errors[0]);
    }

    [Fact]
    public void ValidateReturnsErrorForUnknownProperty()
    {
        // Act
        var errors = UpdatRConfig.Validate("""{ "unknownProperty": [] }""");

        // Assert
        Assert.Single(errors);
        Assert.Contains("Unknown option 'unknownProperty'", errors[0]);
    }

    [Theory]
    [InlineData("""{ "excludePackages": "Foo.*" }""")]
    [InlineData("""{ "excludePackages": [1] }""")]
    [InlineData("""{ "excludePackages": [""] }""")]
    [InlineData("""{ "excludePackages": ["  "] }""")]
    public void ValidateReturnsErrorForInvalidExcludePackagesValue(string json)
    {
        // Act
        var errors = UpdatRConfig.Validate(json);

        // Assert
        Assert.Single(errors);
    }

    [Theory]
    [InlineData("""{ "defaultTarget": [] }""")]
    [InlineData("""{ "defaultTarget": "" }""")]
    [InlineData("""{ "defaultTarget": "  " }""")]
    public void ValidateReturnsErrorForInvalidDefaultTargetValue(string json)
    {
        // Act
        var errors = UpdatRConfig.Validate(json);

        // Assert
        Assert.Single(errors);
    }

    [Fact]
    public void ValidateReturnsErrorWhenDefaultTargetDoesNotExist()
    {
        // Arrange
        var configDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        Directory.CreateDirectory(configDirectory);

        try
        {
            // Act
            var errors = UpdatRConfig.Validate(
                """{ "defaultTarget": "does-not-exist" }""",
                configDirectory
            );

            // Assert
            Assert.Single(errors);
            Assert.Contains("defaultTarget", errors[0]);
            Assert.Contains("does not exist", errors[0]);
        }
        finally
        {
            Directory.Delete(configDirectory, recursive: true);
        }
    }

    [Fact]
    public void ValidateReturnsNoErrorWhenDefaultTargetExists()
    {
        // Arrange
        var configDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        Directory.CreateDirectory(configDirectory);
        Directory.CreateDirectory(Path.Combine(configDirectory, "src"));

        try
        {
            // Act
            var errors = UpdatRConfig.Validate("""{ "defaultTarget": "src" }""", configDirectory);

            // Assert
            Assert.Empty(errors);
        }
        finally
        {
            Directory.Delete(configDirectory, recursive: true);
        }
    }

    [Theory]
    [InlineData("""{ "excludeFiles": "Foo/*" }""")]
    [InlineData("""{ "excludeFiles": [1] }""")]
    [InlineData("""{ "excludeFiles": [""] }""")]
    [InlineData("""{ "excludeFiles": ["  "] }""")]
    public void ValidateReturnsErrorForInvalidExcludeFilesValue(string json)
    {
        // Act
        var errors = UpdatRConfig.Validate(json);

        // Assert
        Assert.Single(errors);
    }

    [Theory]
    [InlineData("""{ "alignWithTfm": "Microsoft.*" }""")]
    [InlineData("""{ "alignWithTfm": [1] }""")]
    [InlineData("""{ "alignWithTfm": [""] }""")]
    [InlineData("""{ "alignWithTfm": ["  "] }""")]
    public void ValidateReturnsErrorForInvalidAlignWithTfmValue(string json)
    {
        // Act
        var errors = UpdatRConfig.Validate(json);

        // Assert
        Assert.Single(errors);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "UpdatR-tests", Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(path);

        return path;
    }
}
