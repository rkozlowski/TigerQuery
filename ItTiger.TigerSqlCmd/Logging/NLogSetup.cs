using Microsoft.Extensions.Logging;
using NLog;
using NLog.Config;
using NLog.Extensions.Logging;
using NLog.Targets;

namespace ItTiger.TigerSqlCmd.Logging;


public static class NLogSetup
{
    public static ILoggerFactory CreateLoggerFactory(string? logFilePath, Microsoft.Extensions.Logging.LogLevel minLevel)
    {
        var config = new LoggingConfiguration();

        if (!string.IsNullOrWhiteSpace(logFilePath))
        {
            var fileTarget = new FileTarget("logfile")
            {
                FileName = logFilePath,
                Layout = "${longdate} ${uppercase:${level}} ${message} ${exception:format=ToString}"
            };
            config.AddTarget(fileTarget);
            config.AddRule(minLevel.ToNLogLevel(), NLog.LogLevel.Fatal, fileTarget);
        }

        // Optional: Add console logging too
        /*
        var consoleTarget = new ConsoleTarget("console")
        {
            Layout = "${message}"
        };
        config.AddTarget(consoleTarget);
        config.AddRule(minLevel.ToNLogLevel(), NLog.LogLevel.Fatal, consoleTarget);
        */
        LogManager.Configuration = config;
        return LoggerFactory.Create(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(minLevel);
            builder.AddNLog();
        });
    }

    public static NLog.LogLevel ToNLogLevel(this Microsoft.Extensions.Logging.LogLevel level) => level switch
    {
        Microsoft.Extensions.Logging.LogLevel.Trace => NLog.LogLevel.Trace,
        Microsoft.Extensions.Logging.LogLevel.Debug => NLog.LogLevel.Debug,
        Microsoft.Extensions.Logging.LogLevel.Information => NLog.LogLevel.Info,
        Microsoft.Extensions.Logging.LogLevel.Warning => NLog.LogLevel.Warn,
        Microsoft.Extensions.Logging.LogLevel.Error => NLog.LogLevel.Error,
        Microsoft.Extensions.Logging.LogLevel.Critical => NLog.LogLevel.Fatal,
        _ => NLog.LogLevel.Info
    };
}
