using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using BuildingBlocks;
using Markdig;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UpdatR.Formatters;

namespace UpdatR.Cli;

internal static partial class Program
{
    private static ILogger _logger = null!;

    internal static Task<int> Main(string[] args)
    {
        var pathArgument = new Argument<string>("args")
        {
            Description =
                "Path to solution or project(s). Defaults to current folder. Target can be a specific file or folder. If target is a folder then all *.csproj/*.fsproj/*.vbproj files, dotnet-tools.json-files and file-based apps will be processed.",
            DefaultValueFactory = _ => ".",
        };

        var packageOption = new Option<string[]>("--package")
        {
            Description =
                "Package to update. Supports * as wildcard. Will update all unless specified.",
            DefaultValueFactory = _ => [],
        };

        var excludePackageOption = new Option<string[]>("--exclude-package")
        {
            Description =
                "Package to exclude. Supports * as wildcard. Merged with \"excludePackages\" from a .updatrrc file, if present.",
            DefaultValueFactory = _ => [],
        };

        var outputOption = new Option<OutputFormat>("--output")
        {
            Description =
                "Format of the summary written to stdout. \"text\" (default) writes the human-readable, colored summary. \"json\" writes only machine-readable JSON to stdout - logs and any other diagnostic output are sent to stderr instead, so stdout can be safely piped to or parsed by another program.",
            DefaultValueFactory = _ => OutputFormat.Text,
        };

        var outputPathOption = new Option<string?>("--output-path")
        {
            Description =
                "Writes the summary to a file. If an existing directory is given, an \"output.md\" file is created there. If a file path is given, its extension decides the format: \".md\" for markdown, \".txt\" for plain text, or \".json\" for machine-readable JSON.",
        };

        var titleOption = new Option<string?>("--title") { Description = "Outputs title to path." };

        var descriptionOption = new Option<string?>("--description")
        {
            Description = "Outputs description to path.",
        };

        var verbosityOption = new Option<LogLevel>("--verbosity")
        {
            Description = "Log level.",
            DefaultValueFactory = _ => LogLevel.Warning,
        };

        var dryRunOption = new Option<bool>("--dry-run")
        {
            Description = "Do not save any changes.",
        };

        var prereleaseOption = new Option<bool>("--prerelease")
        {
            Description = "Allow prerelease packages to be installed.",
        };

        var browserOption = new Option<bool>("--browser")
        {
            Description = "Open summary in browser.",
        };

        var interactiveOption = new Option<bool>("--interactive")
        {
            Description = "Interaction with user is possible.",
        };

        var tfmOption = new Option<string?>("--tfm") { Description = "Lowest TFM to support." };

        var allowedLicensesOption = new Option<string[]>("--allowed-licenses")
        {
            Description =
                "Only update to (and warn about) versions whose license contains one of these values, e.g. 'MIT'. Packages without license information are always allowed. Leave out to disable license checking. Merged with \"allowedLicenses\" from a .updatrrc file, if present.",
            DefaultValueFactory = _ => [],
        };

        var excludeFileOption = new Option<string[]>("--exclude-file")
        {
            Description =
                "File to exclude, matched against its path relative to the resolved target. Supports * as wildcard. Merged with \"excludeFiles\" from a .updatrrc file, if present.",
            DefaultValueFactory = _ => [],
        };

        var alignWithTfmOption = new Option<string[]>("--align-with-tfm")
        {
            Description =
                "Package to keep aligned with the project's target framework's major version, instead of updating to a newer version whose major just happens to also be compatible. Supports * as wildcard. Merged with \"alignWithTfm\" from a .updatrrc file, if present.",
            DefaultValueFactory = _ => [],
        };

        var failOnOption = new Option<FailOn?>("--fail-on")
        {
            Description =
                "Exit with a non-zero code if a finding of this severity or higher is found: \"outdated\" (any package was updated, is deprecated, or is vulnerable - most useful together with --dry-run), \"deprecated\" (deprecated or vulnerable) or \"vulnerable\". Defaults to \"none\", or \"failOn\" from a .updatrrc file, if present.",
        };

        var failOnIncompleteOption = new Option<bool>("--fail-on-incomplete")
        {
            Description =
                "Exit with a non-zero code if the run was incomplete, i.e. a package source returned 401 or a package couldn't be resolved on any source. Independent of --fail-on. Can also be set via \"failOnIncomplete\" in a .updatrrc file.",
        };

        var configPathArgument = new Argument<string>("path")
        {
            Description =
                "Directory to create the .updatrrc file in, or a file path to use directly. Defaults to the current directory.",
            DefaultValueFactory = _ => ".",
        };

        var forceOption = new Option<bool>("--force")
        {
            Description = "Overwrite the file if it already exists.",
        };

        var exampleOption = new Option<bool>("--example")
        {
            Description =
                "Write a populated, realistic example instead of all options present but empty.",
        };

        var initCommand = new Command(
            "init",
            "Create a .updatrrc file with all options present, but empty."
        )
        {
            configPathArgument,
            forceOption,
            exampleOption,
        };

        initCommand.SetAction(
            (parseResult, cancellationToken) =>
                InitConfigAsync(
                    path: parseResult.GetValue(configPathArgument) ?? ".",
                    force: parseResult.GetValue(forceOption),
                    example: parseResult.GetValue(exampleOption)
                )
        );

        var validatePathArgument = new Argument<string>("path")
        {
            Description =
                "Path to a .updatrrc file, or a directory to look for one in. Defaults to the current directory.",
            DefaultValueFactory = _ => ".",
        };

        var validateCommand = new Command("validate", "Validate a .updatrrc file.")
        {
            validatePathArgument,
        };

        validateCommand.SetAction(
            (parseResult, cancellationToken) =>
                ValidateConfigAsync(path: parseResult.GetValue(validatePathArgument) ?? ".")
        );

        var configCommand = new Command("config", "Manage the .updatrrc config file.")
        {
            initCommand,
            validateCommand,
        };

        var rootCommand = new RootCommand("Update all packages in solution or project(s).")
        {
            pathArgument,
            packageOption,
            excludePackageOption,
            outputOption,
            outputPathOption,
            titleOption,
            descriptionOption,
            verbosityOption,
            dryRunOption,
            prereleaseOption,
            browserOption,
            interactiveOption,
            tfmOption,
            allowedLicensesOption,
            excludeFileOption,
            alignWithTfmOption,
            failOnOption,
            failOnIncompleteOption,
            configCommand,
        };

        rootCommand.SetAction(
            (parseResult, cancellationToken) =>
                RunAsync(
                    path: parseResult.GetValue(pathArgument) ?? ".",
                    package: parseResult.GetValue(packageOption),
                    excludePackage: parseResult.GetValue(excludePackageOption),
                    output: parseResult.GetValue(outputOption),
                    outputPath: parseResult.GetValue(outputPathOption),
                    title: parseResult.GetValue(titleOption),
                    description: parseResult.GetValue(descriptionOption),
                    verbosity: parseResult.GetValue(verbosityOption),
                    dryRun: parseResult.GetValue(dryRunOption),
                    prerelease: parseResult.GetValue(prereleaseOption),
                    browser: parseResult.GetValue(browserOption),
                    interactive: parseResult.GetValue(interactiveOption),
                    tfm: parseResult.GetValue(tfmOption),
                    allowedLicenses: parseResult.GetValue(allowedLicensesOption),
                    excludeFile: parseResult.GetValue(excludeFileOption),
                    alignWithTfm: parseResult.GetValue(alignWithTfmOption),
                    failOn: parseResult.GetValue(failOnOption),
                    failOnIncomplete: parseResult.GetValue(failOnIncompleteOption),
                    cancellationToken: cancellationToken
                )
        );

        return rootCommand.Parse(args).InvokeAsync();
    }

    private static Task<int> InitConfigAsync(string path, bool force, bool example)
    {
        try
        {
            var filePath = UpdatRConfig.CreateFile(path, overwrite: force, example: example);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Created '{filePath}'.");
            Console.ResetColor();

            return Task.FromResult(0);
        }
        catch (IOException exception)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(exception.Message);
            Console.ResetColor();

            return Task.FromResult(1);
        }
    }

    private static Task<int> ValidateConfigAsync(string path)
    {
        var filePath = Directory.Exists(path) ? Path.Combine(path, UpdatRConfig.FileName) : path;

        if (!File.Exists(filePath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"'{filePath}' not found.");
            Console.ResetColor();

            return Task.FromResult(1);
        }

        var json = File.ReadAllText(filePath);
        var configDirectory = new FileInfo(filePath).DirectoryName;
        var errors = UpdatRConfig.Validate(json, configDirectory);

        if (errors.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"'{filePath}' is invalid:");

            foreach (var error in errors)
            {
                Console.WriteLine($"- {error}");
            }

            Console.ResetColor();

            return Task.FromResult(1);
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"'{filePath}' is valid.");
        Console.ResetColor();

        return Task.FromResult(0);
    }

    /// <exception cref="ArgumentException"></exception>
    private static async Task<int> RunAsync(
        string? path = ".",
        string[]? package = null,
        string[]? excludePackage = null,
        OutputFormat output = OutputFormat.Text,
        string? outputPath = null,
        string? title = null,
        string? description = null,
        LogLevel verbosity = LogLevel.Warning,
        bool dryRun = false,
        bool prerelease = false,
        bool browser = false,
        bool interactive = false,
        string? tfm = null,
        string[]? allowedLicenses = null,
        string[]? excludeFile = null,
        string[]? alignWithTfm = null,
        FailOn? failOn = null,
        bool failOnIncomplete = false,
        CancellationToken cancellationToken = default
    )
    {
        var crashLog = Path.Combine(Path.GetTempPath(), "dotnet-updatr-crash.log");

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            File.AppendAllText(
                crashLog,
                $"{DateTime.UtcNow:o}: Unhandled: {e.ExceptionObject}{Environment.NewLine}"
            );

            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"A crash log has been written to '{crashLog}'.");
            Console.ResetColor();
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            File.AppendAllText(
                crashLog,
                $"{DateTime.UtcNow:o}: Unobserved: {e.Exception}{Environment.NewLine}"
            );
            e.SetObserved();

            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"A crash log has been written to '{crashLog}'.");
            Console.ResetColor();
        };

        var sw = Stopwatch.StartNew();

        var services = new ServiceCollection()
            .AddTransient<Updater>()
            .AddLogging(builder =>
            {
                builder.SetMinimumLevel(verbosity);
                builder.AddConsole(options =>
                {
                    // In JSON mode stdout is reserved for the JSON summary alone, so every log
                    // line - regardless of level - is routed to stderr instead.
                    if (output is OutputFormat.Json)
                    {
                        options.LogToStandardErrorThreshold = LogLevel.Trace;
                    }
                });
            })
            .BuildServiceProvider();

        _logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(Program));

        var update = services.GetRequiredService<Updater>();

        Summary summary;

        try
        {
            summary = await update.UpdateAsync(
                path,
                new UpdateOptions
                {
                    ExcludePackages = excludePackage,
                    Packages = package,
                    DryRun = dryRun,
                    Prerelease = prerelease,
                    Interactive = interactive,
                    TargetFrameworkMoniker = tfm,
                    AllowedLicenses = allowedLicenses,
                    ExcludeFiles = excludeFile,
                    AlignWithTfm = alignWithTfm,
                    FailOn = failOn,

                    // Only override .updatrrc when the flag was actually passed - there's no way
                    // to turn it back off from the command line, same as every other bool flag.
                    FailOnIncomplete = failOnIncomplete ? true : null,
                },
                cancellationToken
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Error.WriteLine("Cancelled.");
            Console.ResetColor();

            return 130;
        }
        catch (UpdatRException exception)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine(exception.Message);
            Console.ResetColor();

            return 1;
        }

        var outputStr = TextFormatter.PlainText(summary);

        if (browser)
        {
            var outputMd = MarkdownFormatter.Generate(summary);

            var htmlPath = Paths.Temporary;

            Directory.CreateDirectory(htmlPath);

            var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
            var html = Markdown.ToHtml(outputMd, pipeline);

            var filePath = Path.Combine(htmlPath, "summary.html");

            await File.WriteAllTextAsync(filePath, html, cancellationToken);

            OpenFile(filePath);
        }

        if (output is OutputFormat.Json)
        {
            // Only the JSON itself goes to stdout - logs and errors above are already routed to
            // stderr so a pipe/script consuming this output only ever sees valid JSON.
            Console.WriteLine(JsonFormatter.Generate(summary));
        }
        else if (!browser)
        {
            WriteSummaryToConsole(outputStr);
        }

        if (outputPath is not null)
        {
            await WriteOutputAsync(
                outputPath,
                "output.md",
                new Dictionary<string, Func<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    [".md"] = () => MarkdownFormatter.Generate(summary),
                    [".txt"] = () => outputStr,
                    [".json"] = () => JsonFormatter.Generate(summary),
                },
                cancellationToken
            );
        }

        if (title is not null)
        {
            await WriteOutputAsync(
                title,
                "title.md",
                new Dictionary<string, Func<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    [".md"] = () => MarkdownFormatter.GenerateTitle(summary),
                },
                cancellationToken
            );
        }

        if (description is not null)
        {
            await WriteOutputAsync(
                description,
                "description.md",
                new Dictionary<string, Func<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    [".md"] = () => MarkdownFormatter.GenerateDescription(summary),
                },
                cancellationToken
            );
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            var elapsedTime = sw.Elapsed.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);

            LogFinished(_logger, elapsedTime);
        }

        if (summary.ShouldFail)
        {
            var incomplete =
                summary.FailOnIncomplete
                && (summary.UnauthorizedSources.Any() || summary.UnknownPackages.Count > 0);

            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine(
                incomplete
                    ? "Failing because \"--fail-on-incomplete\" was set and the run was incomplete (an unauthorized package source, or a package that couldn't be resolved on any source)."
                    : $"Failing because \"--fail-on {summary.FailOn.ToString().ToLowerInvariant()}\" was set and a matching finding was found."
            );
            Console.ResetColor();

            return 2;
        }

        return 0;
    }

    /// <summary>
    /// Writes generated content to <paramref name="path"/>. If <paramref name="path"/> is an
    /// existing directory, or has no extension at all, <paramref name="defaultFileName"/> is
    /// created inside it. Otherwise, <paramref name="path"/>'s extension picks the generator to
    /// use from <paramref name="generatorsByExtension"/>; an unsupported extension throws a
    /// friendly <see cref="UpdatRException"/> instead of writing anything.
    /// </summary>
    /// <exception cref="UpdatRException"></exception>
    private static async Task WriteOutputAsync(
        string path,
        string defaultFileName,
        IReadOnlyDictionary<string, Func<string>> generatorsByExtension,
        CancellationToken cancellationToken
    )
    {
        if (Directory.Exists(path) || string.IsNullOrWhiteSpace(new FileInfo(path).Extension))
        {
            Directory.CreateDirectory(path);

            var defaultExtension = Path.GetExtension(defaultFileName);
            var content = generatorsByExtension[defaultExtension]();

            await File.WriteAllTextAsync(
                Path.Combine(path, defaultFileName),
                content,
                cancellationToken
            );

            return;
        }

        var extension = new FileInfo(path).Extension;

        if (!generatorsByExtension.TryGetValue(extension, out var generate))
        {
            throw new UpdatRException(
                $"Unsupported file extension '{extension}' for '{path}'. Supported extensions: "
                    + string.Join(", ", generatorsByExtension.Keys)
                    + "."
            );
        }

        // Path.GetDirectoryName is null for a bare file name in the current directory, and empty
        // for a rooted path's root - neither needs creating.
        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path, generate(), cancellationToken);
    }

    private static void WriteSummaryToConsole(string summary)
    {
        var output = summary.Split(Environment.NewLine);

        for (int i = 0; i < output.Length; i++)
        {
            if (i is >= 0 and <= 2)
            {
                Console.ForegroundColor = ConsoleColor.Green;
            }
            else
            {
                Console.ResetColor();
            }

            Console.WriteLine(output[i]);
        }
    }

    private static void OpenFile(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Process.Start("xdg-open", path);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Process.Start("open", path);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Finished after {ElapsedTime}.")]
    static partial void LogFinished(ILogger logger, string elapsedTime);
}
