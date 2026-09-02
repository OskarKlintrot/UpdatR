using UpdatR.Internals;

namespace UpdatR.UnitTests;

public sealed class MsBuildProjectInspectorTests : IDisposable
{
    private readonly string _root = Directory
        .CreateDirectory(
            Path.Combine(Path.GetTempPath(), "UpdatR.UnitTests", Guid.NewGuid().ToString())
        )
        .FullName;

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void PackageReferenceDeclaredInTheProjectItselfIsAttributedToTheProjectFile()
    {
        // Arrange
        var csproj = WriteFile(
            "MyProj.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
              </ItemGroup>
            </Project>
            """
        );

        // Act
        var sources = MsBuildProjectInspector.GetPackageItemSources(csproj);

        // Assert
        var source = Assert.Single(sources);

        Assert.Equal("PackageReference", source.ItemType);
        Assert.Equal("Newtonsoft.Json", source.PackageId);
        Assert.Equal("13.0.3", source.Version);
        Assert.Equal(csproj, source.SourceFile, ignoreCase: true);
    }

    [Fact]
    public void PackageReferenceDeclaredInDirectoryBuildPropsIsAttributedToThatFileNotTheProject()
    {
        // Arrange
        var directoryBuildProps = WriteFile(
            "Directory.Build.props",
            """
            <Project>
              <ItemGroup>
                <PackageReference Include="Root.Package" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """
        );

        var csproj = WriteFile(
            Path.Combine("src", "MyProj", "MyProj.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """
        );

        // Act
        var sources = MsBuildProjectInspector.GetPackageItemSources(csproj);

        // Assert
        var source = Assert.Single(sources);

        Assert.Equal("Root.Package", source.PackageId);
        Assert.Equal(directoryBuildProps, source.SourceFile, ignoreCase: true);
        Assert.NotEqual(csproj, source.SourceFile, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void HandlesMultipleLevelsOfDirectoryBuildPropsImportingEachOther()
    {
        // Arrange

        // Root Directory.Build.props, one PackageReference of its own.
        var rootProps = WriteFile(
            "Directory.Build.props",
            """
            <Project>
              <ItemGroup>
                <PackageReference Include="Root.Package" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """
        );

        // Sub-folder Directory.Build.props that explicitly imports the one above it (the
        // standard pattern for opting in to inheriting props from a parent folder) and adds its
        // own PackageReference.
        var subProps = WriteFile(
            Path.Combine("src", "Directory.Build.props"),
            """
            <Project>
              <Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />
              <ItemGroup>
                <PackageReference Include="Sub.Package" Version="2.0.0" />
              </ItemGroup>
            </Project>
            """
        );

        var csproj = WriteFile(
            Path.Combine("src", "MyProj", "MyProj.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Proj.Package" Version="3.0.0" />
              </ItemGroup>
            </Project>
            """
        );

        // Act
        var sources = MsBuildProjectInspector.GetPackageItemSources(csproj);
        var imports = MsBuildProjectInspector.GetImportedFiles(csproj);

        // Assert
        Assert.Equal(3, sources.Count);

        AssertSource(sources, "Root.Package", "1.0.0", rootProps);
        AssertSource(sources, "Sub.Package", "2.0.0", subProps);
        AssertSource(sources, "Proj.Package", "3.0.0", csproj);

        Assert.Contains(
            imports,
            i => string.Equals(i, rootProps, StringComparison.OrdinalIgnoreCase)
        );
        Assert.Contains(
            imports,
            i => string.Equals(i, subProps, StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public void CentralPackageManagementPackageVersionIsAttributedToDirectoryPackagesProps()
    {
        // Arrange
        var directoryPackagesProps = WriteFile(
            "Directory.Packages.props",
            """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
              </PropertyGroup>
              <ItemGroup>
                <PackageVersion Include="Newtonsoft.Json" Version="13.0.3" />
              </ItemGroup>
            </Project>
            """
        );

        var csproj = WriteFile(
            "MyProj.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" />
              </ItemGroup>
            </Project>
            """
        );

        // Act
        var sources = MsBuildProjectInspector.GetPackageItemSources(csproj);
        var imports = MsBuildProjectInspector.GetImportedFiles(csproj);

        // Assert
        var packageReference = Assert.Single(sources, s => s.ItemType == "PackageReference");
        var packageVersion = Assert.Single(sources, s => s.ItemType == "PackageVersion");

        Assert.Equal("Newtonsoft.Json", packageReference.PackageId);
        Assert.Null(packageReference.Version);
        Assert.Equal(csproj, packageReference.SourceFile, ignoreCase: true);

        Assert.Equal("Newtonsoft.Json", packageVersion.PackageId);
        Assert.Equal("13.0.3", packageVersion.Version);
        Assert.Equal(directoryPackagesProps, packageVersion.SourceFile, ignoreCase: true);

        Assert.Contains(
            imports,
            i => string.Equals(i, directoryPackagesProps, StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public void GetImportedFilesReturnsDirectoryBuildPropsForAPlainProject()
    {
        // Arrange
        var directoryBuildProps = WriteFile(
            "Directory.Build.props",
            """
            <Project>
            </Project>
            """
        );

        var csproj = WriteFile(
            "MyProj.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """
        );

        // Act
        var imports = MsBuildProjectInspector.GetImportedFiles(csproj);

        // Assert
        Assert.Contains(
            imports,
            i => string.Equals(i, directoryBuildProps, StringComparison.OrdinalIgnoreCase)
        );
    }

    private static void AssertSource(
        IReadOnlyList<PackageItemSource> sources,
        string packageId,
        string version,
        string expectedSourceFile
    )
    {
        var source = Assert.Single(sources, s => s.PackageId == packageId);

        Assert.Equal(version, source.Version);
        Assert.Equal(expectedSourceFile, source.SourceFile, ignoreCase: true);
    }

    private string WriteFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_root, relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        File.WriteAllText(fullPath, content);

        return fullPath;
    }
}
