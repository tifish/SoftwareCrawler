using System.Text;

namespace SoftwareCrawler;

static class Program
{
    /// <summary>
    ///     The main entry point for the application.
    /// </summary>
    [STAThread]
    private static void Main(
        bool downloadAll,
        bool autoClose,
        bool forceClose)
    {
        Logger.Information($"Program starts with arguments: downloadAll={downloadAll}, autoClose={autoClose}, forceClose={forceClose}.");
            
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();
        var mainForm = new MainForm();
        Application.Idle += ApplicationOnIdle;
        Application.Run(mainForm);

        async void ApplicationOnIdle(object? sender, EventArgs e)
        {
            Application.Idle -= ApplicationOnIdle;

            if (downloadAll)
            {
                var success = await mainForm.DownloadAll();
                if (forceClose || (success && autoClose))
                    mainForm.Close();
            }
        }
            
        Logger.Information("Program ends.");
    }
}