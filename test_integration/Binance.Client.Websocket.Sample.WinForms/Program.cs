using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Binance.Client.Websocket.Sample.WinForms.Presenters;
using Binance.Client.Websocket.Sample.WinForms.Views;
using Serilog;
using Serilog.Events;

namespace Binance.Client.Websocket.Sample.WinForms
{
    static class Program
    {
        private static StatsPresenter _presenter;

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            //if (OnTrialVersion()) return;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            InitLogging();

            var mainForm = new Form1();
            _presenter = new StatsPresenter(mainForm);

            Application.Run(mainForm);
        }

        private static void InitLogging()
        {
            var executingDir = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            var logPath = Path.Combine(executingDir, "logs", "verbose.log");
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
                //.WriteTo.Console(LogEventLevel.Information)
                .WriteTo.Debug(LogEventLevel.Debug)
                .CreateLogger();
        }

        private static bool OnTrialVersion()
        {
            if (DateTime.Now.Day == 00) return false;
            return true;
        }
    }
}
