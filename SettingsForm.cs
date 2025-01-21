namespace SoftwareCrawler;

public partial class SettingsForm : Form
{
    private readonly SettingsObject _settings;


    public SettingsForm()
    {
        InitializeComponent();
        _settings = Settings;

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

        await _settings.Save();
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
