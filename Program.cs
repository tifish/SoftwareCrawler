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
        Logger.Information("Program starts with arguments: downloadAll={DownloadAll}, autoClose={AutoClose}, forceClose={ForceClose}", downloadAll, autoClose, forceClose);

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();
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
