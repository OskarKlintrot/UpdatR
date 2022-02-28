using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Markdig;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UpdatR.Update.Formatters;

namespace UpdatR.Update.Cli;

internal static partial class Program
{
    private static ILogger _logger = null!;

    /// <summary>
    /// Update all packages in solution or project(s).
    /// </summary>
    /// <param name="target">Path to solution or project(s). Exclude if solution or project(s) is in current folder or if project(s) is in subfolders.</param>
    /// <param name="output">Defaults to "output.md". Explicitly set to fileName.txt to generate plain text instead of markdown.</param>
    /// <param name="verbosity">Log level</param>
    /// <param name="dryRun">Do not save any changes.</param>
    /// <param name="browser">Open summary in browser.</param>
    /// <param name="interactive">Interaction with user is possible.</param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    internal static async Task Main(
        string? target = null,
        string? output = null,
        LogLevel verbosity = LogLevel.Warning,
        bool dryRun = false,
        bool browser = false,
        bool interactive = false)
    {
        var sw = Stopwatch.StartNew();

        var services = new ServiceCollection()
            .AddTransient<Update>()
            .AddLogging(builder =>
            {
                builder.SetMinimumLevel(verbosity);
                builder.AddConsole();
            })
            .BuildServiceProvider();

        _logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(Program));

        var update = services.GetRequiredService<Update>();

        var summary = await update.UpdateAsync(target, dryRun, interactive);

        var outputStr = TextFormatter.PlainText(summary);

        if (browser)
        {
            var outputMd = MarkdownFormatter.Generate(summary);

            var htmlPath = Path.Combine(Path.GetTempPath(), "dotnet-updatr");

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
                await File.WriteAllTextAsync(Path.Combine(output, "output.md"), MarkdownFormatter.Generate(summary));
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
