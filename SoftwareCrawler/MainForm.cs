using System.ComponentModel;
using System.Diagnostics;
using JeekTools;
using Microsoft.Extensions.Logging;
using SoftwareCrawler.Models;
using SoftwareCrawler.Services;
using ZLogger;

namespace SoftwareCrawler;

public partial class MainForm : Form
{
    private static readonly ILogger Log = LogManager.CreateLogger(nameof(MainForm));

    private SearchForm? _searchForm;
    private List<(int RowIndex, int ColumnIndex)> _searchResults = [];
    private int _currentSearchResultIndex = -1;
    private System.Windows.Forms.Timer? _updateCheckTimer;
    private GridViewState? _pendingGridViewState;
    private bool _reloadAfterBatchPending;

    private sealed record GridViewState(
        IReadOnlySet<string> SelectedNames,
        string? CurrentName,
        int CurrentColumnIndex,
        string? FirstDisplayedName,
        int FirstDisplayedRowIndex
    );

    public MainForm()
    {
        InitializeComponent();

        Text = DebugInstanceContext.DecorateTitle(Text);
        ConfigChangeMonitor.ConfigChanged += OnConfigChanged;
        SoftwareManager.Reloaded += OnSoftwareListReloaded;

        // Enable double buffering for the data grid view to prevent flickering
        typeof(DataGridView)
            .GetProperty(
                "DoubleBuffered",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
            )!
            .SetValue(softwareListDataGridView, true, null);

        // Add drag and drop support
        softwareListDataGridView.AllowDrop = true;
        softwareListDataGridView.DragDrop += softwareListDataGridView_DragDrop;
        softwareListDataGridView.DragOver += softwareListDataGridView_DragOver;
        softwareListDataGridView.MouseMove += softwareListDataGridView_MouseMove;

        // Enable key events
        KeyPreview = true;
        KeyDown += MainForm_KeyDown;
    }

    private class DownloadUIDisabler : IDisposable
    {
        private readonly MainForm _mainForm;

        public DownloadUIDisabler(MainForm mainForm)
        {
            _mainForm = mainForm;

            _mainForm.downloadSelectedToolStripMenuItem.Enabled = false;
            _mainForm.downloadAllToolStripMenuItem.Enabled = false;
            _mainForm.testSelectedToolStripMenuItem.Enabled = false;
            _mainForm.testAllToolStripMenuItem.Enabled = false;
            _mainForm.reloadToolStripMenuItem.Enabled = false;
            _mainForm.cleanUpLocalSettingsToolStripMenuItem.Enabled = false;

            _mainForm.cancelToolStripMenuItem.Enabled = true;
        }

        public void Dispose()
        {
            _mainForm.downloadSelectedToolStripMenuItem.Enabled = true;
            _mainForm.downloadAllToolStripMenuItem.Enabled = true;
            _mainForm.testSelectedToolStripMenuItem.Enabled = true;
            _mainForm.testAllToolStripMenuItem.Enabled = true;
            _mainForm.reloadToolStripMenuItem.Enabled = true;
            _mainForm.cleanUpLocalSettingsToolStripMenuItem.Enabled = true;

            _mainForm.cancelToolStripMenuItem.Enabled = false;
        }
    }

    private readonly TaskCompletionSource<bool> _onLoadTaskCompletionSource = new();

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);

        ConfigChangeMonitor.ConfigChanged -= OnConfigChanged;
        SoftwareManager.Reloaded -= OnSoftwareListReloaded;
        _updateCheckTimer?.Stop();

        // Ensure any pending debounced save is flushed before the process exits.
        try
        {
            SoftwareManager.FlushAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.ZLogError(ex, $"Failed to flush software list on closing");
        }
    }

    protected override async void OnLoad(EventArgs args)
    {
        try
        {
            base.OnLoad(args);

            using (new DownloadUIDisabler(this))
            {
                var parentForm = new Form();
                await Browser.Init(parentForm);
                parentForm.Size = new Size(1280, 720);

                BringToFront();

                await Reload();
            }

            _onLoadTaskCompletionSource.TrySetResult(true);

            StartUpdateChecks();
        }
        catch (Exception e)
        {
            Log.ZLogError(e, $"An error occurred in OnLoad");
            MessageBox.Show(e.Message, "", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _onLoadTaskCompletionSource.TrySetResult(false);
        }
    }

    /// <summary>
    /// Checks for updates on startup and then on the configured interval.
    /// Debug builds are opted out by <see cref="AutoUpdateService"/> itself.
    /// </summary>
    private void StartUpdateChecks()
    {
        if (Settings.CheckUpdateOnStartup)
            _ = CheckForUpdatesAsync();

        var interval = AutoUpdateService.GetCheckInterval(Settings.UpdateCheckFrequency);
        if (interval is null)
            return;

        _updateCheckTimer = new System.Windows.Forms.Timer
        {
            Interval = (int)Math.Min(interval.Value.TotalMilliseconds, int.MaxValue),
        };
        _updateCheckTimer.Tick += (_, _) => _ = CheckForUpdatesAsync();
        _updateCheckTimer.Start();
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            if (await AutoUpdateService.CheckAndInstallAsync())
                Application.Exit();
        }
        catch (Exception ex)
        {
            Log.ZLogWarning(ex, $"Update check failed");
        }
    }

    /// <summary>
    /// Reloads only what actually changed on disk after an outside edit.
    /// </summary>
    private void OnConfigChanged(IReadOnlyList<string> changedFiles)
    {
        if (IsDisposed || !IsHandleCreated)
            return;

        var names = changedFiles.Select(Path.GetFileName).ToArray();
        var settingsChanged = names.Contains("settings.json", StringComparer.OrdinalIgnoreCase);
        // Both .tab files back the same grid, so either one means a single rebind.
        var listChanged =
            names.Contains("Software.tab", StringComparer.OrdinalIgnoreCase)
            || names.Contains("LocalSettings.tab", StringComparer.OrdinalIgnoreCase);
        if (!settingsChanged && !listChanged)
            return;

        BeginInvoke(async () =>
        {
            if (settingsChanged)
            {
                SettingsStore.ReloadRoamingSettings();
                Application.SetColorMode(Settings.ColorMode);
                Log.ZLogInformation($"Reloaded settings after an outside edit");
            }

            if (listChanged)
            {
                if (DownloadBatch.IsRunning)
                {
                    _reloadAfterBatchPending = true;
                    Log.ZLogInformation(
                        $"Deferred the software list reload until the running batch finishes"
                    );
                    return;
                }

                await Reload();
                Log.ZLogInformation($"Reloaded the software list after an outside edit");
            }
        });
    }

    private Task Reload()
    {
        if (DownloadBatch.IsRunning)
        {
            _reloadAfterBatchPending = true;
            return Task.CompletedTask;
        }

        // SoftwareManager.Load replaces the backing list before it raises Reloaded.
        // Capture the grid while its rows still describe the old list; otherwise a
        // bound list can already expose the new row at the old numeric selection.
        _pendingGridViewState = CaptureGridViewState();
        // Binding is left to OnSoftwareListReloaded, which Load raises, so that a
        // reload started anywhere else reaches the grid just the same.
        return SoftwareManager.Load();
    }

    internal void ReloadSoftwareListFromDebug() => Reload().GetAwaiter().GetResult();

    /// <summary>
    /// Rebinds the grid after the list was reloaded from disk. Raised on whichever
    /// thread did the loading, which is not always this one.
    /// </summary>
    private void OnSoftwareListReloaded()
    {
        if (IsDisposed || !IsHandleCreated)
            return;

        if (InvokeRequired)
        {
            try
            {
                // BeginInvoke, not Invoke: a caller blocking on Load while the UI
                // thread waits here would deadlock.
                BeginInvoke(BindSoftwareList);
            }
            catch (ObjectDisposedException)
            {
                // The form went away between the check and here; closing anyway.
            }
            return;
        }

        BindSoftwareList();
    }

    private void BindSoftwareList()
    {
        var viewState = _pendingGridViewState ?? CaptureGridViewState();
        _pendingGridViewState = null;
        var bindingList = new BindingList<SoftwareItem>(SoftwareManager.Items);
        softwareListDataGridView.DataSource = new BindingSource(bindingList, "");
        // Use DisplayedCells instead of AllCells: measuring every cell of a large list
        // blocks the UI thread; measuring only currently visible rows is virtually
        // instant and gives the same visual result for the initial viewport.
        softwareListDataGridView.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.DisplayedCells);

        // Set column width
        foreach (DataGridViewColumn column in softwareListDataGridView.Columns)
            if (column.Width > 400)
                column.Width = 400;
        softwareListDataGridView.Columns[0].Width = 3 * softwareListDataGridView.Columns[0].Width;
        softwareListDataGridView.Columns[1].Width = 5 * softwareListDataGridView.Columns[1].Width;

        RestoreGridViewState(viewState);
        if (IsHandleCreated && !IsDisposed)
        {
            try
            {
                BeginInvoke(() => RestoreGridViewState(viewState));
            }
            catch (ObjectDisposedException)
            {
                // The form went away while a reload was being applied.
            }
        }
    }

    private GridViewState CaptureGridViewState()
    {
        var selectedNames = softwareListDataGridView
            .SelectedRows
            .Cast<DataGridViewRow>()
            .Select(row => (row.DataBoundItem as SoftwareItem)?.Name)
            .Where(name => !string.IsNullOrEmpty(name))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currentRow = softwareListDataGridView.CurrentRow;
        var currentName = (currentRow?.DataBoundItem as SoftwareItem)?.Name;
        var currentColumnIndex = softwareListDataGridView.CurrentCell?.ColumnIndex ?? 0;

        var firstDisplayedRowIndex = -1;
        string? firstDisplayedName = null;
        try
        {
            firstDisplayedRowIndex = softwareListDataGridView.FirstDisplayedScrollingRowIndex;
            if (firstDisplayedRowIndex >= 0 && firstDisplayedRowIndex < softwareListDataGridView.Rows.Count)
                firstDisplayedName =
                    (softwareListDataGridView.Rows[firstDisplayedRowIndex].DataBoundItem as SoftwareItem)?.Name;
        }
        catch (InvalidOperationException)
        {
            // The grid has no displayed rows yet (for example during startup).
        }

        return new GridViewState(
            selectedNames,
            currentName,
            currentColumnIndex,
            firstDisplayedName,
            firstDisplayedRowIndex
        );
    }

    private void RestoreGridViewState(GridViewState state)
    {
        if (softwareListDataGridView.Rows.Count == 0)
            return;

        var rowsByName = softwareListDataGridView
            .Rows
            .Cast<DataGridViewRow>()
            .Where(row => row.DataBoundItem is SoftwareItem)
            .GroupBy(row => ((SoftwareItem)row.DataBoundItem!).Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        if (rowsByName.Count == 0)
            return;

        softwareListDataGridView.ClearSelection();
        foreach (var name in state.SelectedNames)
            if (rowsByName.TryGetValue(name, out var row))
                row.Selected = true;

        DataGridViewRow? currentRow = null;
        if (state.CurrentName is not null && state.SelectedNames.Contains(state.CurrentName))
            rowsByName.TryGetValue(state.CurrentName, out currentRow);
        currentRow ??= softwareListDataGridView.SelectedRows.Cast<DataGridViewRow>().FirstOrDefault();
        var fallbackRowIndex = Math.Clamp(
            state.FirstDisplayedRowIndex,
            0,
            softwareListDataGridView.Rows.Count - 1
        );
        currentRow ??= rowsByName.Values.MinBy(row => Math.Abs(row.Index - fallbackRowIndex));
        if (currentRow is null)
            return;

        var columnIndex = Math.Clamp(state.CurrentColumnIndex, 0, softwareListDataGridView.Columns.Count - 1);
        softwareListDataGridView.CurrentCell = softwareListDataGridView[columnIndex, currentRow.Index];
        currentRow.Selected = true;

        var firstDisplayedRow = -1;
        if (state.FirstDisplayedName is not null && rowsByName.TryGetValue(state.FirstDisplayedName, out var anchorRow))
            firstDisplayedRow = anchorRow.Index;
        if (firstDisplayedRow < 0)
            firstDisplayedRow = Math.Clamp(state.FirstDisplayedRowIndex, 0, softwareListDataGridView.Rows.Count - 1);

        try
        {
            softwareListDataGridView.FirstDisplayedScrollingRowIndex = firstDisplayedRow;
        }
        catch (InvalidOperationException)
        {
            // The grid may not have a scrollable row until its next layout pass.
        }
    }

    /// <summary>
    /// The batch the menu, the shortcut and the debug tools all drive; it holds the
    /// cancel flag, so cancelling reaches whichever of them started the run.
    /// </summary>
    public DownloadBatch DownloadBatch { get; } = new();

    public Task<bool> DownloadAll() =>
        RunBatchAsync(
            SoftwareManager.Items,
            retryCount: Settings.DownloadRetryCount,
            operation: "DownloadAll"
        );

    public Task<bool> DownloadSelected() =>
        RunBatchAsync(
            GetSelectedItems(),
            retryCount: Settings.DownloadRetryCount,
            operation: "DownloadSelected"
        );

    /// <summary>
    /// Wraps a batch in what only the window can supply: waiting for the browser
    /// that Load sets up, and locking the menu items for the duration.
    /// </summary>
    internal async Task<bool> RunBatchAsync(
        IEnumerable<SoftwareItem> items,
        bool testOnly = false,
        int retryCount = 0,
        string operation = "Download"
    )
    {
        await _onLoadTaskCompletionSource.Task;

        try
        {
            using (new DownloadUIDisabler(this))
                return await DownloadBatch.RunAsync(items, testOnly, retryCount, operation);
        }
        finally
        {
            await ReloadDeferredAfterBatchAsync();
        }
    }

    private async Task ReloadDeferredAfterBatchAsync()
    {
        if (!_reloadAfterBatchPending || DownloadBatch.IsRunning || IsDisposed)
            return;

        _reloadAfterBatchPending = false;
        await Reload();
        Log.ZLogInformation($"Reloaded the software list after the batch finished");
    }

    private List<SoftwareItem> GetSelectedItems()
    {
        var items = softwareListDataGridView
            .SelectedRows.OfType<DataGridViewRow>()
            .OrderBy(row => row.Index)
            .Select(row => row.DataBoundItem)
            .OfType<SoftwareItem>()
            .ToList();
        return items;
    }

    private async void downloadSelectedToolStripMenuItem_Click(object sender, EventArgs e)
    {
        await DownloadSelected();
    }

    private async void downloadAllToolStripMenuItem_Click(object sender, EventArgs e)
    {
        await DownloadAll();
    }

    private async void testSelectedToolStripMenuItem_Click(object sender, EventArgs e)
    {
        await RunBatchAsync(GetSelectedItems(), testOnly: true, operation: "TestSelected");
    }

    private async void testAllToolStripMenuItem_Click(object sender, EventArgs e)
    {
        await RunBatchAsync(SoftwareManager.Items, testOnly: true, operation: "TestAll");
    }

    private async void reloadToolStripMenuItem_Click(object sender, EventArgs e)
    {
        await Reload();
    }

    private async void editScriptToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (softwareListDataGridView.CurrentRow?.DataBoundItem is not SoftwareItem item)
            return;

        var session = new ScriptEditSession(item);

        // A leftover file means an earlier edit was never applied; only the user
        // knows whether it is worth more than what is in the list.
        if (
            session.HasUnappliedFile
            && MessageBox.Show(
                "The script file already exists. Press Yes to reload or No to override?",
                "",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning
            ) == DialogResult.Yes
        )
        {
            if (await session.ApplyAsync())
                await SoftwareManager.Save();
            return;
        }

        await session.WriteAsync();

        using var editor = session.StartEditor();
        if (editor == null)
            return;

        MessageBox.Show("Edit the script and save it. Then click OK to reload the script.");

        if (await session.ApplyAsync())
            await SoftwareManager.Save();
    }

    private async void softwareListDataGridView_CellEndEdit(
        object sender,
        DataGridViewCellEventArgs e
    )
    {
        await SoftwareManager.Save();
    }

    private void softwareListDataGridView_MouseDown(object sender, MouseEventArgs e)
    {
        // Right click selects the row
        if (e.Button != MouseButtons.Right)
            return;

        var hit = softwareListDataGridView.HitTest(e.X, e.Y);
        if (hit.RowIndex == -1)
            return;

        var rowUnderCursor = softwareListDataGridView.Rows[hit.RowIndex];
        if (!rowUnderCursor.Selected)
            softwareListDataGridView.CurrentCell = rowUnderCursor.Cells[hit.ColumnIndex];
    }

    private async void softwareListDataGridView_UserAddedRow(
        object sender,
        DataGridViewRowEventArgs e
    )
    {
        await SoftwareManager.Save();
    }

    private async void softwareListDataGridView_UserDeletedRow(
        object sender,
        DataGridViewRowEventArgs e
    )
    {
        await SoftwareManager.Save();
    }

    private void softwareListDataGridView_CurrentCellChanged(object sender, EventArgs e)
    {
        // errorMessageLabel bind to selected SoftwareItem
        errorMessageLabel.DataBindings.Clear();

        if (softwareListDataGridView.CurrentRow?.DataBoundItem == null)
            return;

        errorMessageLabel.DataBindings.Add(
            new Binding("Text", softwareListDataGridView.CurrentRow.DataBoundItem, "ErrorMessage")
        );
    }

    private void showDevToolsButton_Click(object sender, EventArgs e)
    {
        Browser.ShowDevTools();
    }

    private async void openWebPageToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (softwareListDataGridView.CurrentRow?.DataBoundItem == null)
            return;

        var item = softwareListDataGridView.CurrentRow?.DataBoundItem as SoftwareItem;
        await Browser.Load(item!.WebPage);
    }

    private void openWebPageInBrowserToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (softwareListDataGridView.CurrentRow?.DataBoundItem == null)
            return;

        Process.Start(
            new ProcessStartInfo
            {
                FileName = (
                    softwareListDataGridView.CurrentRow?.DataBoundItem as SoftwareItem
                )!.WebPage,
                UseShellExecute = true,
            }
        );
    }

    private void openDownloadDirectoryToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (softwareListDataGridView.CurrentRow?.DataBoundItem == null)
            return;

        var item = (softwareListDataGridView.CurrentRow?.DataBoundItem as SoftwareItem)!;

        foreach (var dir in new[] { item.FinalDownloadDirectory, item.DownloadDirectory2 })
        {
            if (string.IsNullOrEmpty(dir))
                continue;

            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
    }

    /// <summary>
    /// Discards the per-machine settings held for names the list no longer has.
    /// Those rows survive a save so a temporarily shorter list cannot destroy
    /// them; this is the way to get rid of the ones that really are obsolete.
    /// </summary>
    private async void cleanUpLocalSettingsToolStripMenuItem_Click(object sender, EventArgs e)
    {
        var unclaimed = SoftwareManager.UnclaimedLocalSettingNames;
        const string caption = "Clean up unused local settings";

        if (unclaimed.Count == 0)
        {
            MessageBox.Show(
                this,
                "Every saved setting belongs to an item in the list; there is nothing to clean up.",
                caption,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
            return;
        }

        var answer = MessageBox.Show(
            this,
            $"These {unclaimed.Count} name(s) are no longer in the software list, but their saved "
                + "settings are still on file:"
                + Environment.NewLine
                + Environment.NewLine
                + string.Join(Environment.NewLine, unclaimed)
                + Environment.NewLine
                + Environment.NewLine
                + "Delete them? This cannot be undone.",
            caption,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2
        );
        if (answer != DialogResult.Yes)
            return;

        if (await SoftwareManager.RemoveUnclaimedLocalSettings())
            MessageBox.Show(
                this,
                $"Removed the unused settings for {unclaimed.Count} name"
                    + (unclaimed.Count == 1 ? "." : "s."),
                caption,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        else
            MessageBox.Show(
                this,
                "Nothing was removed: the list could not be saved. Check the log, reload the list "
                    + "and try again.",
                caption,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
    }

    private void cancelToolStripMenuItem_Click(object sender, EventArgs e)
    {
        DownloadBatch.Cancel();
    }

    private void Restart()
    {
        Process.Start(Application.ExecutablePath);
        Application.Exit();
    }

    private void softwareListDataGridView_CurrentCellDirtyStateChanged(object sender, EventArgs e)
    {
        if (
            softwareListDataGridView.IsCurrentCellDirty
            && softwareListDataGridView.CurrentCell!.OwningColumn is DataGridViewCheckBoxColumn
        )
            softwareListDataGridView.EndEdit();
    }

    private async void clearCookieButton_Click(object sender, EventArgs e)
    {
        await Browser.ClearCookies();
    }

    private int dragRowIndex;

    private void softwareListDataGridView_MouseMove(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            var dragSize = SystemInformation.DragSize;
            var dragRect = new Rectangle(new Point(dragRowIndex, dragRowIndex), dragSize);

            if (!dragRect.Contains(e.X, e.Y))
            {
                var row = softwareListDataGridView.HitTest(e.X, e.Y).RowIndex;
                if (row >= 0)
                {
                    dragRowIndex = row;
                    var draggedItem = softwareListDataGridView.Rows[row];
                    softwareListDataGridView.DoDragDrop(draggedItem, DragDropEffects.Move);
                }
            }
        }
    }

    private void softwareListDataGridView_DragOver(object? sender, DragEventArgs e)
    {
        e.Effect = DragDropEffects.Move;
    }

    private async void softwareListDataGridView_DragDrop(object? sender, DragEventArgs e)
    {
        var clientPoint = softwareListDataGridView.PointToClient(new Point(e.X, e.Y));
        var targetRowIndex = softwareListDataGridView
            .HitTest(clientPoint.X, clientPoint.Y)
            .RowIndex;

        if (targetRowIndex < 0)
            return;
        if (targetRowIndex == dragRowIndex)
            return;

        if (
            MessageBox.Show(
                $"Move item from {dragRowIndex + 1} to {targetRowIndex + 1}?",
                "Confirm Move",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            ) != DialogResult.Yes
        )
            return;

        var bindingList =
            (BindingList<SoftwareItem>)((BindingSource)softwareListDataGridView.DataSource!).List;
        var item = bindingList[dragRowIndex];

        bindingList.RemoveAt(dragRowIndex);
        bindingList.Insert(targetRowIndex, item);

        await SoftwareManager.Save();

        // Select the dragged item at its new position and move cursor to the cell
        softwareListDataGridView.ClearSelection();
        softwareListDataGridView.Rows[targetRowIndex].Selected = true;
        softwareListDataGridView.CurrentCell = softwareListDataGridView[
            softwareListDataGridView.CurrentCell!.ColumnIndex,
            targetRowIndex
        ];
    }

    private async void insertNewToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (softwareListDataGridView.CurrentRow == null)
            return;

        var currentIndex = softwareListDataGridView.CurrentRow.Index;
        var bindingList =
            (BindingList<SoftwareItem>)((BindingSource)softwareListDataGridView.DataSource!).List;

        // Create a new SoftwareItem
        var newItem = new SoftwareItem { Name = "New Software", Enabled = true };

        // Insert new item at current position
        bindingList.Insert(currentIndex, newItem);

        // Select the newly inserted row
        softwareListDataGridView.ClearSelection();
        softwareListDataGridView.Rows[currentIndex].Selected = true;

        await SoftwareManager.Save();
    }

    private async void duplicateToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (softwareListDataGridView.CurrentRow?.DataBoundItem == null)
            return;

        var currentIndex = softwareListDataGridView.CurrentRow.Index;
        var bindingList =
            (BindingList<SoftwareItem>)((BindingSource)softwareListDataGridView.DataSource!).List;

        var currentItem = (SoftwareItem)softwareListDataGridView.CurrentRow.DataBoundItem;

        // Clone the current item
        var duplicatedItem = currentItem.Clone();

        // Insert duplicated item at the next position
        var insertIndex = currentIndex + 1;
        bindingList.Insert(insertIndex, duplicatedItem);

        // Select the newly duplicated row
        softwareListDataGridView.ClearSelection();
        softwareListDataGridView.Rows[insertIndex].Selected = true;
        softwareListDataGridView.CurrentCell = softwareListDataGridView[
            softwareListDataGridView.CurrentCell?.ColumnIndex ?? 0,
            insertIndex
        ];

        await SoftwareManager.Save();
    }

    private async void deleteToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (softwareListDataGridView.CurrentRow == null)
            return;

        // Confirm before deletion
        if (
            MessageBox.Show(
                "Are you sure you want to delete the selected items?",
                "Confirm Delete",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            ) != DialogResult.Yes
        )
            return;

        var bindingList =
            (BindingList<SoftwareItem>)((BindingSource)softwareListDataGridView.DataSource!).List;
        var selectedRows = softwareListDataGridView
            .SelectedRows.Cast<DataGridViewRow>()
            .OrderByDescending(r => r.Index) // Delete from bottom to top to maintain correct indices
            .ToList();

        foreach (var row in selectedRows)
        {
            bindingList.RemoveAt(row.Index);
        }

        await SoftwareManager.Save();
    }

    private async void settingsButton_Click(object sender, EventArgs e)
    {
        var configRootBefore = SettingsStore.ResolveConfigRoot();

        using var form = new SettingsForm();
        if (form.ShowDialog(this) != DialogResult.OK)
            return;

        // A new storage folder can hold a different software list, so rebind
        // instead of showing the old one until the next restart.
        if (
            !string.Equals(
                configRootBefore,
                SettingsStore.ResolveConfigRoot(),
                StringComparison.OrdinalIgnoreCase
            )
        )
            await Reload();
    }

    private void MainForm_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.F)
        {
            ShowSearchForm();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Escape && _searchForm != null)
        {
            CloseSearchForm();
            e.Handled = true;
        }
    }

    private void ShowSearchForm()
    {
        if (_searchForm == null || _searchForm.IsDisposed)
        {
            _searchForm = new SearchForm();
            _searchForm.SearchNext += SearchForm_SearchNext;
            _searchForm.SearchPrevious += SearchForm_SearchPrevious;
            _searchForm.SearchTextChanged += SearchForm_SearchTextChanged;
            _searchForm.FormClosed += SearchForm_FormClosed;

            // Position the search form at the top-right of the main form
            var location = new Point(Location.X + Width - _searchForm.Width - 20, Location.Y + 50);
            _searchForm.Location = location;
        }

        _searchForm.Show();
        _searchForm.BringToFront();
        _searchForm.FocusSearchBox();
    }

    private void CloseSearchForm()
    {
        if (_searchForm != null && !_searchForm.IsDisposed)
        {
            _searchForm.Close();
        }
    }

    private void SearchForm_FormClosed(object? sender, FormClosedEventArgs e)
    {
        ClearSearchHighlight();
        _searchResults.Clear();
        _currentSearchResultIndex = -1;
    }

    private async void SearchForm_SearchTextChanged(object? sender, EventArgs e)
    {
        if (_searchForm == null)
            return;

        var searchText = _searchForm.SearchText;
        if (string.IsNullOrWhiteSpace(searchText))
        {
            _searchCts?.Cancel();
            ClearSearchHighlight();
            _searchResults.Clear();
            _currentSearchResultIndex = -1;
            _searchForm.UpdateResults(0, 0);
            return;
        }

        await PerformSearchAsync(searchText, _searchForm.MatchCase, _searchForm.FirstMatchPerRow);
    }

    private void SearchForm_SearchNext(object? sender, EventArgs e)
    {
        if (_searchResults.Count == 0)
            return;

        _currentSearchResultIndex = (_currentSearchResultIndex + 1) % _searchResults.Count;
        NavigateToSearchResult();
    }

    private void SearchForm_SearchPrevious(object? sender, EventArgs e)
    {
        if (_searchResults.Count == 0)
            return;

        _currentSearchResultIndex =
            _currentSearchResultIndex <= 0
                ? _searchResults.Count - 1
                : _currentSearchResultIndex - 1;
        NavigateToSearchResult();
    }

    private CancellationTokenSource? _searchCts;

    private async Task PerformSearchAsync(string searchText, bool matchCase, bool firstMatchPerRow)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        try
        {
            // Debounce: avoid running search on every keystroke
            await Task.Delay(150, token);

            // Snapshot column->property mapping on UI thread (uses DataPropertyName from auto-generated columns)
            var columnProps = new List<(int columnIndex, System.Reflection.PropertyInfo prop)>();
            foreach (DataGridViewColumn column in softwareListDataGridView.Columns)
            {
                if (!column.Visible)
                    continue;
                var propName = column.DataPropertyName;
                if (string.IsNullOrEmpty(propName))
                    continue;
                var prop = typeof(SoftwareItem).GetProperty(propName);
                if (prop == null)
                    continue;
                columnProps.Add((column.Index, prop));
            }
            var items = SoftwareManager.Items.ToList();

            // Run the actual matching on a background thread
            var results = await Task.Run(
                () =>
                {
                    var cmp = matchCase
                        ? StringComparison.Ordinal
                        : StringComparison.OrdinalIgnoreCase;
                    var list = new List<(int RowIndex, int ColumnIndex)>();
                    for (int r = 0; r < items.Count; r++)
                    {
                        foreach (var (colIdx, prop) in columnProps)
                        {
                            var value = prop.GetValue(items[r])?.ToString() ?? "";
                            if (value.Contains(searchText, cmp))
                            {
                                list.Add((r, colIdx));
                                if (firstMatchPerRow)
                                    break;
                            }
                        }
                    }
                    return list;
                },
                token
            );

            if (token.IsCancellationRequested)
                return;

            _searchResults = results;

            if (_searchResults.Count > 0)
            {
                _currentSearchResultIndex = 0;
                NavigateToSearchResult();
            }
            else
            {
                _currentSearchResultIndex = -1;
                ClearSearchHighlight();
                _searchForm?.UpdateResults(0, 0);
            }
        }
        catch (OperationCanceledException)
        {
            // Search was superseded by a newer query; ignore.
        }
    }

    private void NavigateToSearchResult()
    {
        if (_currentSearchResultIndex < 0 || _currentSearchResultIndex >= _searchResults.Count)
            return;

        var (rowIndex, columnIndex) = _searchResults[_currentSearchResultIndex];

        // Clear previous selection
        softwareListDataGridView.ClearSelection();

        // Select and focus the found cell
        softwareListDataGridView.CurrentCell = softwareListDataGridView[columnIndex, rowIndex];
        softwareListDataGridView.Rows[rowIndex].Selected = true;

        // Ensure the cell is visible
        softwareListDataGridView.FirstDisplayedScrollingRowIndex = Math.Max(0, rowIndex - 5);

        // Highlight the row
        HighlightSearchResult(rowIndex);

        _searchForm?.UpdateResults(_currentSearchResultIndex + 1, _searchResults.Count);
    }

    private int _highlightedRow = -1;

    private void HighlightSearchResult(int rowIndex)
    {
        if (_highlightedRow == rowIndex)
            return;

        // Only repaint the row that actually changes, not the entire grid.
        if (_highlightedRow >= 0 && _highlightedRow < softwareListDataGridView.Rows.Count)
        {
            var prev = softwareListDataGridView.Rows[_highlightedRow];
            prev.DefaultCellStyle.BackColor = Color.Empty;
            prev.DefaultCellStyle.SelectionBackColor = Color.Empty;
        }

        if (rowIndex >= 0 && rowIndex < softwareListDataGridView.Rows.Count)
        {
            var row = softwareListDataGridView.Rows[rowIndex];
            row.DefaultCellStyle.BackColor = Color.Yellow;
            row.DefaultCellStyle.SelectionBackColor = Color.Orange;
            _highlightedRow = rowIndex;
        }
        else
        {
            _highlightedRow = -1;
        }
    }

    private void ClearSearchHighlight() => HighlightSearchResult(-1);
}
