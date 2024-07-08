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

    /// <summary>
    /// Update all packages in solution or project(s).
    /// </summary>
    /// <param name="args">Path to solution or project(s). Defaults to current folder. Target can be a specific file or folder. If target is a folder then all *.csproj-files and dotnet-config.json-files will be processed.</param>
    /// <param name="package">Package to update. Supports * as wildcard. Will update all unless specified.</param>
    /// <param name="excludePackage">Package to exclude. Supports * as wildcard.</param>
    /// <param name="output">Defaults to "output.md". Explicitly set to fileName.txt to generate plain text instead of markdown.</param>
    /// <param name="title">Outputs title to path.</param>
    /// <param name="description">Outputs description to path.</param>
    /// <param name="verbosity">Log level.</param>
    /// <param name="dryRun">Do not save any changes.</param>
    /// <param name="browser">Open summary in browser.</param>
    /// <param name="interactive">Interaction with user is possible.</param>
    /// <param name="tfm">Lowest TFM to support.</param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    internal static async Task Main(
        string? args = ".",
        string[]? package = null,
        string[]? excludePackage = null,
        string? output = null,
        string? title = null,
        string? description = null,
        LogLevel verbosity = LogLevel.Warning,
        bool dryRun = false,
        bool browser = false,
        bool interactive = false,
        string? tfm = null
    )
    {
        var sw = Stopwatch.StartNew();

        var services = new ServiceCollection()
            .AddTransient<Updater>()
            .AddLogging(
                builder =>
                {
                    builder.SetMinimumLevel(verbosity);
                    builder.AddConsole();
                }
            )
            .BuildServiceProvider();

        _logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(Program));

        var update = services.GetRequiredService<Updater>();

        var summary = await update.UpdateAsync(
            args,
            excludePackages: excludePackage,
            packages: package,
            dryRun,
            interactive,
            tfm
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

        LogFinished(_logger, sw.Elapsed.ToString("hh\\:mm\\:ss\\.fff", new CultureInfo("en-US")));
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
