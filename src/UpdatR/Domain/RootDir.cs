using System.Text.RegularExpressions;
using System.Xml.Linq;
using NuGet.Frameworks;
using NuGet.Packaging;
using UpdatR.Domain.Utils;
using UpdatR.Internals;

namespace UpdatR.Domain;

internal sealed class RootDir
{
    private readonly DirectoryInfo _path;

    private RootDir(DirectoryInfo path)
    {
        _path = path;
    }

    public string Path => _path.FullName;

    public ICollection<DotnetTools>? DotnetTools { get; private set; }

    public ICollection<Csproj>? Csprojs { get; private set; }

    public ICollection<FileBasedApp>? FileBasedApps { get; private set; }

    public ICollection<PropsFile>? PropsFiles { get; private set; }

    public void AddDotnetTools(DotnetTools dotnetTools)
    {
        (DotnetTools ??= []).Add(dotnetTools);
    }

    public void AddCsproj(Csproj csproj)
    {
        (Csprojs ??= []).Add(csproj);
    }

    public void AddFileBasedApp(FileBasedApp fileBasedApp)
    {
        (FileBasedApps ??= []).Add(fileBasedApp);
    }

    public void AddPropsFile(PropsFile propsFile)
    {
        (PropsFiles ??= []).Add(propsFile);
    }

    public static RootDir Create(string path)
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

        return File.GetAttributes(path).HasFlag(FileAttributes.Directory) switch
        {
            true => CreateFromFolder(new DirectoryInfo(path)),
            false => CreateFromFile(new FileInfo(path)),
        };
    }

    private static RootDir CreateFromFolder(DirectoryInfo path)
    {
        var dir = new RootDir(path);

        foreach (
            var projectFile in Directory.EnumerateFiles(
                path.FullName,
                "*.csproj",
                new EnumerationOptions
                {
                    MatchCasing = MatchCasing.CaseInsensitive,
                    RecurseSubdirectories = true,
                }
            )
        )
        {
            var csproj = Csproj.Create(projectFile);

            dir.AddCsproj(csproj);
        }

        foreach (
            var configFile in Directory.EnumerateFiles(
                path.FullName,
                "dotnet-tools.json",
                new EnumerationOptions
                {
                    MatchCasing = MatchCasing.CaseInsensitive,
                    RecurseSubdirectories = true,
                    AttributesToSkip = FileAttributes.System,
                }
            )
        )
        {
            var config = Domain.DotnetTools.Create(
                configFile,
                FilterCsprojsInScope(
                    configFile,
                    dir.Csprojs ?? GetProjectsRecursiveFromParent(path)
                )
            );

            dir.AddDotnetTools(config);
        }

        foreach (
            var csFile in Directory.EnumerateFiles(
                path.FullName,
                "*.cs",
                new EnumerationOptions
                {
                    MatchCasing = MatchCasing.CaseInsensitive,
                    RecurseSubdirectories = true,
                    AttributesToSkip = FileAttributes.System,
                }
            )
        )
        {
            if (IsInBinOrObjFolder(csFile) || !FileBasedApp.IsFileBasedApp(csFile))
            {
                continue;
            }

            dir.AddFileBasedApp(FileBasedApp.Create(csFile));
        }

        if (dir.Csprojs is null && dir.DotnetTools is null && dir.FileBasedApps is null)
        {
            throw new ArgumentException(
                "Path contains no .csproj files, dotnet-tools.json files or file-based apps.",
                nameof(path)
            );
        }

        DiscoverPropsFiles(dir);

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
                );
    }

    private static RootDir CreateFromFile(FileInfo path)
    {
        if (path.Extension.Equals(".sln", StringComparison.OrdinalIgnoreCase))
        {
            var dir = new RootDir(path.Directory!);

            foreach (var csproj in GetProjectsFromSolution(path))
            {
                dir.AddCsproj(csproj);
            }

            AddDotnetToolsFromCsproj(dir);

            foreach (var item in GetDotnetToolsConfigFromSolution(path, dir.Csprojs ?? []))
            {
                dir.AddDotnetTools(item);
            }

            DiscoverPropsFiles(dir);

            return dir;
        }

        if (path.Extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            var dir = new RootDir(path.Directory!);

            foreach (var csproj in GetProjectsFromSolutionX(path))
            {
                dir.AddCsproj(csproj);
            }

            AddDotnetToolsFromCsproj(dir);

            foreach (var item in GetDotnetToolsConfigFromSolutionX(path, dir.Csprojs ?? []))
            {
                dir.AddDotnetTools(item);
            }

            foreach (var fileBasedApp in GetFileBasedAppsFromSolutionX(path))
            {
                dir.AddFileBasedApp(fileBasedApp);
            }

            DiscoverPropsFiles(dir);

            return dir;
        }

        if (path.Extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            var dir = new RootDir(path.Directory!);

            dir.AddCsproj(Csproj.Create(path.FullName));

            AddDotnetToolsFromCsproj(dir);

            DiscoverPropsFiles(dir);

            return dir;
        }

        if (path.Name.Equals("dotnet-tools.json", StringComparison.OrdinalIgnoreCase))
        {
            var projects = GetProjectsRecursiveFromParent(path.Directory!);

            var dir = new RootDir(path.Directory!);

            dir.AddDotnetTools(Domain.DotnetTools.Create(path.FullName, projects));

            return dir;
        }

        if (path.Extension.Equals(".cs", StringComparison.OrdinalIgnoreCase))
        {
            if (!FileBasedApp.IsFileBasedApp(path.FullName))
            {
                throw new ArgumentException(
                    $"'{path.FullName}' does not contain any '#:package' directives.",
                    nameof(path)
                );
            }

            var dir = new RootDir(path.Directory!);

            dir.AddFileBasedApp(FileBasedApp.Create(path.FullName));

            return dir;
        }

        throw new ArgumentException($"'{nameof(path)}' is not a supported file.", nameof(path));

        static void AddDotnetToolsFromCsproj(RootDir dir)
        {
            foreach (var csproj in dir.Csprojs ?? Array.Empty<Csproj>())
            {
                var configPath = System.IO.Path.Combine(
                    csproj.Parent,
                    ".config",
                    "dotnet-tools.json"
                );

                if (!File.Exists(configPath))
                {
                    continue;
                }

                dir.AddDotnetTools(
                    Domain.DotnetTools.Create(
                        configPath,
                        FilterCsprojsInScope(configPath, dir.Csprojs ?? [])
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

            return csprojDir.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
                || csprojDir.StartsWith(
                    normalizedRoot + System.IO.Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase
                );
        });
    }

    private static HashSet<Csproj> GetProjectsRecursiveFromParent(DirectoryInfo path)
    {
        var isInConfigFolder = path.Name.Equals(".config", StringComparison.OrdinalIgnoreCase);

        HashSet<Csproj> projects = [];

        if (isInConfigFolder)
        {
            projects.AddRange(
                Directory
                    .EnumerateFiles(
                        path.Parent!.FullName,
                        "*.csproj",
                        new EnumerationOptions
                        {
                            MatchCasing = MatchCasing.CaseInsensitive,
                            RecurseSubdirectories = true,
                        }
                    )
                    .Select(Csproj.Create)
            );
        }

        return projects;
    }

    private static IEnumerable<Csproj> GetProjectsFromSolution(FileInfo solution) =>
        Regex
            .Matches(
                File.ReadAllText(solution.FullName),
                @"(Project).*(?<="")(?<Project>\S*\.csproj)(?="")",
                RegexOptions.Multiline
            )
            .Select(x => System.IO.Path.Combine(x.Groups["Project"].Value.Split('\\'))) // sln has windows-style paths, will not work on linux
            .Select(x => System.IO.Path.Combine(solution.DirectoryName!, x))
            .Select(x => new FileInfo(x))
            .Where(x => x.Exists)
            .Select(x => Csproj.Create(x.FullName));

    private static IEnumerable<DotnetTools> GetDotnetToolsConfigFromSolution(
        FileInfo solution,
        IEnumerable<Csproj> csprojs
    ) =>
        Regex
            .Matches(
                File.ReadAllText(solution.FullName),
                """(?<File>\.config\\dotnet-tools\.json)(?= =)""",
                RegexOptions.Multiline
            )
            .Select(x => System.IO.Path.Combine(x.Groups["File"].Value))
            .Select(x => System.IO.Path.Combine(solution.DirectoryName!, x))
            .Select(x => new FileInfo(x))
            .Where(x => x.Exists)
            .Select(x =>
                Domain.DotnetTools.Create(x.FullName, FilterCsprojsInScope(x.FullName, csprojs))
            );

    private static IEnumerable<Csproj> GetProjectsFromSolutionX(FileInfo solution) =>
        GetPathsFromSolutionX(solution, "Project")
            .Where(x => x.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Select(x => new FileInfo(x))
            .Where(x => x.Exists)
            .Select(x => Csproj.Create(x.FullName));

    private static IEnumerable<DotnetTools> GetDotnetToolsConfigFromSolutionX(
        FileInfo solution,
        IEnumerable<Csproj> csprojs
    ) =>
        GetPathsFromSolutionX(solution, "File")
            .Where(x => x.EndsWith("dotnet-tools.json", StringComparison.OrdinalIgnoreCase))
            .Select(x => new FileInfo(x))
            .Where(x => x.Exists)
            .Select(x =>
                Domain.DotnetTools.Create(x.FullName, FilterCsprojsInScope(x.FullName, csprojs))
            );

    private static IEnumerable<FileBasedApp> GetFileBasedAppsFromSolutionX(FileInfo solution) =>
        GetPathsFromSolutionX(solution, "File")
            .Where(x => x.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Select(x => new FileInfo(x))
            .Where(x => x.Exists)
            .Where(x => FileBasedApp.IsFileBasedApp(x.FullName))
            .Select(x => FileBasedApp.Create(x.FullName));

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
    private static void DiscoverPropsFiles(RootDir dir)
    {
        if (dir.Csprojs is null)
        {
            return;
        }

        Dictionary<string, List<NuGetFramework>> tfmsByPath = new(StringComparer.OrdinalIgnoreCase);

        Dictionary<string, Dictionary<string, HashSet<NuGetFramework>>> candidateTfmsByPath = new(
            StringComparer.OrdinalIgnoreCase
        );

        foreach (var csproj in dir.Csprojs)
        {
            IReadOnlyList<PackageItemSource> sources;

            try
            {
                sources = MsBuildProjectInspector.GetPackageItemSources(csproj.Path);
            }
            catch (Exception)
            {
                // MSBuild evaluation can fail for a malformed project, a missing SDK, etc. The
                // csproj itself is still updated normally by Updater - we just won't be able to
                // discover any props/targets files it might import.
                continue;
            }

            var importedSources = sources
                .Where(x =>
                    !string.Equals(x.SourceFile, csproj.Path, StringComparison.OrdinalIgnoreCase)
                )
                .ToArray();

            foreach (
                var sourceFile in importedSources
                    .Select(x => x.SourceFile)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
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
                catch (Exception)
                {
                    continue;
                }

                foreach (var (tfmString, tfmSources) in byTfm)
                {
                    var tfm = NuGetFramework.Parse(tfmString);

                    foreach (
                        var source in tfmSources.Where(x =>
                            x.Version is not null
                            && !string.Equals(
                                x.SourceFile,
                                csproj.Path,
                                StringComparison.OrdinalIgnoreCase
                            )
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

    private static IEnumerable<string> GetPathsFromSolutionX(FileInfo solution, string elementName)
    {
        var doc = XDocument.Load(solution.FullName);

        return doc.Descendants(elementName)
            .Select(x => x.Attribute("Path")?.Value)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x =>
                System.IO.Path.Combine(
                    solution.DirectoryName!,
                    x!
                        .Replace('/', System.IO.Path.DirectorySeparatorChar)
                        .Replace('\\', System.IO.Path.DirectorySeparatorChar)
                )
            );
    }
}
