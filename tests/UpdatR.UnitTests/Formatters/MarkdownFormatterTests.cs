using NuGet.Versioning;
using UpdatR.Formatters;
using UpdatR.Internals;

namespace UpdatR.UnitTests;

public class MarkdownFormatterTests
{
    [Theory]
    [MemberData(nameof(GetMethods))]
    public Task EmptyResults(string method)
    {
        // Arrange
        var methodInfo = typeof(MarkdownFormatter).GetMethod(
            method,
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public
        )!;
        var summary = Summary.Create(new Result(Path.GetTempPath()));

        // Act
        var md = methodInfo.Invoke(null, new object[] { summary });

        // Assert
        return Verify(md).UseParameters(method);
    }

    [Theory]
    [MemberData(nameof(GetMethods))]
    public Task NothingToReport(string method)
    {
        // Arrange
        var methodInfo = typeof(MarkdownFormatter).GetMethod(
            method,
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public
        )!;
        var root = Path.GetTempPath();

        var project = new ProjectBuilder(root).Build();

        var result = new ResultBuilder(root).WithProject(project).Build();

        var summary = Summary.Create(result);

        // Act
        var md = methodInfo.Invoke(null, new object[] { summary });

        // Assert
        return Verify(md).UseParameters(method);
    }

    [Theory]
    [MemberData(nameof(GetMethods))]
    public Task OneUpdatedPackage(string method)
    {
        // Arrange
        var methodInfo = typeof(MarkdownFormatter).GetMethod(
            method,
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public
        )!;
        var root = Path.GetTempPath();

        var project = new ProjectBuilder(root)
            .WithUpdatedPackage("Updated.Package", "1.0.0", "2.0.0")
            .Build();

        var result = new ResultBuilder(root).WithProject(project).Build();

        var summary = Summary.Create(result);

        // Act
        var md = methodInfo.Invoke(null, new object[] { summary });

        // Assert
        return Verify(md).UseParameters(method);
    }

    [Theory]
    [MemberData(nameof(GetMethods))]
    public Task OneUpdatedPackageInTwoProjects(string method)
    {
        // Arrange
        var methodInfo = typeof(MarkdownFormatter).GetMethod(
            method,
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public
        )!;
        var root = Path.GetTempPath();

        var project1 = new ProjectBuilder(root)
            .WithUpdatedPackage("Updated.Package", "1.0.0", "2.0.0")
            .Build();

        var project2 = project1 with { Path = Path.Combine(root, "Bar", "Bar.csproj") };

        var result = new ResultBuilder(root).WithProject(project1).WithProject(project2).Build();

        var summary = Summary.Create(result);

        // Act
        var md = methodInfo.Invoke(null, new object[] { summary });

        // Assert
        return Verify(md).UseParameters(method);
    }

    [Theory]
    [MemberData(nameof(GetMethods))]
    public Task OneUnknownPackage(string method)
    {
        // Arrange
        var methodInfo = typeof(MarkdownFormatter).GetMethod(
            method,
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public
        )!;
        var root = Path.GetTempPath();

        var project = new ProjectBuilder(root).WithUnknownPackages("Unknown.Package").Build();

        var project2 = project with { Path = Path.Combine(root, "Bar", "Bar.csproj") };

        var result = new ResultBuilder(root).WithProject(project).WithProject(project2).Build();

        var summary = Summary.Create(result);

        // Act
        var md = methodInfo.Invoke(null, new object[] { summary });

        // Assert
        return Verify(md).UseParameters(method);
    }

    [Theory]
    [MemberData(nameof(GetMethods))]
    public Task TwoUpdatedPackage(string method)
    {
        // Arrange
        var methodInfo = typeof(MarkdownFormatter).GetMethod(
            method,
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public
        )!;
        var root = Path.GetTempPath();

        var project = new ProjectBuilder(root)
            .WithUpdatedPackage("Updated.Package", "1.0.0", "2.0.0")
            .WithUpdatedPackage("Updated.Package.Abstracts", "1.0.0", "2.0.0")
            .Build();

        var result = new ResultBuilder(root).WithProject(project).Build();

        var summary = Summary.Create(result);

        // Act
        var md = methodInfo.Invoke(null, new object[] { summary });

        // Assert
        return Verify(md).UseParameters(method);
    }

    [Theory]
    [MemberData(nameof(GetMethods))]
    public Task DeprecatedPackage(string method)
    {
        // Arrange
        var methodInfo = typeof(MarkdownFormatter).GetMethod(
            method,
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public
        )!;
        var root = Path.GetTempPath();

        var project = new ProjectBuilder(root)
            .WithDeprecatedPackage("Deprecated.Package", "1.2.3", "Old and deprecated package.")
            .Build();

        var project2 = project with { Path = Path.Combine(root, "Bar", "Bar.csproj") };

        var result = new ResultBuilder(root).WithProject(project).WithProject(project2).Build();

        var summary = Summary.Create(result);

        // Act
        var md = methodInfo.Invoke(null, new object[] { summary });

        // Assert
        return Verify(md).UseParameters(method);
    }

    [Theory]
    [MemberData(nameof(GetMethods))]
    public Task DeprecatedPackageWithoutAlternative(string method)
    {
        // Arrange
        var methodInfo = typeof(MarkdownFormatter).GetMethod(
            method,
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public
        )!;
        var root = Path.GetTempPath();

        var project = new ProjectBuilder(root)
            .WithDeprecatedPackage(
                "Deprecated.Package",
                "1.2.3",
                "Old and deprecated package.",
                hasAlternativPackage: false
            )
            .Build();

        var project2 = project with { Path = Path.Combine(root, "Bar", "Bar.csproj") };

        var result = new ResultBuilder(root).WithProject(project).WithProject(project2).Build();

        var summary = Summary.Create(result);

        // Act
        var md = methodInfo.Invoke(null, new object[] { summary });

        // Assert
        return Verify(md).UseParameters(method);
    }

    [Theory]
    [MemberData(nameof(GetMethods))]
    public Task VulnerablePackage(string method)
    {
        // Arrange
        var methodInfo = typeof(MarkdownFormatter).GetMethod(
            method,
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public
        )!;
        var root = Path.GetTempPath();

        var project = new ProjectBuilder(root)
            .WithVulnerablePackage("Vulnerable.Package", "1.2.3")
            .Build();

        var project2 = project with { Path = Path.Combine(root, "Bar", "Bar.csproj") };

        var result = new ResultBuilder(root).WithProject(project).WithProject(project2).Build();

        var summary = Summary.Create(result);

        // Act
        var md = methodInfo.Invoke(null, new object[] { summary });

        // Assert
        return Verify(md).UseParameters(method);
    }

    [Theory]
    [MemberData(nameof(GetMethods))]
    public Task VulnerablePackageWithVulnerability(string method)
    {
        // Arrange
        var methodInfo = typeof(MarkdownFormatter).GetMethod(
            method,
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public
        )!;
        var root = Path.GetTempPath();

        var project = new ProjectBuilder(root)
            .WithVulnerablePackage(
                "Vulnerable.Package",
                "1.2.3",
                new PackageVulnerabilityMetadata(new Uri("https://google.com"), 1)
            )
            .Build();

        var project2 = project with { Path = Path.Combine(root, "Bar", "Bar.csproj") };

        var result = new ResultBuilder(root).WithProject(project).WithProject(project2).Build();

        var summary = Summary.Create(result);

        // Act
        var md = methodInfo.Invoke(null, new object[] { summary });

        // Assert
        return Verify(md).UseParameters(method);
    }

    [Theory]
    [MemberData(nameof(GetMethods))]
    public Task VulnerablePackageWithVulnerabilities(string method)
    {
        // Arrange
        var methodInfo = typeof(MarkdownFormatter).GetMethod(
            method,
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public
        )!;
        var root = Path.GetTempPath();

        var project = new ProjectBuilder(root)
            .WithVulnerablePackage(
                "Vulnerable.Package",
                "1.2.3",
                new PackageVulnerabilityMetadata(new Uri("https://google.com"), 1),
                new PackageVulnerabilityMetadata(new Uri("https://google.com/foo"), 2)
            )
            .Build();

        var project2 = project with { Path = Path.Combine(root, "Bar", "Bar.csproj") };

        var result = new ResultBuilder(root).WithProject(project).WithProject(project2).Build();

        var summary = Summary.Create(result);

        // Act
        var md = methodInfo.Invoke(null, new object[] { summary });

        // Assert
        return Verify(md).UseParameters(method);
    }

    [Theory]
    [MemberData(nameof(GetMethods))]
    public Task UnauthorizedSource(string method)
    {
        // Arrange
        var methodInfo = typeof(MarkdownFormatter).GetMethod(
            method,
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public
        )!;
        var root = Path.GetTempPath();

        var result = new ResultBuilder(root)
            .WithUnauthorizedSources("Unauthorized source", "https://google.com")
            .Build();

        var summary = Summary.Create(result);

        // Act
        var md = methodInfo.Invoke(null, new object[] { summary });

        // Assert
        return Verify(md).UseParameters(method);
    }

    [Theory]
    [MemberData(nameof(GetMethods))]
    public Task KitchenSink(string method)
    {
        // Arrange
        var methodInfo = typeof(MarkdownFormatter).GetMethod(
            method,
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public
        )!;
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
            .Build();

        var result = new ResultBuilder(root)
            .WithProject(project)
            .WithUnauthorizedSources("Unauthorized source", "https://google.com")
            .Build();

        var summary = Summary.Create(result);

        // Act
        var md = methodInfo.Invoke(null, new object[] { summary });

        // Assert
        return Verify(md).UseParameters(method);
    }

    public static IEnumerable<object[]> GetMethods()
    {
        yield return new object[] { "Generate" };
        yield return new object[] { "GenerateTitle" };
        yield return new object[] { "GenerateDescription" };
    }
}

internal sealed class ProjectBuilder
{
    private readonly string _root;
    private readonly ProjectWithPackages _project;

    public ProjectBuilder(string root, string? path = null)
    {
        _root = root;
        _project = new(
            path is null ? Path.Combine(_root, "Foo", "Foo.csproj") : Path.Combine(_root, path)
        );
    }

    public ProjectBuilder WithUpdatedPackage(string packageId, string from, string to)
    {
        _project.AddUpdatedPackage(
            new Internals.UpdatedPackage(
                packageId,
                NuGetVersion.Parse(from),
                NuGetVersion.Parse(to)
            )
        );

        return this;
    }

    public ProjectBuilder WithDeprecatedPackage(
        string packageId,
        string version,
        string message,
        bool hasAlternativPackage = true
    )
    {
        _project.AddDeprecatedPackage(
            new(
                packageId,
                NuGetVersion.Parse(version),
                new(
                    message,
                    new[] { "Other" },
                    hasAlternativPackage ? new("Not.Deprecated", VersionRange.AllStable) : null
                )
            )
        );

        return this;
    }

    public ProjectBuilder WithVulnerablePackage(
        string packageId,
        string version,
        params PackageVulnerabilityMetadata[] vulnerabilities
    )
    {
        _project.AddVulnerablePackage(
            new(
                packageId,
                NuGetVersion.Parse(version),
                vulnerabilities ?? Array.Empty<PackageVulnerabilityMetadata>()
            )
        );

        return this;
    }

    public ProjectBuilder WithUnknownPackages(string packageId)
    {
        _project.AddUnknownPackage(packageId);

        return this;
    }

    public ProjectWithPackages Build() => _project;
}

internal sealed class ResultBuilder
{
    private readonly string _root;
    private readonly Result _result;

    public ResultBuilder(string root)
    {
        _root = root;
        _result = new(_root);
    }

    public ResultBuilder WithProject(ProjectWithPackages project)
    {
        _result.TryAddProject(project);

        return this;
    }

    public ResultBuilder WithUnauthorizedSources(string name, string source)
    {
        _result.TryAddUnauthorizedSource(name, source);

        return this;
    }

    public Result Build() => _result;
}
