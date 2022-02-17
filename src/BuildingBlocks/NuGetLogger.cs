namespace BuildingBlocks;

internal class NuGetLogger : NuGet.Common.LoggerBase
{
    private readonly NuGet.Common.LogLevel _logLevel;

    public NuGetLogger(LogLevel logLevel)
    {
        _logLevel = TranslateVerbosity(logLevel);
    }

    public override void Log(NuGet.Common.ILogMessage message)
    {
        if (message.Level < _logLevel)
        {
            return;
        }

        Console.WriteLine($"{message.Level}: {message.Message}");
    }

    public override Task LogAsync(NuGet.Common.ILogMessage message)
    {
        Log(message);

        return Task.CompletedTask;
    }

    private static NuGet.Common.LogLevel TranslateVerbosity(LogLevel verbosity) => verbosity switch
    {
        LogLevel.Debug => NuGet.Common.LogLevel.Debug,
        LogLevel.Verbose => NuGet.Common.LogLevel.Verbose,
        LogLevel.Information => NuGet.Common.LogLevel.Information,
        LogLevel.Minimal => NuGet.Common.LogLevel.Minimal,
        LogLevel.Warning => NuGet.Common.LogLevel.Warning,
        LogLevel.Error => NuGet.Common.LogLevel.Error,
        _ => throw new NotImplementedException("Unknown verbosity."),
    };
}
