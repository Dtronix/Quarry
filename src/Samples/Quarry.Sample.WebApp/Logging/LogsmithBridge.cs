using System.Text;
using Quarry.Logging;
using MsLogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Quarry.Sample.WebApp.Logging;

public sealed class LogsmithBridge(ILoggerFactory loggerFactory) : ILogsmithLogger
{
    public bool IsEnabled(Quarry.Logging.LogLevel level, string category)
    {
        var logger = loggerFactory.CreateLogger(category);
        return logger.IsEnabled(MapLevel(level));
    }

    public void Write(in LogEntry entry, ReadOnlySpan<byte> utf8Message)
    {
        var logger = loggerFactory.CreateLogger(entry.Category);
        var msLevel = MapLevel(entry.Level);

        if (!logger.IsEnabled(msLevel))
            return;

        var message = Encoding.UTF8.GetString(utf8Message);
        logger.Log(msLevel, entry.Exception, "{Message}", message);
    }

    private static MsLogLevel MapLevel(Quarry.Logging.LogLevel level) => level switch
    {
        Quarry.Logging.LogLevel.Trace => MsLogLevel.Trace,
        Quarry.Logging.LogLevel.Debug => MsLogLevel.Debug,
        Quarry.Logging.LogLevel.Information => MsLogLevel.Information,
        Quarry.Logging.LogLevel.Warning => MsLogLevel.Warning,
        Quarry.Logging.LogLevel.Error => MsLogLevel.Error,
        _ => MsLogLevel.None,
    };
}
