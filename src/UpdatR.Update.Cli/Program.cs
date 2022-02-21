using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using BuildingBlocks;
using UpdatR.Update.Formatters;

namespace UpdatR.Update.Cli;

internal static partial class Program
{
    private static ILogger _logger = null!;

    /// <summary>
    /// Update all packages in solution or project(s).
    /// </summary>
    /// <param name="target">Path to solution or project(s). Exclude if solution or project(s) is in current folder or if project(s) is in subfolders.</param>
    /// <param name="verbosity">Log level</param>
    /// <param name="dryRun">Do not save any changes.</param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    internal static async Task Main(
        string? target = null,
        Microsoft.Extensions.Logging.LogLevel verbosity = Microsoft.Extensions.Logging.LogLevel.Warning,
        bool dryRun = false)
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

        var summary = await update.UpdateAsync(target, dryRun);

        WriteSummaryToConsole(summary);

#pragma warning disable CA1305 // Specify IFormatProvider
#pragma warning disable CA1848 // Use the LoggerMessage delegates
        Console.ForegroundColor = ConsoleColor.Green;
        _logger.LogTrace("Finished after {ElapsedTime}.", sw.Elapsed.ToString("hh\\:mm\\:ss\\.fff"));
#pragma warning restore CA1848 // Use the LoggerMessage delegates
#pragma warning restore CA1305 // Specify IFormatProvider
    }

    private static void WriteSummaryToConsole(Summary summary)
    {
        var output = TextFormatter
            .PlainText(summary)
            .Split(Environment.NewLine);

        for (int i = 0; i < output.Length; i++)
        {
            if (i is >= 0 and <= 3)
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
