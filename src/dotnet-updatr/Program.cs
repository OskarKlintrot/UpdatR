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
                "Path to solution or project(s). Defaults to current folder. Target can be a specific file or folder. If target is a folder then all *.csproj-files, dotnet-tools.json-files and file-based apps will be processed.",
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

        var outputOption = new Option<string?>("--output")
        {
            Description =
                "Writes the summary to a file. If an existing directory is given, an \"output.md\" file is created there. If a file path is given, its extension decides the format: \".md\" for markdown or \".txt\" for plain text.",
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

        var initCommand = new Command(
            "init",
            "Create a .updatrrc file with all properties present, but empty."
        )
        {
            configPathArgument,
            forceOption,
        };

        initCommand.SetAction(
            (parseResult, cancellationToken) =>
                InitConfigAsync(
                    path: parseResult.GetValue(configPathArgument) ?? ".",
                    force: parseResult.GetValue(forceOption)
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
            titleOption,
            descriptionOption,
            verbosityOption,
            dryRunOption,
            prereleaseOption,
            browserOption,
            interactiveOption,
            tfmOption,
            allowedLicensesOption,
            configCommand,
        };

        rootCommand.SetAction(
            (parseResult, cancellationToken) =>
                RunAsync(
                    path: parseResult.GetValue(pathArgument) ?? ".",
                    package: parseResult.GetValue(packageOption),
                    excludePackage: parseResult.GetValue(excludePackageOption),
                    output: parseResult.GetValue(outputOption),
                    title: parseResult.GetValue(titleOption),
                    description: parseResult.GetValue(descriptionOption),
                    verbosity: parseResult.GetValue(verbosityOption),
                    dryRun: parseResult.GetValue(dryRunOption),
                    prerelease: parseResult.GetValue(prereleaseOption),
                    browser: parseResult.GetValue(browserOption),
                    interactive: parseResult.GetValue(interactiveOption),
                    tfm: parseResult.GetValue(tfmOption),
                    allowedLicenses: parseResult.GetValue(allowedLicensesOption)
                )
        );

        return rootCommand.Parse(args).InvokeAsync();
    }

    private static Task<int> InitConfigAsync(string path, bool force)
    {
        try
        {
            var filePath = UpdatRConfig.CreateFile(path, overwrite: force);

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
        var errors = UpdatRConfig.Validate(json);

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
        string? output = null,
        string? title = null,
        string? description = null,
        LogLevel verbosity = LogLevel.Warning,
        bool dryRun = false,
        bool prerelease = false,
        bool browser = false,
        bool interactive = false,
        string? tfm = null,
        string[]? allowedLicenses = null
    )
    {
        var crashLog = Path.Combine(Path.GetTempPath(), "dotnet-updatr-crash.log");

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            File.AppendAllText(
                crashLog,
                $"{DateTime.UtcNow:o}: Unhandled: {e.ExceptionObject}{Environment.NewLine}"
            );
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            File.AppendAllText(
                crashLog,
                $"{DateTime.UtcNow:o}: Unobserved: {e.Exception}{Environment.NewLine}"
            );
            e.SetObserved();
        };

        var sw = Stopwatch.StartNew();

        var services = new ServiceCollection()
            .AddTransient<Updater>()
            .AddLogging(builder =>
            {
                builder.SetMinimumLevel(verbosity);
                builder.AddConsole();
            })
            .BuildServiceProvider();

        _logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(Program));

        var update = services.GetRequiredService<Updater>();

        var summary = await update.UpdateAsync(
            path: path,
            excludePackages: excludePackage,
            packages: package,
            dryRun: dryRun,
            prerelease: prerelease,
            interactive: interactive,
            targetFrameworkMoniker: tfm,
            allowedLicenses: allowedLicenses
        );

        var outputStr = TextFormatter.PlainText(summary);

        if (browser)
        {
            var outputMd = MarkdownFormatter.Generate(summary);

            var htmlPath = Paths.Temporary;

            Directory.CreateDirectory(htmlPath);

            var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
            var html = Markdown.ToHtml(outputMd, pipeline);

            var filePath = Path.Combine(htmlPath, "summary.html");

            await File.WriteAllTextAsync(filePath, html);

            OpenFile(filePath);
        }
        else
        {
            WriteSummaryToConsole(outputStr);
        }

        if (output is not null)
        {
            if (string.IsNullOrWhiteSpace(new FileInfo(output).Extension))
            {
                await File.WriteAllTextAsync(
                    Path.Combine(output, "output.md"),
                    MarkdownFormatter.Generate(summary)
                );
            }
            else
            {
                outputStr = new FileInfo(output).Extension switch
                {
                    ".txt" => outputStr,
                    ".md" => MarkdownFormatter.Generate(summary),
                    _ => throw new NotImplementedException(),
                };

                await File.WriteAllTextAsync(output, outputStr);
            }
        }

        if (title is not null)
        {
            if (string.IsNullOrWhiteSpace(new FileInfo(title).Extension))
            {
                await File.WriteAllTextAsync(
                    Path.Combine(title, "title.md"),
                    MarkdownFormatter.GenerateTitle(summary)
                );
            }
            else if (
                new FileInfo(title).Extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
            )
            {
                await File.WriteAllTextAsync(title, MarkdownFormatter.GenerateTitle(summary));
            }
            else
            {
                throw new InvalidOperationException(
                    "Unsupported file extension. Only .md is supported."
                );
            }
        }

        if (description is not null)
        {
            if (string.IsNullOrWhiteSpace(new FileInfo(description).Extension))
            {
                await File.WriteAllTextAsync(
                    Path.Combine(description, "description.md"),
                    MarkdownFormatter.GenerateDescription(summary)
                );
            }
            else if (
                new FileInfo(description).Extension.Equals(
                    ".md",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                await File.WriteAllTextAsync(
                    description,
                    MarkdownFormatter.GenerateDescription(summary)
                );
            }
            else
            {
                throw new InvalidOperationException(
                    "Unsupported file extension. Only .md is supported."
                );
            }
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            var elapsedTime = sw.Elapsed.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);

            LogFinished(_logger, elapsedTime);
        }

        return 0;
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
            Process.Start("cmd.exe ", "/c " + path);
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
