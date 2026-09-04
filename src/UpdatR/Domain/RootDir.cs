using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.SolutionPersistence.Model;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;
using NuGet.Frameworks;
using NuGet.Packaging;
using UpdatR.Domain.Utils;
using UpdatR.Internals;

namespace UpdatR.Domain;

internal sealed partial class RootDir
{
    private readonly DirectoryInfo _path;
    private readonly ILogger _logger;

    private RootDir(DirectoryInfo path, ILogger logger)
    {
        _path = path;
        _logger = logger;
    }

    public string Path => _path.FullName;

    private readonly List<DotnetTools> _dotnetTools = [];

    private readonly List<Csproj> _csprojs = [];

    private readonly List<FileBasedApp> _fileBasedApps = [];

    private readonly List<PropsFile> _propsFiles = [];

    public IReadOnlyCollection<DotnetTools> DotnetTools => _dotnetTools;

    public IReadOnlyCollection<Csproj> Csprojs => _csprojs;

    public IReadOnlyCollection<FileBasedApp> FileBasedApps => _fileBasedApps;

    public IReadOnlyCollection<PropsFile> PropsFiles => _propsFiles;

    public void AddDotnetTools(DotnetTools dotnetTools)
    {
        _dotnetTools.Add(dotnetTools);
    }

    public void AddCsproj(Csproj csproj)
    {
        _csprojs.Add(csproj);
    }

    public void AddFileBasedApp(FileBasedApp fileBasedApp)
    {
        _fileBasedApps.Add(fileBasedApp);
    }

    public void AddPropsFile(PropsFile propsFile)
    {
        _propsFiles.Add(propsFile);
    }

    public static Task<RootDir> CreateAsync(
        string path,
        Func<string, bool>? shouldExcludeFile = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                $"'{nameof(path)}' cannot be null or whitespace.",
                nameof(path)
            );
        }

        if (!Directory.Exists(path) && !File.Exists(path))
        {
            throw new ArgumentException($"'{nameof(path)}' does not exist.", nameof(path));
        }

        path = System.IO.Path.GetFullPath(path);

        shouldExcludeFile ??= static _ => false;
        logger ??= Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        return File.GetAttributes(path).HasFlag(FileAttributes.Directory)
            ? Task.FromResult(CreateFromFolder(new DirectoryInfo(path), shouldExcludeFile, logger))
            : CreateFromFileAsync(new FileInfo(path), shouldExcludeFile, logger, cancellationToken);
    }

    private static RootDir CreateFromFolder(
        DirectoryInfo path,
        Func<string, bool> shouldExcludeFile,
        ILogger logger
    )
    {
        var dir = new RootDir(path, logger);

        var csprojFiles = EnumerateProjectFiles(path.FullName);

        var dotnetToolsFiles = Directory
            .EnumerateFiles(
                path.FullName,
                "dotnet-tools.json",
                new EnumerationOptions
                {
                    MatchCasing = MatchCasing.CaseInsensitive,
                    RecurseSubdirectories = true,
                    AttributesToSkip = FileAttributes.System,
                }
            )
            .ToArray();

        var fileBasedAppFiles = Directory
            .EnumerateFiles(
                path.FullName,
                "*.cs",
                new EnumerationOptions
                {
                    MatchCasing = MatchCasing.CaseInsensitive,
                    RecurseSubdirectories = true,
                    AttributesToSkip = FileAttributes.System,
                }
            )
            .Where(csFile => !IsInBinOrObjFolder(csFile) && FileBasedApp.IsFileBasedApp(csFile))
            .ToArray();

        // The "nothing found" check below intentionally looks at every discovered file,
        // regardless of exclusion - excludeFiles narrows down which of an otherwise valid target
        // gets updated, it isn't meant to turn a target that legitimately contains no matching
        // files into a different kind of error.
        if (
            csprojFiles.Length == 0
            && dotnetToolsFiles.Length == 0
            && fileBasedAppFiles.Length == 0
        )
        {
            throw new InvalidUpdateTargetException(
                $"'{path.FullName}' contains no {string.Join(", ", Csproj.SupportedExtensions)} files, dotnet-tools.json files or file-based apps."
            );
        }

        // Excluded files are filtered out here - before they're added to `dir` - rather than
        // after RootDir.Create returns, so an excluded csproj never contributes its target
        // framework(s) to a shared props file (DiscoverPropsFiles below) and never influences
        // scoped dotnet-tools.json handling (e.g. the dotnet-ef version pin), matching the
        // principle that an excluded file should be as if it didn't exist for this run.
        foreach (var projectFile in csprojFiles.Where(x => !shouldExcludeFile(x)))
        {
            dir.AddCsproj(Csproj.Create(projectFile));
        }

        foreach (var configFile in dotnetToolsFiles.Where(x => !shouldExcludeFile(x)))
        {
            var config = Domain.DotnetTools.Create(
                configFile,
                FilterCsprojsInScope(
                    configFile,
                    dir.Csprojs.Count > 0 ? dir.Csprojs : GetProjectsRecursiveFromParent(path)
                )
            );

            dir.AddDotnetTools(config);
        }

        foreach (var csFile in fileBasedAppFiles.Where(x => !shouldExcludeFile(x)))
        {
            dir.AddFileBasedApp(FileBasedApp.Create(csFile));
        }

        DiscoverPropsFiles(dir, shouldExcludeFile);

        return dir;

        static bool IsInBinOrObjFolder(string filePath) =>
            filePath
                .Split(
                    System.IO.Path.DirectorySeparatorChar,
                    System.IO.Path.AltDirectorySeparatorChar
                )
                .Any(x =>
                    x.Equals("bin", StringComparison.OrdinalIgnoreCase)
                    || x.Equals("obj", StringComparison.OrdinalIgnoreCase)
                    || x.Equals(".git", StringComparison.OrdinalIgnoreCase)
                    || x.Equals(".vs", StringComparison.OrdinalIgnoreCase)
                    || x.Equals("node_modules", StringComparison.OrdinalIgnoreCase)
                );
    }

    private static async Task<RootDir> CreateFromFileAsync(
        FileInfo path,
        Func<string, bool> shouldExcludeFile,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        if (
            path.Extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
            || path.Extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase)
        )
        {
            var dir = new RootDir(path.Directory!, logger);

            var (projectPaths, filePaths) = await ReadSolutionAsync(path, cancellationToken)
                .ConfigureAwait(false);

            foreach (
                var projectPath in projectPaths
                    .Where(x =>
                        Csproj.SupportedExtensions.Any(extension =>
                            x.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
                        )
                    )
                    .Where(x => File.Exists(x))
                    .Where(x => !shouldExcludeFile(x))
            )
            {
                dir.AddCsproj(Csproj.Create(projectPath));
            }

            AddDotnetToolsFromCsproj(dir);

            foreach (var filePath in filePaths.Where(x => File.Exists(x) && !shouldExcludeFile(x)))
            {
                if (filePath.EndsWith("dotnet-tools.json", StringComparison.OrdinalIgnoreCase))
                {
                    dir.AddDotnetTools(
                        Domain.DotnetTools.Create(
                            filePath,
                            FilterCsprojsInScope(filePath, dir.Csprojs)
                        )
                    );
                }
                else if (
                    filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                    && FileBasedApp.IsFileBasedApp(filePath)
                )
                {
                    dir.AddFileBasedApp(FileBasedApp.Create(filePath));
                }
            }

            DiscoverPropsFiles(dir, shouldExcludeFile);

            return dir;
        }

        if (Csproj.SupportedExtensions.Contains(path.Extension, StringComparer.OrdinalIgnoreCase))
        {
            var dir = new RootDir(path.Directory!, logger);

            if (!shouldExcludeFile(path.FullName))
            {
                dir.AddCsproj(Csproj.Create(path.FullName));
            }

            AddDotnetToolsFromCsproj(dir);

            DiscoverPropsFiles(dir, shouldExcludeFile);

            return dir;
        }

        if (path.Name.Equals("dotnet-tools.json", StringComparison.OrdinalIgnoreCase))
        {
            var dir = new RootDir(path.Directory!, logger);

            if (!shouldExcludeFile(path.FullName))
            {
                var projects = GetProjectsRecursiveFromParent(path.Directory!)
                    .Where(x => !shouldExcludeFile(x.Path));

                dir.AddDotnetTools(Domain.DotnetTools.Create(path.FullName, projects));
            }

            return dir;
        }

        if (path.Extension.Equals(".cs", StringComparison.OrdinalIgnoreCase))
        {
            if (!FileBasedApp.IsFileBasedApp(path.FullName))
            {
                throw new InvalidUpdateTargetException(
                    $"'{path.FullName}' does not contain any '#:package' directives."
                );
            }

            var dir = new RootDir(path.Directory!, logger);

            if (!shouldExcludeFile(path.FullName))
            {
                dir.AddFileBasedApp(FileBasedApp.Create(path.FullName));
            }

            return dir;
        }

        throw new InvalidUpdateTargetException($"'{path.FullName}' is not a supported file.");

        void AddDotnetToolsFromCsproj(RootDir dir)
        {
            foreach (var csproj in dir.Csprojs)
            {
                var configPath = System.IO.Path.Combine(
                    csproj.Parent,
                    ".config",
                    "dotnet-tools.json"
                );

                if (!File.Exists(configPath) || shouldExcludeFile(configPath))
                {
                    continue;
                }

                dir.AddDotnetTools(
                    Domain.DotnetTools.Create(
                        configPath,
                        FilterCsprojsInScope(configPath, dir.Csprojs)
                    )
                );
            }
        }
    }

    /// <summary>
    /// Restricts <paramref name="csprojs"/> to the ones actually within the scope of
    /// <paramref name="configFilePath"/> (a <c>dotnet-tools.json</c> file), i.e. the directory
    /// containing its <c>.config</c> folder and any subdirectory of it. Without this, a
    /// <c>dotnet-tools.json</c> could be affected by an unrelated csproj found elsewhere in a
    /// larger folder/solution scan - e.g. capping <c>dotnet-ef</c> to a lower
    /// <c>Microsoft.EntityFrameworkCore</c> version than necessary just because some other,
    /// unrelated, project happens to reference an older version of it.
    /// </summary>
    private static IEnumerable<Csproj> FilterCsprojsInScope(
        string configFilePath,
        IEnumerable<Csproj> csprojs
    )
    {
        var configDir = new FileInfo(configFilePath).Directory;

        var scopeRoot =
            configDir is not null
            && configDir.Name.Equals(".config", StringComparison.OrdinalIgnoreCase)
                ? configDir.Parent?.FullName
                : configDir?.FullName;

        if (scopeRoot is null)
        {
            return csprojs;
        }

        var normalizedRoot = System
            .IO.Path.GetFullPath(scopeRoot)
            .TrimEnd(
                System.IO.Path.DirectorySeparatorChar,
                System.IO.Path.AltDirectorySeparatorChar
            );

        return csprojs.Where(csproj =>
        {
            var csprojDir = System.IO.Path.GetFullPath(csproj.Parent);

            return csprojDir.Equals(normalizedRoot, PathComparer.Comparison)
                || csprojDir.StartsWith(
                    normalizedRoot + System.IO.Path.DirectorySeparatorChar,
                    PathComparer.Comparison
                );
        });
    }

    private static string[] EnumerateProjectFiles(string rootPath)
    {
        var options = new EnumerationOptions
        {
            MatchCasing = MatchCasing.CaseInsensitive,
            RecurseSubdirectories = true,
        };

        return Csproj
            .SupportedExtensions.SelectMany(extension =>
                Directory.EnumerateFiles(rootPath, "*" + extension, options)
            )
            .ToArray();
    }

    private static HashSet<Csproj> GetProjectsRecursiveFromParent(DirectoryInfo path)
    {
        var isInConfigFolder = path.Name.Equals(".config", StringComparison.OrdinalIgnoreCase);

        HashSet<Csproj> projects = [];

        if (isInConfigFolder)
        {
            projects.AddRange(EnumerateProjectFiles(path.Parent!.FullName).Select(Csproj.Create));
        }

        return projects;
    }

    /// <summary>
    /// Opens <paramref name="solution"/> (either a classic <c>.sln</c> or an <c>.slnx</c> file)
    /// via <c>Microsoft.VisualStudio.SolutionPersistence</c> and returns the absolute paths of
    /// every referenced project as well as every loose file attached to a solution folder (e.g.
    /// <c>dotnet-tools.json</c> or a file-based app's <c>.cs</c> file). Both solution formats
    /// share the exact same object model, so this single implementation replaces what used to be
    /// two divergent, regex/XML-based parsers - and, as a bonus, gives <c>.sln</c> file-based-app
    /// discovery for free, which the old regex parser never supported.
    /// </summary>
    private static async Task<(
        IReadOnlyList<string> ProjectPaths,
        IReadOnlyList<string> FilePaths
    )> ReadSolutionAsync(FileInfo solution, CancellationToken cancellationToken)
    {
        var serializer =
            SolutionSerializers.GetSerializerByMoniker(solution.FullName)
            ?? throw new InvalidUpdateTargetException(
                $"'{solution.FullName}' is not a recognized solution file."
            );

        SolutionModel model;

        try
        {
            model = await serializer
                .OpenAsync(solution.FullName, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (SolutionException exception)
        {
            throw new InvalidUpdateTargetException(
                $"'{solution.FullName}' could not be parsed as a solution file: {exception.Message}",
                exception
            );
        }

        var projectPaths = model
            .SolutionProjects.Select(x => ResolveSolutionPath(solution, x.FilePath))
            .ToArray();

        var filePaths = model
            .SolutionFolders.SelectMany(x => x.Files ?? [])
            .Select(x => ResolveSolutionPath(solution, x))
            .ToArray();

        return (projectPaths, filePaths);

        static string ResolveSolutionPath(FileInfo solution, string relativeOrAbsolutePath) =>
            System.IO.Path.GetFullPath(
                System.IO.Path.Combine(
                    solution.DirectoryName!,
                    relativeOrAbsolutePath
                        .Replace('/', System.IO.Path.DirectorySeparatorChar)
                        .Replace('\\', System.IO.Path.DirectorySeparatorChar)
                )
            );
    }

    /// <summary>
    /// Finds every <c>.props</c>/<c>.targets</c> file (typically <c>Directory.Build.props</c> or,
    /// with Central Package Management, <c>Directory.Packages.props</c>) imported by any csproj
    /// in <paramref name="dir"/> that declares a <c>PackageReference</c>, <c>PackageVersion</c>
    /// or <c>GlobalPackageReference</c> item, using real MSBuild evaluation. A file imported by
    /// several csproj is only added once, tracking every contributing csproj's target framework
    /// so it can later be updated conservatively (i.e. only to a version compatible with all of
    /// them) - as well as, per occurrence, exactly which of those frameworks it applies to, so a
    /// <c>Condition</c>-guarded occurrence (e.g. framework-specific package versions in a shared
    /// <c>Directory.Packages.props</c>) can be updated independently of the same package's other
    /// occurrence(s) in the same file.
    /// </summary>
    private static void DiscoverPropsFiles(RootDir dir, Func<string, bool> shouldExcludeFile)
    {
        Dictionary<string, List<NuGetFramework>> tfmsByPath = new(PathComparer.Comparer);

        Dictionary<string, Dictionary<string, HashSet<NuGetFramework>>> candidateTfmsByPath = new(
            PathComparer.Comparer
        );

        foreach (var csproj in dir.Csprojs)
        {
            IReadOnlyList<PackageItemSource> sources;

            try
            {
                sources = MsBuildProjectInspector.GetPackageItemSources(csproj.Path);
            }
            catch (Exception exception)
            {
                // MSBuild evaluation can fail for a malformed project, a missing SDK, etc. The
                // csproj itself is still updated normally by Updater - we just won't be able to
                // discover any props/targets files it might import.
                dir.LogMsBuildEvaluationFailed(csproj.Path, exception);
                continue;
            }

            var importedSources = sources
                .Where(x => !string.Equals(x.SourceFile, csproj.Path, PathComparer.Comparison))
                .Where(x => !shouldExcludeFile(x.SourceFile))
                .ToArray();

            foreach (
                var sourceFile in importedSources
                    .Select(x => x.SourceFile)
                    .Distinct(PathComparer.Comparer)
            )
            {
                if (!tfmsByPath.TryGetValue(sourceFile, out var tfms))
                {
                    tfmsByPath[sourceFile] = tfms = [];
                }

                tfms.AddRange(csproj.TargetFrameworks);
            }

            // A single-targeted (or untargeted) project's plain evaluation above already
            // resolved $(TargetFramework) correctly - MSBuild sets it directly from the
            // TargetFramework property, no cross-targeting "outer build" is involved - so
            // whatever occurrence won there already reflects the right Condition branch for that
            // one framework. A genuinely multi-targeted project needs a real per-framework
            // evaluation instead, since a single plain evaluation of it never sets
            // $(TargetFramework) at all.
            if (
                csproj.TargetFrameworks.Count > 1
                && !csproj.TargetFrameworks.Contains(NuGetFramework.AnyFramework)
            )
            {
                IReadOnlyDictionary<string, IReadOnlyList<PackageItemSource>> byTfm;

                try
                {
                    byTfm = MsBuildProjectInspector.GetPackageItemSourcesByTfm(
                        csproj.Path,
                        [.. csproj.TargetFrameworks.Select(x => x.GetShortFolderName())]
                    );
                }
                catch (Exception exception)
                {
                    // Same as above - the per-TFM evaluation is best-effort. Every candidate
                    // in this project's props/targets files then falls back to TargetFrameworks.
                    dir.LogMsBuildPerTfmEvaluationFailed(csproj.Path, exception);
                    continue;
                }

                // Pass this evaluation straight to the Csproj instance so it doesn't need to run
                // the exact same (expensive) per-framework MSBuild evaluation a second time when
                // Updater later resolves ApplicableTfms for its PackageReference candidates.
                csproj.SetTfmSources(byTfm);

                foreach (var (tfmString, tfmSources) in byTfm)
                {
                    var tfm = NuGetFramework.Parse(tfmString);

                    foreach (
                        var source in tfmSources.Where(x =>
                            x.Version is not null
                            && !string.Equals(x.SourceFile, csproj.Path, PathComparer.Comparison)
                            && !shouldExcludeFile(x.SourceFile)
                        )
                    )
                    {
                        AddCandidateTfm(candidateTfmsByPath, source, tfm);
                    }
                }
            }
            else
            {
                foreach (var tfm in csproj.TargetFrameworks)
                {
                    foreach (var source in importedSources.Where(x => x.Version is not null))
                    {
                        AddCandidateTfm(candidateTfmsByPath, source, tfm);
                    }
                }
            }
        }

        foreach (var (path, tfms) in tfmsByPath)
        {
            candidateTfmsByPath.TryGetValue(path, out var candidateTfms);

            dir.AddPropsFile(
                PropsFile.Create(
                    path,
                    tfms,
                    candidateTfms?.ToDictionary(
                        x => x.Key,
                        x => (IReadOnlyCollection<NuGetFramework>)x.Value,
                        StringComparer.Ordinal
                    )
                )
            );
        }

        static void AddCandidateTfm(
            Dictionary<string, Dictionary<string, HashSet<NuGetFramework>>> candidateTfmsByPath,
            PackageItemSource source,
            NuGetFramework tfm
        )
        {
            if (!candidateTfmsByPath.TryGetValue(source.SourceFile, out var byKey))
            {
                candidateTfmsByPath[source.SourceFile] = byKey = new(StringComparer.Ordinal);
            }

            var key = CandidateTfmKey.Create(source.ItemType, source.PackageId, source.Version!);

            if (!byKey.TryGetValue(key, out var tfmSet))
            {
                byKey[key] = tfmSet = [];
            }

            tfmSet.Add(tfm);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Failed to evaluate '{ProjectPath}' with MSBuild; any props/targets files it imports will not be discovered or updated for it."
    )]
    private partial void LogMsBuildEvaluationFailed(string projectPath, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Failed to evaluate '{ProjectPath}' with MSBuild per target framework; its props/targets occurrences will fall back to being treated as applying to every one of its target frameworks."
    )]
    private partial void LogMsBuildPerTfmEvaluationFailed(string projectPath, Exception exception);
}
