using System.Text.RegularExpressions;
using NuGet.Packaging;

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

    public void AddDotnetTools(DotnetTools dotnetTools)
    {
        (DotnetTools ??= []).Add(dotnetTools);
    }

    public void AddCsproj(Csproj csproj)
    {
        (Csprojs ??= []).Add(csproj);
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
                dir.Csprojs ?? GetProjectsRecursiveFromParent(path)
            );

            dir.AddDotnetTools(config);
        }

        if (dir.Csprojs is null && dir.DotnetTools is null)
        {
            throw new ArgumentException(
                "Path contains no .csproj files or dotnet-tools.json files.",
                nameof(path)
            );
        }

        return dir;
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

            return dir;
        }

        if (path.Extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            var dir = new RootDir(path.Directory!);

            dir.AddCsproj(Csproj.Create(path.FullName));

            AddDotnetToolsFromCsproj(dir);

            return dir;
        }

        if (path.Name.Equals("dotnet-tools.json", StringComparison.OrdinalIgnoreCase))
        {
            var projects = GetProjectsRecursiveFromParent(path.Directory!);

            var dir = new RootDir(path.Directory!);

            dir.AddDotnetTools(Domain.DotnetTools.Create(path.FullName, projects));

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

                dir.AddDotnetTools(Domain.DotnetTools.Create(configPath, dir.Csprojs ?? []));
            }
        }
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
            .Select(x => Domain.DotnetTools.Create(x.FullName, csprojs));
}
