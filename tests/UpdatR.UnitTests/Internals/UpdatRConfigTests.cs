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
              "excludeFiles": ["tests/**/*"]
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
        """{ "excludePackages": ["Foo.*"], "allowedLicenses": ["MIT"], "defaultTarget": "src/Foo.sln", "excludeFiles": ["tests/*"] }"""
    )]
    [InlineData("{}")]
    [InlineData(
        """{ "excludePackages": null, "allowedLicenses": null, "defaultTarget": null, "excludeFiles": null }"""
    )]
    public void ValidateReturnsNoErrorsForValidJson(string json)
    {
        // Act
        var errors = UpdatRConfig.Validate(json);

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

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "UpdatR-tests", Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(path);

        return path;
    }
}
