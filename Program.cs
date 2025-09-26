using System.CommandLine;
using System.Text;
using System.CommandLine.NamingConventionBinder;
using System.Windows.Forms;
namespace SoftwareCrawler;

static class Program
{
    /// <summary>
    ///     The main entry point for the application.
    /// </summary>
    [STAThread]
    public static void Main(string[] args)
    {
        var rootCommand = new RootCommand("Software Crawler")
        {
            new Option<bool>("--download-all", "Download all software"),
            new Option<bool>("--auto-close", "Auto close after download"),
            new Option<bool>("--force-close", "Force close after download")
        };
        rootCommand.Handler = CommandHandler.Create<bool, bool, bool>(Run);
        rootCommand.Invoke(args);
    }

    private static void Run(
        bool downloadAll,
        bool autoClose,
        bool forceClose)
    {
        Logger.Information("Program starts with arguments: downloadAll={DownloadAll}, autoClose={AutoClose}, forceClose={ForceClose}", downloadAll, autoClose, forceClose);

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        Settings.Load();

        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();
        Application.SetColorMode(Settings.ColorMode);
        var mainForm = new MainForm();
        Application.Idle += ApplicationOnIdle;
        Application.Run(mainForm);

        Logger.Information("Program ends");
        return;

        async void ApplicationOnIdle(object? sender, EventArgs e)
        {
            try
            {
                Application.Idle -= ApplicationOnIdle;

                if (downloadAll)
                {
                    var success = await mainForm.DownloadAll();
                    if (forceClose || (success && autoClose))
                        mainForm.Close();
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "An error occurred in ApplicationOnIdle");
                MessageBox.Show(ex.Message, "", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
