using System.Diagnostics;
using System.Runtime.InteropServices;
using BuildingBlocks;
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
    /// <param name="dry">Do not save any changes.</param>
    /// <param name="browser">Open summary in browser.</param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    internal static async Task Main(
        string? target = null,
        string? output = null,
        Microsoft.Extensions.Logging.LogLevel verbosity = Microsoft.Extensions.Logging.LogLevel.Warning,
        bool dry = false,
        bool browser = false)
    {
        var sw = Stopwatch.StartNew();

        var services = new ServiceCollection()
            .AddTransient<Update>()
            .AddTransient<NuGet.Common.ILogger>(provider
                => verbosity == Microsoft.Extensions.Logging.LogLevel.None
                    ? new NuGet.Common.NullLogger()
                    : new NuGetLogger(provider.GetRequiredService<ILogger<NuGetLogger>>()))
            .AddLogging(builder =>
            {
                builder.SetMinimumLevel(verbosity);
                builder.AddConsole();
            })
            .BuildServiceProvider();

        _logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(Program));

        var update = services.GetRequiredService<Update>();

        update.LogMessage += ReceivedLogMessage;

        var summary = await update.UpdateAsync(target, dry);

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
                await File.WriteAllTextAsync(Path.Combine(output, "output.md"), outputStr);
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

#pragma warning disable CA1305 // Specify IFormatProvider
#pragma warning disable CA1848 // Use the LoggerMessage delegates
        Console.ForegroundColor = ConsoleColor.Green;
        _logger.LogTrace("Finished after {ElapsedTime}.", sw.Elapsed.ToString("hh\\:mm\\:ss\\.fff"));
#pragma warning restore CA1848 // Use the LoggerMessage delegates
#pragma warning restore CA1305 // Specify IFormatProvider
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
            throw new PlatformNotSupportedException("Not yet supported. PR's are welcome!");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            throw new PlatformNotSupportedException("Not yet supported. PR's are welcome!");
        }
    }

    private static void ReceivedLogMessage(object? _, Update.LogMessageEventArgs e)
    {
        LogUpdate(_logger, TranslateVerbosity(e.Level), e.Message);

        static Microsoft.Extensions.Logging.LogLevel TranslateVerbosity(LogLevel verbosity) => verbosity switch
        {
            LogLevel.Trace => Microsoft.Extensions.Logging.LogLevel.Trace,
            LogLevel.Debug => Microsoft.Extensions.Logging.LogLevel.Debug,
            LogLevel.Information => Microsoft.Extensions.Logging.LogLevel.Information,
            LogLevel.Warning => Microsoft.Extensions.Logging.LogLevel.Warning,
            LogLevel.Error => Microsoft.Extensions.Logging.LogLevel.Error,
            LogLevel.None => Microsoft.Extensions.Logging.LogLevel.None,
            _ => throw new NotImplementedException("Unknown verbosity."),
        };
    }

#pragma warning disable IDE0060 // Remove unused parameter
    [LoggerMessage(EventId = 0, Message = "update: {Message}`")]
    static partial void LogUpdate(ILogger logger, Microsoft.Extensions.Logging.LogLevel level, string message);
#pragma warning restore IDE0060 // Remove unused parameter
}
