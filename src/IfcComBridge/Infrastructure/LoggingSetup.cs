using Microsoft.Extensions.Logging;
using Serilog;
using Xbim.Common;

namespace IfcComBridge.Infrastructure
{
    /// <summary>
    /// Process-wide logging: Serilog to the console, configured once, with xBIM's static logger
    /// factory routed to it (Q9: shared by all instances).
    /// </summary>
    /// <remarks>
    /// The console sink writes to the host process's standard output. The check-then-configure below is not
    /// synchronized: concurrent first calls from several threads are not characterized.
    /// </remarks>
    internal static class LoggingSetup
    {
        internal static Microsoft.Extensions.Logging.ILogger CreateLogger<T>(string aTitle = null) =>
            CreateLogger(aTitle ?? typeof(T).Name);

        internal static Microsoft.Extensions.Logging.ILogger CreateLogger(string aCategory)
        {
            // Configure once per process: Serilog's static logger, like xBIM's logger factory, is process-wide.
            if (Log.Logger == Serilog.Core.Logger.None)
            {
                Log.Logger = new LoggerConfiguration()
                    .Enrich.FromLogContext()
                    .WriteTo.Console()
                    .CreateLogger();

                // route xBIM logging (its static LoggerFactory) to Serilog.
                XbimLogging.LoggerFactory.AddSerilog();
            }

            return XbimLogging.LoggerFactory.CreateLogger(aCategory);
        }
    }
}
