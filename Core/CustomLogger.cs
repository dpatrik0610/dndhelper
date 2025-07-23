using Serilog;
using Serilog.Events;
using System;
using System.IO;
using System.Text;

namespace dndhelper.Core
{
    public static class CustomLogger
    {
        public static ILogger CreateLogger()
        {
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "dndhelper.log");
            string path = Path.GetDirectoryName(logPath) ?? throw new Exception("Couldn't Find Path.");
            Directory.CreateDirectory(path);

            Console.OutputEncoding = Encoding.UTF8;
            return new LoggerConfiguration()
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss}] {Level:u3}: {Message:lj}{NewLine}",
                    theme: Serilog.Sinks.SystemConsole.Themes.AnsiConsoleTheme.Code)
                .WriteTo.File(
                    path: logPath,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss}] {Level:u3} {Message:lj}{NewLine}",
                    rollingInterval: RollingInterval.Day,
                    encoding: Encoding.UTF8)
                .CreateLogger();
        }
    }
}