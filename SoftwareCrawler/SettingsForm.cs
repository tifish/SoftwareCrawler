using System.Diagnostics;
using JeekTools;
using Microsoft.Extensions.Logging;
using SoftwareCrawler.Models;
using SoftwareCrawler.Services;
using ZLogger;

namespace SoftwareCrawler;

public partial class SettingsForm : Form
{
    private static readonly ILogger Log = LogManager.CreateLogger(nameof(SettingsForm));

    private readonly AppSettings _settings;

    private sealed record ColorModeOption(string DisplayName, SystemColorMode Mode);

    private sealed record StorageLocationOption(string DisplayName, StorageLocation Location);

    private sealed record UpdateCheckOption(string DisplayName, UpdateCheckFrequency Frequency);

    private static readonly ColorModeOption[] ColorModeOptions =
    [
        new("Follow system", SystemColorMode.System),
        new("Dark", SystemColorMode.Dark),
        new("Light", SystemColorMode.Classic),
    ];

    private static readonly StorageLocationOption[] StorageLocationOptions =
    [
        new("Default (AppData)", StorageLocation.UserDirectory),
        new("Portable (program folder)", StorageLocation.ProgramDirectory),
        new("Custom folder", StorageLocation.CustomDirectory),
    ];

    private static readonly UpdateCheckOption[] UpdateCheckOptions =
    [
        new("Never", UpdateCheckFrequency.Never),
        new("Every 6 hours", UpdateCheckFrequency.EverySixHours),
        new("Daily", UpdateCheckFrequency.Daily),
        new("Weekly", UpdateCheckFrequency.Weekly),
    ];

    public SettingsForm()
    {
        InitializeComponent();
        _settings = Settings;

        colorModeComboBox.DisplayMember = nameof(ColorModeOption.DisplayName);
        colorModeComboBox.ValueMember = nameof(ColorModeOption.Mode);
        colorModeComboBox.DataSource = ColorModeOptions;
        colorModeComboBox.SelectedValue = _settings.ColorMode;

        storageLocationComboBox.DisplayMember = nameof(StorageLocationOption.DisplayName);
        storageLocationComboBox.ValueMember = nameof(StorageLocationOption.Location);
        storageLocationComboBox.DataSource = StorageLocationOptions;
        storageLocationComboBox.SelectedValue = SettingsStore.CurrentStorageLocation;

        updateCheckComboBox.DisplayMember = nameof(UpdateCheckOption.DisplayName);
        updateCheckComboBox.ValueMember = nameof(UpdateCheckOption.Frequency);
        updateCheckComboBox.DataSource = UpdateCheckOptions;
        updateCheckComboBox.SelectedValue = _settings.UpdateCheckFrequency;

        versionValueLabel.Text = AutoUpdateService.GetDisplayVersion();
        checkUpdateButton.Enabled = !DebugInstanceContext.IsDebugBuild;

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
        customStoragePathTextBox.Text = _settings.CustomStoragePath ?? "";
        scheduledTimesTextBox.Text = string.Join(", ", _settings.ScheduledDownloadTimes);
        frequentCheckIntervalNumericUpDown.Value = _settings.FrequentCheckIntervalMinutes;
        // The Startup folder is the truth, not the setting: the shortcut can be
        // removed behind the app's back by any startup manager.
        runAtStartupCheckBox.Checked = StartupShortcutService.IsEnabled;

        UpdateStorageControls();
    }

    private StorageLocation SelectedStorageLocation =>
        (StorageLocation)storageLocationComboBox.SelectedValue!;

    private void UpdateStorageControls()
    {
        var isCustom = SelectedStorageLocation == StorageLocation.CustomDirectory;
        customStoragePathTextBox.Visible = isCustom;
        browseStoragePathButton.Visible = isCustom;
    }

    private void storageLocationComboBox_SelectedIndexChanged(object sender, EventArgs e) =>
        UpdateStorageControls();

    private void browseStoragePathButton_Click(object sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the folder that will hold the Config folder",
            UseDescriptionForTitle = true,
            SelectedPath = customStoragePathTextBox.Text,
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
            customStoragePathTextBox.Text = dialog.SelectedPath;
    }

    /// <summary>
    /// Reads the comma-separated schedule box. Accepts a missing leading zero
    /// ("9:00") and normalizes it, but rejects anything else rather than dropping
    /// it, so a typo cannot quietly turn into "no run at that time".
    /// </summary>
    private static bool TryParseScheduledTimes(
        string text,
        out List<string> times,
        out string badTime
    )
    {
        times = [];
        badTime = "";

        foreach (
            var part in text.Split(
                [',', ';'],
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries
            )
        )
        {
            if (!TimeOnly.TryParseExact(part, ["HH:mm", "H:mm"], out var time))
            {
                badTime = part;
                return false;
            }

            var normalized = time.ToString("HH:mm");
            if (!times.Contains(normalized))
                times.Add(normalized);
        }

        times.Sort(StringComparer.Ordinal);
        return true;
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
        _settings.UpdateCheckFrequency = (UpdateCheckFrequency)updateCheckComboBox.SelectedValue!;

        if (!TryParseScheduledTimes(scheduledTimesTextBox.Text, out var scheduledTimes, out var badTime))
        {
            // Refusing beats silently dropping it: a time nobody can parse is a run
            // that never happens, and the dialog would close looking like it worked.
            MessageBox.Show(
                this,
                $"'{badTime}' is not a time of day. Use 24-hour HH:mm, separated by commas.",
                "Download all at",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
            scheduledTimesTextBox.Focus();
            return;
        }

        _settings.ScheduledDownloadTimes = scheduledTimes;
        _settings.FrequentCheckIntervalMinutes = (int)frequentCheckIntervalNumericUpDown.Value;

        if (
            runAtStartupCheckBox.Checked != StartupShortcutService.IsEnabled
            && !StartupShortcutService.Apply(runAtStartupCheckBox.Checked, out var startupError)
        )
        {
            MessageBox.Show(
                this,
                $"Could not update the startup shortcut: {startupError}",
                "Start with Windows",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
            return;
        }

        _settings.RunAtStartup = StartupShortcutService.IsEnabled;

        if (!ApplyStorageLocation())
            return;

        var colorMode = (SystemColorMode)colorModeComboBox.SelectedValue!;
        var colorModeChanged = _settings.ColorMode != colorMode;
        _settings.ColorMode = colorMode;

        await Task.Run(() => SettingsStore.SaveIfChanged());
        Application.SetColorMode(Settings.ColorMode);

        if (colorModeChanged)
        {
            var messageBoxResult = MessageBox.Show(
                this,
                "Theme settings have been changed. The application needs to restart to apply the new theme. Would you like to restart now?",
                "Theme Changed",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );
            if (messageBoxResult == DialogResult.Yes)
            {
                SynchronizationContext.Current?.Post(
                    _ =>
                    {
                        Process.Start(
                            new ProcessStartInfo
                            {
                                FileName = Application.ExecutablePath,
                                UseShellExecute = true,
                            }
                        );
                        Application.Exit();
                    },
                    null
                );
            }
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>
    /// Applies a storage-mode change: asks whether to move the existing Config
    /// folder, moves it when asked, and points the settings at the new root.
    /// Only roaming data moves; machine-local settings stay where they are.
    /// Returns false when the user cancelled and the dialog should stay open.
    /// </summary>
    private bool ApplyStorageLocation()
    {
        var currentLocation = SettingsStore.CurrentStorageLocation;
        var currentCustomPath = _settings.CustomStoragePath;
        var newLocation = SelectedStorageLocation;
        var newCustomPath = customStoragePathTextBox.Text.Trim();

        if (newLocation == StorageLocation.CustomDirectory && newCustomPath.Length == 0)
        {
            MessageBox.Show(
                this,
                "Choose a folder for the custom storage location.",
                "Settings storage",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
            return false;
        }

        var sourceRoot = SettingsService.ResolveConfigRoot(currentLocation, currentCustomPath);
        var destRoot = SettingsService.ResolveConfigRoot(
            newLocation,
            newLocation == StorageLocation.CustomDirectory ? newCustomPath : null
        );

        if (string.Equals(sourceRoot, destRoot, StringComparison.OrdinalIgnoreCase))
        {
            _settings.StorageLocation = newLocation;
            _settings.CustomStoragePath =
                newLocation == StorageLocation.CustomDirectory ? newCustomPath : null;
            return true;
        }

        // Leaving portable mode without moving the folder is pointless: the
        // executable-side Config folder would force portable mode again on the
        // next start, so that combination is not offered.
        var mustMove = currentLocation == StorageLocation.ProgramDirectory;
        var prompt = mustMove
            ? $"Move the existing settings from\n{sourceRoot}\nto\n{destRoot}?\n\n"
                + "Leaving portable mode requires moving the folder; otherwise the app switches back to portable on the next start."
            : $"Move the existing settings from\n{sourceRoot}\nto\n{destRoot}?\n\n"
                + "Choose No to leave the files in place and just use the new location.";

        var answer = MessageBox.Show(
            this,
            prompt,
            "Settings storage",
            mustMove ? MessageBoxButtons.OKCancel : MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question
        );

        if (answer is DialogResult.Cancel)
            return false;

        var move = answer is DialogResult.Yes or DialogResult.OK;
        if (move)
        {
            try
            {
                SettingsService.MoveConfigRoot(sourceRoot, destRoot);
                if (
                    currentLocation == StorageLocation.ProgramDirectory
                    && !SettingsService.TryDeleteProgramConfig(out var error)
                )
                {
                    Log.ZLogWarning($"Could not remove the portable Config folder: {error}");
                }
            }
            catch (Exception ex)
            {
                Log.ZLogError(ex, $"Failed to move the Config folder");
                MessageBox.Show(
                    this,
                    $"Could not move the settings folder:\n{ex.Message}",
                    "Settings storage",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return false;
            }
        }

        _settings.StorageLocation = newLocation;
        _settings.CustomStoragePath =
            newLocation == StorageLocation.CustomDirectory ? newCustomPath : null;

        // The portable marker is the folder itself, so create it when switching in.
        if (newLocation == StorageLocation.ProgramDirectory)
            Directory.CreateDirectory(destRoot);

        ConfigChangeMonitor.Watch(destRoot, SoftwareManager.WatchedTemplateFolder);
        return true;
    }

    private async void checkUpdateButton_Click(object sender, EventArgs e)
    {
        checkUpdateButton.Enabled = false;
        try
        {
            var outcome = await AutoUpdateService.HasUpdateAsync();
            switch (outcome)
            {
                case UpdateCheckOutcome.UpToDate:
                    MessageBox.Show(
                        this,
                        "You are running the latest version.",
                        "Check for updates",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );
                    return;
                case UpdateCheckOutcome.Failed:
                    MessageBox.Show(
                        this,
                        $"Could not check for updates: {AutoUpdateService.FailureReason}",
                        "Check for updates",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                    return;
            }

            var install = MessageBox.Show(
                this,
                $"Version {AutoUpdateService.RemoteCommitCount} is available "
                    + $"(current: {AutoUpdateService.LocalCommitCount}).\n\n"
                    + "Download and install it now? The app will restart.",
                "Check for updates",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );
            if (install != DialogResult.Yes)
                return;

            var staged = await AutoUpdateService.DownloadAndStageAsync();
            if (staged is null || !AutoUpdateService.LaunchInstall(staged))
            {
                MessageBox.Show(
                    this,
                    $"Could not download the update: {AutoUpdateService.FailureReason}",
                    "Check for updates",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return;
            }

            Application.Exit();
        }
        finally
        {
            checkUpdateButton.Enabled = !DebugInstanceContext.IsDebugBuild;
        }
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
            Title = "Select External JavaScript Editor",
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            externalJavascriptEditorTextBox.Text = dialog.FileName;
        }
    }
}
