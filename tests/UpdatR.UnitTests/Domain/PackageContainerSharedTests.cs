using Microsoft.Extensions.Logging;
using NuGet.Frameworks;
using NuGet.Versioning;
using UpdatR.Domain;
using UpdatR.Internals;

namespace UpdatR.UnitTests.Domain;

/// <summary>
/// Tests the shared update algorithm in <c>PackageContainer</c> (the abstract base class behind
/// <see cref="Csproj"/>, <see cref="PropsFile"/> and <see cref="FileBasedApp"/>) once, running the
/// same scenarios against all three concrete types via <see cref="Fixture"/>. Type-specific
/// behavior (e.g. <c>VersionOverride</c>/CPM handling, <c>#:package</c> directive parsing, or
/// EntityFramework version tracking) is still covered by <see cref="CsprojTests"/>,
/// <see cref="PropsFileTests"/> and <see cref="FileBasedAppTests"/>.
/// </summary>
public class PackageContainerSharedTests
{
    public static TheoryData<string> FixtureKinds => ["Csproj", "PropsFile", "FileBasedApp"];

    private static Fixture CreateFixture(string kind) =>
        kind switch
        {
            "Csproj" => new CsprojFixture(),
            "PropsFile" => new PropsFileFixture(),
            "FileBasedApp" => new FileBasedAppFixture(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, message: null),
        };

    [Theory]
    [MemberData(nameof(FixtureKinds))]
    public async Task BumpsFloatingVersionWhenNewerVersionIsAvailable(string fixtureKind)
    {
        await using var fixture = CreateFixture(fixtureKind);

        // Arrange
        await fixture.CreateAsync("Some.Package", "4.8.*");

        var logger = new FakeLogger();
        var package = CreatePackage("Some.Package", "4.8.1", "4.9.2");

        // Act
        var result = await fixture.UpdatePackagesAsync(
            new Dictionary<string, NuGetPackage?> { ["Some.Package"] = package },
            dryRun: false,
            logger
        );

        // Assert
        Assert.NotNull(result);

        var updated = Assert.Single(result.UpdatedPackages);

        Assert.Equal("Some.Package", updated.PackageId);
        Assert.Equal(NuGetVersion.Parse("4.9.2"), updated.To);

        Assert.Equal("4.9.*", await fixture.ReadVersionStringAsync("Some.Package"));
    }

    [Theory]
    [MemberData(nameof(FixtureKinds))]
    public async Task WarnsAndReportsFixedVersionRangeThatCannotBeRewritten(string fixtureKind)
    {
        await using var fixture = CreateFixture(fixtureKind);

        // Arrange - "[1.0,2.0)" has no floating segment, so UpdatR doesn't know how to safely
        // rewrite it even though a newer, non-matching version (2.5.0) is available.
        await fixture.CreateAsync("Some.Package", "[1.0,2.0)");

        var logger = new FakeLogger();
        var package = CreatePackage("Some.Package", "1.5.0", "2.5.0");

        // Act
        var result = await fixture.UpdatePackagesAsync(
            new Dictionary<string, NuGetPackage?> { ["Some.Package"] = package },
            dryRun: true,
            logger
        );

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.UpdatedPackages);

        var unsupported = Assert.Single(result.UnsupportedRangePackages);

        Assert.Equal("Some.Package", unsupported.PackageId);
        Assert.Equal("[1.0,2.0)", unsupported.VersionRange);

        var (level, message) = Assert.Single(logger.Logs);

        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains("[1.0,2.0)", message, StringComparison.Ordinal);

        Assert.Equal("[1.0,2.0)", await fixture.ReadVersionStringAsync("Some.Package"));
    }

    [Theory]
    [MemberData(nameof(FixtureKinds))]
    public async Task SkipsFloatingVersionWithoutWarningWhenAlreadyLatest(string fixtureKind)
    {
        await using var fixture = CreateFixture(fixtureKind);

        // Arrange - "4.8.*" already resolves to the latest matching version, so there's nothing
        // for UpdatR to do, and this should stay quiet (no warning-level log, no report entry).
        await fixture.CreateAsync("Some.Package", "4.8.*");

        var logger = new FakeLogger();
        var package = CreatePackage("Some.Package", "4.8.1");

        // Act
        var result = await fixture.UpdatePackagesAsync(
            new Dictionary<string, NuGetPackage?> { ["Some.Package"] = package },
            dryRun: true,
            logger
        );

        // Assert
        Assert.Null(result);
        Assert.DoesNotContain(logger.Logs, x => x.Level == LogLevel.Warning);
        Assert.Equal("4.8.*", await fixture.ReadVersionStringAsync("Some.Package"));
    }

    [Theory]
    [MemberData(nameof(FixtureKinds))]
    public async Task LogsWarningAndReportsUnknownPackageWhenMissing(string fixtureKind)
    {
        await using var fixture = CreateFixture(fixtureKind);

        // Arrange
        await fixture.CreateAsync("Some.Package", "1.0.0");

        var logger = new FakeLogger();

        // Act
        var result = await fixture.UpdatePackagesAsync(
            new Dictionary<string, NuGetPackage?>(),
            dryRun: true,
            logger
        );

        // Assert - only FileBasedApp reports a project whose only finding is unknown packages
        // (a pre-existing, intentional difference from Csproj/PropsFile); the missing-package
        // warning itself is logged the same way by all three.
        if (fixtureKind == "FileBasedApp")
        {
            Assert.NotNull(result);
            Assert.Contains("Some.Package", result.UnknownPackages);
        }
        else
        {
            Assert.Null(result);
        }

        var (level, message) = Assert.Single(logger.Logs);

        Assert.Equal(LogLevel.Warning, level);
        Assert.Equal("Could not find Some.Package.", message);
    }

    private static NuGetPackage CreatePackage(
        string packageId,
        string installedVersion,
        string? newerVersion = null
    )
    {
        var tfm = NuGetFramework.Parse("net10.0");

        var metadatas = new List<PackageMetadata>
        {
            new(NuGetVersion.Parse(installedVersion), [tfm], null, null),
        };

        if (newerVersion is not null)
        {
            metadatas.Add(new(NuGetVersion.Parse(newerVersion), [tfm], null, null));
        }

        return new NuGetPackage(packageId, metadatas);
    }

    /// <summary>
    /// A minimal adapter over a single-package <see cref="Csproj"/>/<see cref="PropsFile"/>/
    /// <see cref="FileBasedApp"/> fixture file, so the same scenario can run against all three
    /// concrete <c>PackageContainer</c> types.
    /// </summary>
    internal abstract class Fixture : IAsyncDisposable
    {
        protected Fixture(string fileExtension)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"{Guid.NewGuid()}{fileExtension}"
            );
        }

        protected string Path { get; }

        public abstract Task CreateAsync(string packageId, string versionString);

        public abstract Task<ProjectWithPackages?> UpdatePackagesAsync(
            IDictionary<string, NuGetPackage?> packages,
            bool dryRun,
            ILogger logger
        );

        public abstract Task<string> ReadVersionStringAsync(string packageId);

        public ValueTask DisposeAsync()
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }

            GC.SuppressFinalize(this);

            return ValueTask.CompletedTask;
        }

        // xunit prints the fixture in test names via ToString().
        public override string ToString() =>
            GetType().Name.Replace("Fixture", "", StringComparison.Ordinal);
    }

    private sealed class CsprojFixture : Fixture
    {
        public CsprojFixture()
            : base(".csproj") { }

        public override async Task CreateAsync(string packageId, string versionString) =>
            await File.WriteAllTextAsync(
                Path,
                $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="{packageId}" Version="{versionString}" />
                  </ItemGroup>
                </Project>
                """
            );

        public override Task<ProjectWithPackages?> UpdatePackagesAsync(
            IDictionary<string, NuGetPackage?> packages,
            bool dryRun,
            ILogger logger
        ) =>
            Csproj.Create(Path).UpdatePackagesAsync(packages, dryRun, usePrerelease: false, logger);

        public override async Task<string> ReadVersionStringAsync(string packageId)
        {
            var content = await File.ReadAllTextAsync(Path);
            var match = System.Text.RegularExpressions.Regex.Match(content, "Version=\"([^\"]*)\"");

            return match.Groups[1].Value;
        }
    }

    private sealed class PropsFileFixture : Fixture
    {
        public PropsFileFixture()
            : base(".props") { }

        public override async Task CreateAsync(string packageId, string versionString) =>
            await File.WriteAllTextAsync(
                Path,
                $"""
                <Project>
                  <ItemGroup>
                    <PackageReference Include="{packageId}" Version="{versionString}" />
                  </ItemGroup>
                </Project>
                """
            );

        public override Task<ProjectWithPackages?> UpdatePackagesAsync(
            IDictionary<string, NuGetPackage?> packages,
            bool dryRun,
            ILogger logger
        ) =>
            PropsFile
                .Create(Path, [NuGetFramework.Parse("net10.0")])
                .UpdatePackagesAsync(packages, dryRun, usePrerelease: false, logger);

        public override async Task<string> ReadVersionStringAsync(string packageId)
        {
            var content = await File.ReadAllTextAsync(Path);
            var match = System.Text.RegularExpressions.Regex.Match(content, "Version=\"([^\"]*)\"");

            return match.Groups[1].Value;
        }
    }

    private sealed class FileBasedAppFixture : Fixture
    {
        public FileBasedAppFixture()
            : base(".cs") { }

        public override async Task CreateAsync(string packageId, string versionString) =>
            await File.WriteAllTextAsync(
                Path,
                $"""
                #:package {packageId}@{versionString}
                Console.WriteLine("Hello, world!");
                """
            );

        public override async Task<ProjectWithPackages?> UpdatePackagesAsync(
            IDictionary<string, NuGetPackage?> packages,
            bool dryRun,
            ILogger logger
        ) =>
            await FileBasedApp
                .Create(Path)
                .UpdatePackagesAsync(
                    packages,
                    dryRun,
                    usePrerelease: false,
                    logger,
                    tfm: NuGetFramework.Parse("net10.0")
                );

        public override async Task<string> ReadVersionStringAsync(string packageId)
        {
            var content = await File.ReadAllTextAsync(Path);
            var match = System.Text.RegularExpressions.Regex.Match(
                content,
                $"#:package\\s+{System.Text.RegularExpressions.Regex.Escape(packageId)}@(\\S+)"
            );

            return match.Groups[1].Value;
        }
    }

    private sealed class FakeLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Logs { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            Logs.Add((logLevel, formatter(state, exception)));
        }
    }
}
