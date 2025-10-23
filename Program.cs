using System.CommandLine;
using System.Text;

namespace SoftwareCrawler;

static class Program
{
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
        var rootCommand = new RootCommand("Software Crawler")
        {
            downloadAllOption,
            autoCloseOption,
            forceCloseOption,
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
