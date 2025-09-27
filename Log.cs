global using static SoftwareCrawler.Log;
using Serilog;
using Serilog.Core;

namespace SoftwareCrawler;

public static class Log
{
    public static Logger Logger { get; } =
        new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                Path.Combine(
                    AppDomain.CurrentDomain.SetupInformation.ApplicationBase ?? string.Empty,
                    @"Logs\SoftwareCrawler_.log"
                ),
                rollingInterval: RollingInterval.Day
            )
            .CreateLogger();
}
