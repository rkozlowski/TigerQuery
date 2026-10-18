using Microsoft.Extensions.Logging;
using NLog;
using NLog.Config;
using NLog.Extensions.Logging;
using NLog.Targets;

namespace ItTiger.TigerSqlCmd.Logging;


public static class NLogSetup
{
    /// <summary>
    /// How a logged exception is written: the type and message of the exception and of each
    /// exception that caused it, which is what says what failed and why. The exception is never
    /// serialized raw: <see cref="Exception.ToString"/> adds stack traces, and a type's own
    /// override can add whatever state that type chooses to show.
    /// </summary>
    internal const string ExceptionLayout =
        "${exception:format=Type,Message:separator=\\: :innerFormat=Type,Message:maxInnerExceptionLevel=10:innerExceptionSeparator= ---> }";

    public static ILoggerFactory CreateLoggerFactory(string? logFilePath, Microsoft.Extensions.Logging.LogLevel minLevel)
    {
        var config = new LoggingConfiguration();

        if (!string.IsNullOrWhiteSpace(logFilePath))
        {
            var fileTarget = new FileTarget("logfile")
            {
                FileName = logFilePath,
                Layout = "${longdate} ${uppercase:${level}} ${message} " + ExceptionLayout
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
