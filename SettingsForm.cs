using System.Diagnostics;
using System.Reflection;
namespace SoftwareCrawler;

public partial class SettingsForm : Form
{
    private readonly SettingsObject _settings;
    private sealed record ColorModeOption(string DisplayName, SystemColorMode Mode);

    private static readonly ColorModeOption[] ColorModeOptions =
    [
        new("Follow system", SystemColorMode.System),
        new("Dark", SystemColorMode.Dark),
        new("Light", SystemColorMode.Classic)
    ];

    public SettingsForm()
    {
        InitializeComponent();
        _settings = Settings;

        colorModeComboBox.DisplayMember = nameof(ColorModeOption.DisplayName);
        colorModeComboBox.ValueMember = nameof(ColorModeOption.Mode);
        colorModeComboBox.DataSource = ColorModeOptions;
        colorModeComboBox.SelectedValue = _settings.ColorMode;

        // Load settings into controls
        proxyTextBox.Text = _settings.Proxy;
        downloadRetryCountNumericUpDown.Value = _settings.DownloadRetryCount;
        downloadRetryIntervalNumericUpDown.Value = _settings.DownloadRetryInterval;
        loadPageEndTimeoutNumericUpDown.Value = _settings.LoadPageEndTimeout;
        tryClickCountNumericUpDown.Value = _settings.TryClickCount;
        tryClickIntervalNumericUpDown.Value = _settings.TryClickInterval;
        startDownloadTimeoutNumericUpDown.Value = _settings.StartDownloadTimeout;
        downloadTimeoutNumericUpDown.Value = _settings.DownloadTimeout;
        externalJavascriptEditorTextBox.Text = _settings.ExternalJavascriptEditor;
        defaultDownloadDirectoryTextBox.Text = _settings.DefaultDownloadDirectory;
    }

    private async void okButton_Click(object sender, EventArgs e)
    {
        // Save control values to settings
        _settings.Proxy = proxyTextBox.Text;
        _settings.DownloadRetryCount = (int)downloadRetryCountNumericUpDown.Value;
        _settings.DownloadRetryInterval = (int)downloadRetryIntervalNumericUpDown.Value;
        _settings.LoadPageEndTimeout = (int)loadPageEndTimeoutNumericUpDown.Value;
        _settings.TryClickCount = (int)tryClickCountNumericUpDown.Value;
        _settings.TryClickInterval = (int)tryClickIntervalNumericUpDown.Value;
        _settings.StartDownloadTimeout = (int)startDownloadTimeoutNumericUpDown.Value;
        _settings.DownloadTimeout = (int)downloadTimeoutNumericUpDown.Value;
        _settings.ExternalJavascriptEditor = externalJavascriptEditorTextBox.Text;
        _settings.DefaultDownloadDirectory = defaultDownloadDirectoryTextBox.Text;

        var colorMode = (SystemColorMode)colorModeComboBox.SelectedValue!;
        var colorModeChanged = _settings.ColorMode != colorMode;
        _settings.ColorMode = colorMode;


        await _settings.Save();
        Application.SetColorMode(_settings.ColorMode);

        if (colorModeChanged)
        {
            var messageBoxResult = MessageBox.Show(this, "Theme settings have been changed. The application needs to restart to apply the new theme. Would you like to restart now?", "Theme Changed", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (messageBoxResult == DialogResult.Yes)
            {
                SynchronizationContext.Current?.Post(_ =>
                {
                    var executablePath = Path.ChangeExtension(Assembly.GetEntryAssembly()!.Location, ".exe");
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = executablePath,
                        UseShellExecute = true,
                    });
                    Application.Exit();
                }, null);
            }
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private void cancelButton_Click(object sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void browseButton_Click(object sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
            Title = "Select External JavaScript Editor"
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            externalJavascriptEditorTextBox.Text = dialog.FileName;
        }
    }
}
