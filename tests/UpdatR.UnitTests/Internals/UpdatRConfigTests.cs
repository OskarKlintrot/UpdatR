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
              "path": "src/Foo.sln",
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
        Assert.Equal("src/Foo.sln", config.Path);
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
        Assert.Equal(UpdatRConfig.SchemaUrl, config.Schema);
        Assert.Empty(config.ExcludePackages ?? []);
        Assert.Empty(config.AllowedLicenses ?? []);
        Assert.Null(config.Path);
        Assert.Empty(config.ExcludeFiles ?? []);
        Assert.Empty(config.AlignWithTfm ?? []);
        Assert.Null(config.FailOn);
        Assert.Empty(config.ToolPackagePins ?? []);
        Assert.Empty(config.PackagePolicies ?? []);
    }

    [Fact]
    public void CreateFileWithExampleCreatesFileWithPopulatedValues()
    {
        // Arrange
        var temp = CreateTempDirectory();

        // Act
        var filePath = UpdatRConfig.CreateFile(temp, example: true);

        // Assert
        Assert.Equal(Path.Combine(temp, UpdatRConfig.FileName), filePath);

        var config = UpdatRConfig.Load(temp);

        Assert.NotNull(config);
        Assert.Equal(UpdatRConfig.SchemaUrl, config.Schema);
        Assert.Equal(["Microsoft.CodeAnalysis.*"], config.ExcludePackages ?? []);
        Assert.Equal(
            [
                "Microsoft.EntityFrameworkCore",
                "Microsoft.EntityFrameworkCore.*",
                "Microsoft.Extensions.*",
                "System.Net.Http.Json",
            ],
            config.AlignWithTfm ?? []
        );
        Assert.Equal(["dotnet-ef"], config.ToolPackagePins?.Select(x => x.Tool).ToArray() ?? []);
        Assert.Equal(
            ["Microsoft.EntityFrameworkCore"],
            config.ToolPackagePins?.Select(x => x.Package).ToArray() ?? []
        );
        Assert.Empty(UpdatRConfig.Validate(File.ReadAllText(filePath)));
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
        """{ "excludePackages": ["Foo.*"], "allowedLicenses": ["MIT"], "path": "src/Foo.sln", "excludeFiles": ["tests/*"], "alignWithTfm": ["Microsoft.Extensions.*"] }"""
    )]
    [InlineData("{}")]
    [InlineData(
        """{ "excludePackages": null, "allowedLicenses": null, "path": null, "excludeFiles": null, "alignWithTfm": null }"""
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
    [InlineData("""{ "$schema": "https://example.com/schema.json" }""")]
    [InlineData("""{ "$schema": null }""")]
    public void ValidateReturnsNoErrorsForValidSchemaValue(string json)
    {
        // Act
        var errors = UpdatRConfig.Validate(json);

        // Assert
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("""{ "$schema": "" }""")]
    [InlineData("""{ "$schema": [] }""")]
    public void ValidateReturnsErrorForInvalidSchemaValue(string json)
    {
        // Act
        var errors = UpdatRConfig.Validate(json);

        // Assert
        Assert.Single(errors);
    }

    [Fact]
    public void ValidateReturnsNoErrorForSchemaWithConfigDirectory()
    {
        // Arrange
        var configDirectory = CreateTempDirectory();

        try
        {
            // Act
            var errors = UpdatRConfig.Validate(
                """{ "$schema": "https://example.com/schema.json" }""",
                configDirectory
            );

            // Assert
            Assert.Empty(errors);
        }
        finally
        {
            Directory.Delete(configDirectory, recursive: true);
        }
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
    [InlineData("""{ "path": [] }""")]
    [InlineData("""{ "path": "" }""")]
    [InlineData("""{ "path": "  " }""")]
    public void ValidateReturnsErrorForInvalidPathValue(string json)
    {
        // Act
        var errors = UpdatRConfig.Validate(json);

        // Assert
        Assert.Single(errors);
    }

    [Fact]
    public void ValidateReturnsErrorWhenPathDoesNotExist()
    {
        // Arrange
        var configDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        Directory.CreateDirectory(configDirectory);

        try
        {
            // Act
            var errors = UpdatRConfig.Validate("""{ "path": "does-not-exist" }""", configDirectory);

            // Assert
            Assert.Single(errors);
            Assert.Contains("path", errors[0]);
            Assert.Contains("does not exist", errors[0]);
        }
        finally
        {
            Directory.Delete(configDirectory, recursive: true);
        }
    }

    [Fact]
    public void ValidateReturnsNoErrorWhenPathExists()
    {
        // Arrange
        var configDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        Directory.CreateDirectory(configDirectory);
        Directory.CreateDirectory(Path.Combine(configDirectory, "src"));

        try
        {
            // Act
            var errors = UpdatRConfig.Validate("""{ "path": "src" }""", configDirectory);

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

    [Theory]
    [InlineData("""{ "packagePolicies": [] }""")]
    [InlineData("""{ "packagePolicies": null }""")]
    [InlineData("""{ "packagePolicies": [{ "package": "Serilog*", "maxMajor": 3 }] }""")]
    [InlineData(
        """
            {
              "packagePolicies": [
                { "package": "Serilog*", "maxMajor": 3 },
                { "package": "Foo", "maxMajor": 0 }
              ]
            }
            """
    )]
    public void ValidateReturnsNoErrorsForValidPackagePoliciesValue(string json)
    {
        // Act
        var errors = UpdatRConfig.Validate(json);

        // Assert
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("""{ "packagePolicies": "not-an-array" }""")]
    [InlineData("""{ "packagePolicies": ["not-an-object"] }""")]
    [InlineData("""{ "packagePolicies": [{ "maxMajor": 3 }] }""")]
    [InlineData("""{ "packagePolicies": [{ "package": "" , "maxMajor": 3 }] }""")]
    [InlineData("""{ "packagePolicies": [{ "package": "Serilog*" }] }""")]
    [InlineData(
        """{ "packagePolicies": [{ "package": "Serilog*", "maxMajor": "not-a-number" }] }"""
    )]
    [InlineData("""{ "packagePolicies": [{ "package": "Serilog*", "maxMajor": -1 }] }""")]
    public void ValidateReturnsErrorForInvalidPackagePoliciesValue(string json)
    {
        // Act
        var errors = UpdatRConfig.Validate(json);

        // Assert
        Assert.NotEmpty(errors);
    }

    [Theory]
    [InlineData("""{ "failOn": "outdated" }""")]
    [InlineData("""{ "failOn": "Outdated" }""")]
    [InlineData("""{ "failOn": "deprecated" }""")]
    [InlineData("""{ "failOn": "vulnerable" }""")]
    [InlineData("""{ "failOn": "none" }""")]
    [InlineData("""{ "failOn": null }""")]
    public void ValidateReturnsNoErrorsForValidFailOnValue(string json)
    {
        // Act
        var errors = UpdatRConfig.Validate(json);

        // Assert
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("""{ "failOn": "not-a-real-value" }""")]
    [InlineData("""{ "failOn": 1 }""")]
    [InlineData("""{ "failOn": [] }""")]
    public void ValidateReturnsErrorForInvalidFailOnValue(string json)
    {
        // Act
        var errors = UpdatRConfig.Validate(json);

        // Assert
        Assert.Single(errors);
    }

    [Theory]
    [InlineData("""{ "failOnIncomplete": true }""")]
    [InlineData("""{ "failOnIncomplete": false }""")]
    [InlineData("""{ "failOnIncomplete": null }""")]
    public void ValidateReturnsNoErrorsForValidFailOnIncompleteValue(string json)
    {
        // Act
        var errors = UpdatRConfig.Validate(json);

        // Assert
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("""{ "failOnIncomplete": "true" }""")]
    [InlineData("""{ "failOnIncomplete": 1 }""")]
    [InlineData("""{ "failOnIncomplete": [] }""")]
    public void ValidateReturnsErrorForInvalidFailOnIncompleteValue(string json)
    {
        // Act
        var errors = UpdatRConfig.Validate(json);

        // Assert
        Assert.Single(errors);
    }

    [Fact]
    public void ParseFailOnReturnsNullForNullOrEmpty()
    {
        // Act & Assert
        Assert.Null(UpdatRConfig.ParseFailOn(null));
        Assert.Null(UpdatRConfig.ParseFailOn(""));
        Assert.Null(UpdatRConfig.ParseFailOn("  "));
    }

    [Theory]
    [InlineData("outdated", FailOn.Outdated)]
    [InlineData("Deprecated", FailOn.Deprecated)]
    [InlineData("VULNERABLE", FailOn.Vulnerable)]
    [InlineData("none", FailOn.None)]
    public void ParseFailOnParsesCaseInsensitively(string value, FailOn expected)
    {
        // Act
        var result = UpdatRConfig.ParseFailOn(value);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseFailOnThrowsForUnknownValue()
    {
        // Act & Assert
        Assert.Throws<UpdatRException>(() => UpdatRConfig.ParseFailOn("not-a-real-value"));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "UpdatR-tests", Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(path);

        return path;
    }
}
