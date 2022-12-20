using Microsoft.Extensions.Logging;

namespace BuildingBlocks;

internal sealed partial class NuGetLogger : NuGet.Common.LoggerBase
{
    private readonly ILogger _logger;

    public NuGetLogger(ILogger logger)
    {
        _logger = logger;
    }

    public override void Log(NuGet.Common.ILogMessage message)
    {
        var logLevel = TranslateVerbosity(message.Level);

        Log(_logger, logLevel, message.Level, message.Message);
    }

    public override Task LogAsync(NuGet.Common.ILogMessage message)
    {
        Log(message);

        return Task.CompletedTask;
    }

    private static LogLevel TranslateVerbosity(NuGet.Common.LogLevel verbosity) => verbosity switch
    {
        NuGet.Common.LogLevel.Debug => LogLevel.Trace,
        NuGet.Common.LogLevel.Verbose => LogLevel.Debug,
        NuGet.Common.LogLevel.Information => LogLevel.Information,
        NuGet.Common.LogLevel.Minimal => LogLevel.Warning,
        NuGet.Common.LogLevel.Warning => LogLevel.Warning,
        NuGet.Common.LogLevel.Error => LogLevel.Error,
        _ => throw new NotImplementedException("Unknown verbosity."),
    };

    [LoggerMessage(EventId = 0, Message = "nuget: ({NuGetLogLevel}): {Message}")]
    static partial void Log(ILogger logger, LogLevel level, NuGet.Common.LogLevel nuGetLogLevel, string message);
}
