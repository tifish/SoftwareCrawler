using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using CefSharp;

namespace SoftwareCrawler;

public partial class MainForm : Form
{
    public MainForm()
    {
        InitializeComponent();

        // Add drag and drop support
        softwareListDataGridView.AllowDrop = true;
        softwareListDataGridView.DragDrop += softwareListDataGridView_DragDrop;
        softwareListDataGridView.DragOver += softwareListDataGridView_DragOver;
        softwareListDataGridView.MouseMove += softwareListDataGridView_MouseMove;
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

            _mainForm.cancelToolStripMenuItem.Enabled = true;
        }

        public void Dispose()
        {
            _mainForm.downloadSelectedToolStripMenuItem.Enabled = true;
            _mainForm.downloadAllToolStripMenuItem.Enabled = true;
            _mainForm.testSelectedToolStripMenuItem.Enabled = true;
            _mainForm.testAllToolStripMenuItem.Enabled = true;
            _mainForm.reloadToolStripMenuItem.Enabled = true;

            _mainForm.cancelToolStripMenuItem.Enabled = false;

            _mainForm._currentDownloadItem = null;
        }
    }

    private readonly TaskCompletionSource<bool> _onLoadTaskCompletionSource = new();

    protected override async void OnLoad(EventArgs args)
    {
        try
        {
            base.OnLoad(args);

            using (new DownloadUIDisabler(this))
            {
                await Settings.Load();

                var parentForm = new Form();
                await Browser.Init(parentForm);
                parentForm.Size = new Size(1280, 720);

                await Reload();
            }

            _onLoadTaskCompletionSource.TrySetResult(true);
        }
        catch (Exception e)
        {
            Logger.Error(e, "An error occurred in OnLoad");
            MessageBox.Show(e.Message, "", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _onLoadTaskCompletionSource.TrySetResult(false);
        }
    }

    private async Task Reload()
    {
        await SoftwareManager.Load();
        var bindingList = new BindingList<SoftwareItem>(SoftwareManager.Items);
        softwareListDataGridView.DataSource = new BindingSource(bindingList, null);
        softwareListDataGridView.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.AllCells);
        foreach (DataGridViewColumn column in softwareListDataGridView.Columns)
            if (column.Width > 400)
                column.Width = 400;
    }

    private SoftwareItem? _currentDownloadItem;

    public async Task<bool> DownloadAll()
    {
        Logger.Information("DownloadAll starts");

        await _onLoadTaskCompletionSource.Task;

        _hasCancelled = false;
        var success = true;

        using (new DownloadUIDisabler(this))
        {
            var items = SoftwareManager.Items.ToList();

            foreach (var item in items)
            {
                if (_hasCancelled)
                {
                    success = false;
                    break;
                }

                item.ResetStatus();
            }

            foreach (var item in items)
            {
                if (_hasCancelled)
                {
                    success = false;
                    break;
                }

                _currentDownloadItem = item;
                if (!await item.Download(retryCount: Settings.DownloadRetryCount))
                    success = false;
            }
        }

        Logger.Information("DownloadAll ends with success = {Success}", success);
        return success;
    }

    public async Task<bool> DownloadSelected()
    {
        Logger.Information("DownloadSelected starts");

        await _onLoadTaskCompletionSource.Task;

        _hasCancelled = false;
        var success = true;

        using (new DownloadUIDisabler(this))
        {
            var items = GetSelectedItems();

            foreach (var item in items)
            {
                if (_hasCancelled)
                {
                    success = false;
                    break;
                }

                item.ResetStatus();
            }

            foreach (var item in items)
            {
                if (_hasCancelled)
                {
                    success = false;
                    break;
                }

                _currentDownloadItem = item;
                if (!await item.Download(retryCount: Settings.DownloadRetryCount))
                    success = false;
            }
        }

        Logger.Information("DownloadSelected ends with success = {Success}", success);
        return success;
    }

    private List<SoftwareItem> GetSelectedItems()
    {
        var items = softwareListDataGridView.SelectedRows.OfType<DataGridViewRow>()
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
        _hasCancelled = false;

        using (new DownloadUIDisabler(this))
        {
            var items = GetSelectedItems();

            foreach (var item in items.TakeWhile(_ => !_hasCancelled))
                item.ResetStatus();

            foreach (var item in items.TakeWhile(_ => !_hasCancelled))
            {
                _currentDownloadItem = item;
                // ReSharper disable once RedundantSuppressNullableWarningExpression
                await item!.Download(true);
            }
        }
    }

    private async void testAllToolStripMenuItem_Click(object sender, EventArgs e)
    {
        _hasCancelled = false;

        using (new DownloadUIDisabler(this))
        {
            foreach (var item in SoftwareManager.Items.TakeWhile(_ => !_hasCancelled))
                item.ResetStatus();

            foreach (var item in SoftwareManager.Items.TakeWhile(_ => !_hasCancelled))
            {
                _currentDownloadItem = item;
                await item.Download(true);
            }
        }
    }

    private async void reloadToolStripMenuItem_Click(object sender, EventArgs e)
    {
        await Reload();
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var pattern = "[" + Regex.Escape(new string(invalidChars)) + "]";
        return Regex.Replace(fileName, pattern, "_");
    }

    private async void editScriptToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (softwareListDataGridView.CurrentRow?.DataBoundItem == null)
            return;

        var item = (SoftwareItem)softwareListDataGridView.CurrentRow.DataBoundItem;
        // Unescape the return character
        var script = item.XPathOrScripts.Replace("`n", "\r\n");

        // Save script to a temp file or reload from file
        var tempScriptDir = Path.Join(Path.GetTempPath(), "SoftwareCrawler");
        Directory.CreateDirectory(tempScriptDir);
        var tempScriptFilePath = Path.Join(tempScriptDir, SanitizeFileName(item.Name) + ".js");

        if (File.Exists(tempScriptFilePath))
            if (MessageBox.Show("The script file already exists. Press Yes to reload or No to override?", "",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                await GetScriptFromFile(tempScriptFilePath, item);
                return;
            }

        await File.WriteAllTextAsync(tempScriptFilePath, script, new UTF8Encoding(true));

        // Edit the script with an external editor
        var editor = Settings.ExternalJavascriptEditor;
        if (editor == "")
            editor = "notepad.exe";

        var proc = Process.Start(new ProcessStartInfo
        {
            FileName = editor,
            Arguments = $"\"{tempScriptFilePath}\"",
            UseShellExecute = true,
        });
        if (proc == null)
            return;

        MessageBox.Show("Edit the script and save it. Then click OK to reload the script.");

        // Read script from the temp file
        await GetScriptFromFile(tempScriptFilePath, item);
        return;

        async Task GetScriptFromFile(string scriptFile, SoftwareItem softwareItem)
        {
            script = await File.ReadAllTextAsync(scriptFile);
            File.Delete(scriptFile);

            script = script.Trim(); // Trim end of file
            script = script.Replace("\r\n", "`n").Replace("\n", "`n");

            softwareItem.XPathOrScripts = script;
            await SoftwareManager.Save();
        }
    }

    private async void softwareListDataGridView_CellEndEdit(object sender, DataGridViewCellEventArgs e)
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

    private async void softwareListDataGridView_UserAddedRow(object sender, DataGridViewRowEventArgs e)
    {
        await SoftwareManager.Save();
    }

    private async void softwareListDataGridView_UserDeletedRow(object sender, DataGridViewRowEventArgs e)
    {
        await SoftwareManager.Save();
    }

    private void softwareListDataGridView_CurrentCellChanged(object sender, EventArgs e)
    {
        // errorMessageLabel bind to selected SoftwareItem
        errorMessageLabel.DataBindings.Clear();

        if (softwareListDataGridView.CurrentRow?.DataBoundItem == null)
            return;

        errorMessageLabel.DataBindings.Add(new Binding("Text", softwareListDataGridView.CurrentRow.DataBoundItem, "ErrorMessage"));
    }

    private void showDevToolsButton_Click(object sender, EventArgs e)
    {
        Browser.WebBrowser.ShowDevTools();
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

        Process.Start(new ProcessStartInfo
        {
            FileName = (softwareListDataGridView.CurrentRow?.DataBoundItem as SoftwareItem)!.WebPage,
            UseShellExecute = true,
        });
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

            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true,
            });
        }
    }

    private bool _hasCancelled;

    private void cancelToolStripMenuItem_Click(object sender, EventArgs e)
    {
        _hasCancelled = true;
        _currentDownloadItem?.CancelDownload();
    }

    private void Restart()
    {
        Process.Start(Application.ExecutablePath);
        Application.Exit();
    }

    private void softwareListDataGridView_CurrentCellDirtyStateChanged(object sender, EventArgs e)
    {
        if (softwareListDataGridView.IsCurrentCellDirty
            && softwareListDataGridView.CurrentCell.OwningColumn is DataGridViewCheckBoxColumn)
            softwareListDataGridView.EndEdit();
    }

    private void clearCookieButton_Click(object sender, EventArgs e)
    {
        Cef.GetGlobalCookieManager().DeleteCookies("", "");
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
        var targetRowIndex = softwareListDataGridView.HitTest(clientPoint.X, clientPoint.Y).RowIndex;

        if (targetRowIndex < 0) return;
        if (targetRowIndex == dragRowIndex) return;

        var bindingList = (BindingList<SoftwareItem>)((BindingSource)softwareListDataGridView.DataSource).List;
        var item = bindingList[dragRowIndex];

        bindingList.RemoveAt(dragRowIndex);
        bindingList.Insert(targetRowIndex, item);

        await SoftwareManager.Save();

        // Select the dragged item at its new position and move cursor to the cell
        softwareListDataGridView.ClearSelection();
        softwareListDataGridView.Rows[targetRowIndex].Selected = true;
        softwareListDataGridView.CurrentCell = softwareListDataGridView[softwareListDataGridView.CurrentCell.ColumnIndex, targetRowIndex];
    }

    private async void insertNewToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (softwareListDataGridView.CurrentRow == null) return;

        var currentIndex = softwareListDataGridView.CurrentRow.Index;
        var bindingList = (BindingList<SoftwareItem>)((BindingSource)softwareListDataGridView.DataSource).List;

        // Create a new SoftwareItem
        var newItem = new SoftwareItem
        {
            Name = "New Software",
            Enabled = true
        };

        // Insert new item at current position
        bindingList.Insert(currentIndex, newItem);

        // Select the newly inserted row
        softwareListDataGridView.ClearSelection();
        softwareListDataGridView.Rows[currentIndex].Selected = true;

        await SoftwareManager.Save();
    }

    private async void deleteToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (softwareListDataGridView.CurrentRow == null)
            return;

        // Confirm before deletion
        if (MessageBox.Show("Are you sure you want to delete the selected items?", "Confirm Delete",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        var bindingList = (BindingList<SoftwareItem>)((BindingSource)softwareListDataGridView.DataSource).List;
        var selectedRows = softwareListDataGridView.SelectedRows.Cast<DataGridViewRow>()
            .OrderByDescending(r => r.Index)  // Delete from bottom to top to maintain correct indices
            .ToList();

        foreach (var row in selectedRows)
        {
            bindingList.RemoveAt(row.Index);
        }

        await SoftwareManager.Save();
    }

    private void settingsButton_Click(object sender, EventArgs e)
    {
        using var form = new SettingsForm();
        form.ShowDialog(this);
    }
}
