using System.CommandLine;
using System.Text;
using JeekTools;
using Microsoft.Extensions.Logging;
using SoftwareCrawler.Services;
using ZLogger;

namespace SoftwareCrawler;

static class Program
{
    private static readonly ILogger Log = LogManager.CreateLogger(nameof(Program));

    /// <summary>
    ///     The main entry point for the application.
    /// </summary>
    [STAThread]
    public static void Main(string[] args)
    {
        var downloadAllOption = new Option<bool>("--download-all")
        {
            Description = "Download all software",
        };
        var autoCloseOption = new Option<bool>("--auto-close")
        {
            Description = "Auto close after download",
        };
        var forceCloseOption = new Option<bool>("--force-close")
        {
            Description = "Force close after download",
        };
        var trayOption = new Option<bool>("--tray")
        {
            Description = "Start hidden in the notification area",
        };
        var rootCommand = new RootCommand("Software Crawler")
        {
            downloadAllOption,
            autoCloseOption,
            forceCloseOption,
            trayOption,
        };
        var parseResult = rootCommand.Parse(args);
        if (parseResult.Errors.Count > 0)
        {
            Console.WriteLine(parseResult.Errors[0].Message);
            return;
        }
        var downloadAll = parseResult.GetValue(downloadAllOption);
        var autoClose = parseResult.GetValue(autoCloseOption);
        var forceClose = parseResult.GetValue(forceCloseOption);
        var tray = parseResult.GetValue(trayOption);

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        LogManager.MinimumLevel = DebugInstanceContext.IsDebugBuild
            ? LogLevel.Debug
            : LogLevel.Information;
        LogManager.EnableLogging();
        Log.ZLogInformation(
            $"Program starts (build {AutoUpdateService.GetDisplayVersion()}, instance {DebugInstanceContext.InstanceLabel})"
        );

        // A second instance from the same folder would fight this one over the WebView2
        // profile and break every download that needs the proxy.
        using var instanceGuard = SingleInstanceGuard.Acquire();
        if (!instanceGuard.IsOnlyInstance)
        {
            Log.ZLogWarning(
                $"Another instance already runs from {AppContext.BaseDirectory} (pid {instanceGuard.OwnerProcessId}); this one exits so the two do not share the WebView2 profile"
            );
            if (!downloadAll)
                instanceGuard.TryActivateOwnerWindow();
            LogManager.Shutdown();
            return;
        }

        // Pick up settings and list edits made outside the app.
        ConfigChangeMonitor.Watch(
            SettingsStore.ResolveConfigRoot(),
            SoftwareManager.WatchedTemplateFolder
        );

        // Only Debug builds actually listen; the call is a no-op otherwise.
        DebugMcpServer.Start();

        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();
        Application.SetColorMode(Settings.ColorMode);
        var mainForm = new MainForm();

        // A plain launch is resident: it keeps the tray icon and runs both schedules.
        // --download-all is the one-shot shape and stays exactly as it was.
        mainForm.ConfigureResidentMode(resident: !downloadAll, startHidden: tray);

        Application.Idle += ApplicationOnIdle;
        Application.Run(mainForm);

        DebugMcpServer.Stop();
        ConfigChangeMonitor.Stop();
        Log.ZLogInformation($"Program ends");
        LogManager.Shutdown();
        return;

        async void ApplicationOnIdle(object? sender, EventArgs e)
        {
            try
            {
                Application.Idle -= ApplicationOnIdle;

                // A tray start never shows the form, so Load never fires and nothing
                // would build the browser or start the scheduler.
                await mainForm.EnsureInitializedAsync();

                if (downloadAll)
                {
                    var success = await mainForm.DownloadAll();
                    if (forceClose || (success && autoClose))
                        mainForm.Close();
                }
            }
            catch (Exception ex)
            {
                Log.ZLogError(ex, $"An error occurred in ApplicationOnIdle");
                MessageBox.Show(ex.Message, "", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
