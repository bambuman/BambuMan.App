using BambuMan.Shared.Enums;
using BambuMan.Shared.Interfaces;
using BambuMan.Shared.Managers;
using BambuMan.Shared.Models;
using BambuMan.Shared.Services;
using System.Globalization;
using LogLevel = BambuMan.Shared.Enums.LogLevel;

namespace BambuMan.Desktop;

/// <summary>
/// The spools the no-backend mode keeps on this computer ("Store spools locally"): search by name or tag, filter by
/// location, edit, delete, and csv import / export — the desktop counterpart of the Android Stored spools page — and,
/// with Spoolman or Bambuddy selected, sending them there.
/// </summary>
public class LocalInventoryForm : Form
{
    private const string AllLocations = "All locations";

    private readonly NoBackendManager manager;
    private readonly Action<LogLevel, string> log;

    private readonly TextBox txtSearch = new();
    private readonly ComboBox cmbLocation = new();
    private readonly DataGridView dgvSpools = new();
    private readonly Label lblCount = new();
    private readonly Button btnEdit = new();
    private readonly Button btnDelete = new();
    private readonly Button btnDeleteAll = new();
    private readonly Button btnImport = new();
    private readonly Button btnExport = new();
    private readonly Button btnSend = new() { AutoSize = true };

    private List<LocalSpoolItem> allSpools = [];

    // Set while the location list is rebuilt, so the selection it restores doesn't filter halfway.
    private bool updatingLocations;

    private readonly BaseManager? server;
    private CancellationTokenSource? sendCancellation;
    private bool closeAfterSend;

    /// <summary>True while stored spools are being sent; the main form holds back scans meanwhile.</summary>
    public bool IsSending => sendCancellation != null;

    /// <summary>
    /// Whether spools were sent from this window. The server's "current spool" is then the last one sent, so a spool still shown
    /// in the main form's edit panel must not be saved.
    /// </summary>
    public bool HasSent { get; private set; }

    /// <param name="server">The selected backend, when it is a server the spools can be sent to; null with no backend.</param>
    public LocalInventoryForm(NoBackendManager manager, Action<LogLevel, string> log, BaseManager? server)
    {
        this.manager = manager;
        this.log = log;
        this.server = server is ISpoolUploadTarget ? server : null;

        Text = "Stored spools";
        Size = new Size(1100, 550);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;

        #region Filters

        txtSearch.Width = 260;
        txtSearch.PlaceholderText = "Search name or tag";
        txtSearch.Margin = new Padding(3, 4, 12, 3);
        txtSearch.TextChanged += txtSearch_TextChanged;

        cmbLocation.Width = 200;
        cmbLocation.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbLocation.Margin = new Padding(3, 4, 12, 3);
        cmbLocation.SelectedIndexChanged += cmbLocation_SelectedIndexChanged;

        var btnClearFilter = new Button { Text = "Clear filter", AutoSize = true };
        btnClearFilter.Click += btnClearFilter_Click;

        var filters = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6), WrapContents = false };
        filters.Controls.AddRange([
            new Label { Text = "Search:", AutoSize = true, Margin = new Padding(3, 8, 3, 3) }, txtSearch,
            new Label { Text = "Location:", AutoSize = true, Margin = new Padding(3, 8, 3, 3) }, cmbLocation,
            btnClearFilter]);

        #endregion

        #region List

        dgvSpools.Dock = DockStyle.Fill;
        dgvSpools.ReadOnly = true;
        dgvSpools.AllowUserToAddRows = false;
        dgvSpools.AllowUserToDeleteRows = false;
        dgvSpools.AllowUserToResizeRows = false;
        dgvSpools.RowHeadersVisible = false;
        dgvSpools.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        dgvSpools.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
        dgvSpools.Columns.Add("Colour", "Colour");
        dgvSpools.Columns.Add("Material", "Material");
        dgvSpools.Columns.Add("Name", "Name");
        dgvSpools.Columns.Add("Location", "Location");
        dgvSpools.Columns.Add("Weight", "Weight (g)");
        dgvSpools.Columns.Add("Price", "Price");
        dgvSpools.Columns.Add("BuyDate", "Buy date");
        dgvSpools.Columns.Add("LotNr", "Lot nr");
        dgvSpools.Columns.Add("TrayUid", "Tray uid");
        dgvSpools.Columns.Add("TagUid", "Tag uid");
        dgvSpools.Columns.Add("LastScanned", "Last scanned");
        dgvSpools.Columns.Add("Sent", "Sent");
        dgvSpools.CellDoubleClick += dgvSpools_CellDoubleClick;
        dgvSpools.KeyDown += dgvSpools_KeyDown;
        dgvSpools.SelectionChanged += dgvSpools_SelectionChanged;

        #endregion

        #region Buttons

        btnSend.Click += btnSend_Click;
        btnSend.Visible = this.server != null;

        btnEdit.Text = "Edit...";
        btnEdit.AutoSize = true;
        btnEdit.Click += btnEdit_Click;

        btnDelete.Text = "Delete selected";
        btnDelete.AutoSize = true;
        btnDelete.Click += btnDelete_Click;

        btnDeleteAll.Text = "Delete all";
        btnDeleteAll.AutoSize = true;
        btnDeleteAll.Click += btnDeleteAll_Click;

        btnImport.Text = "Import CSV...";
        btnImport.AutoSize = true;
        btnImport.Click += btnImport_Click;

        btnExport.Text = "Export CSV...";
        btnExport.AutoSize = true;
        btnExport.Click += btnExport_Click;

        var btnClose = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
        CancelButton = btnClose;

        lblCount.AutoSize = true;
        lblCount.Anchor = AnchorStyles.Left;
        lblCount.Margin = new Padding(3, 8, 20, 3);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6) };
        buttons.Controls.AddRange([lblCount, btnSend, btnEdit, btnDelete, btnDeleteAll, btnImport, btnExport, btnClose]);

        #endregion

        // Docked from the last added inwards: filters on top, buttons at the bottom, the list fills the rest.
        Controls.Add(dgvSpools);
        Controls.Add(buttons);
        Controls.Add(filters);

        // A scan or an import while the window is open changes the store.
        manager.OnSpoolsChanged += ManagerOnSpoolsChanged;

        LoadSpools();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        manager.OnSpoolsChanged -= ManagerOnSpoolsChanged;
        base.OnFormClosed(e);
    }

    #region Methods

    private string ServerName => server?.Backend.DisplayName() ?? string.Empty;

    private string? SelectedLocation => cmbLocation.SelectedIndex > 0 ? cmbLocation.SelectedItem as string : null;

    private List<string> SelectedKeys => dgvSpools.SelectedRows.Cast<DataGridViewRow>().Select(x => x.Tag).OfType<string>().ToList();

    /// <summary>The spools the search and location filter show, in list order.</summary>
    private List<string> ShownKeys => dgvSpools.Rows.Cast<DataGridViewRow>().Select(x => x.Tag).OfType<string>().ToList();

    /// <summary>Reads the store again and rebuilds the location filter, keeping the chosen location while a spool still has it.</summary>
    private void LoadSpools()
    {
        allSpools = LocalSpoolFilter.ToItems(manager.Spools);

        var selected = SelectedLocation;
        var locations = LocalSpoolFilter.Locations(allSpools);

        updatingLocations = true;
        cmbLocation.BeginUpdate();
        cmbLocation.Items.Clear();
        cmbLocation.Items.Add(AllLocations);
        foreach (var location in locations) cmbLocation.Items.Add(location);

        // A location whose last spool was deleted would otherwise leave an empty list with no way to see why.
        cmbLocation.SelectedIndex = (selected == null ? -1 : locations.FindIndex(x => string.Equals(x, selected, StringComparison.OrdinalIgnoreCase))) + 1;
        cmbLocation.EndUpdate();
        updatingLocations = false;

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var selectedKeys = SelectedKeys;
        var filtered = LocalSpoolFilter.Filter(allSpools, txtSearch.Text, SelectedLocation);
        var groups = LocalSpoolFilter.Group(filtered);

        dgvSpools.Rows.Clear();

        // Spools of the same filament next to each other, as the Android page groups them; newest group first.
        foreach (var item in groups.SelectMany(x => x.Spools))
        {
            var spool = item.Spool;
            var info = spool.Info;
            var index = dgvSpools.Rows.Add(
                "",
                info.DetailedFilamentType ?? info.FilamentType ?? "",
                spool.Name ?? "",
                spool.Location ?? "",
                spool.Weight?.ToString("0.##", CultureInfo.CurrentCulture) ?? "",
                spool.Price?.ToString("0.00", CultureInfo.CurrentCulture) ?? "",
                spool.BuyDate?.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture) ?? "",
                spool.LotNr ?? "",
                info.TrayUid ?? "",
                info.SerialNumber,
                spool.LastScanned.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture),
                spool.SentTo == null ? "" : $"{spool.SentTo.Value.DisplayName()} {spool.SentAt?.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture)}");

            var row = dgvSpools.Rows[index];
            row.Tag = item.Key;
            row.Selected = selectedKeys.Contains(item.Key);

            // Same colour the read-only card shows for this tag, as #AARRGGBB; none for a tag without one.
            var color = Color.FromArgb(int.Parse(item.Color[1..], NumberStyles.HexNumber));

            if (color.A > 0)
            {
                row.Cells[0].Style.BackColor = color;
                row.Cells[0].Style.SelectionBackColor = color;
            }
        }

        if (allSpools.Count == 0) lblCount.Text = "No spools stored yet. Scan a spool to add it.";
        else if (filtered.Count == 0) lblCount.Text = $"No spools match the filter ({allSpools.Count} stored).";
        else lblCount.Text = LocalSpoolFilter.CountText(filtered.Count, allSpools.Count, groups.Count, LocalSpoolFilter.IsFiltered(txtSearch.Text, SelectedLocation));

        UpdateButtons();
    }

    private void UpdateSendButton()
    {
        if (server == null) return;

        if (IsSending)
        {
            btnSend.Text = "Stop sending";
            return;
        }

        var selected = dgvSpools.SelectedRows.Count;
        var shown = dgvSpools.Rows.Count;

        btnSend.Text = selected > 0 ? $"Send {selected} selected to {ServerName}"
            : shown < allSpools.Count ? $"Send {shown} shown to {ServerName}"
            : $"Send all to {ServerName}";
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // Nothing selected sends them all; the grid would otherwise select the first row by itself.
        dgvSpools.ClearSelection();
        UpdateButtons();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Closing mid-send would leave it running unseen: stop it after the spool being sent now, and close once it has.
        if (sendCancellation != null)
        {
            sendCancellation.Cancel();
            btnSend.Enabled = false;
            closeAfterSend = true;
            e.Cancel = true;
        }

        base.OnFormClosing(e);
    }

    /// <summary>Sends the selected spools (the ones the filters show when none is selected) to the selected server; a second click stops it.</summary>
    private async void btnSend_Click(object? sender, EventArgs e)
    {
        try
        {
            if (sendCancellation != null)
            {
                sendCancellation.Cancel();
                btnSend.Enabled = false;
                return;
            }

            if (server is not ISpoolUploadTarget target) return;

            var selected = SelectedKeys;
            // A search or location filter narrows what is sent, as on the Android page.
            var keys = selected.Count > 0 ? selected : ShownKeys;

            if (keys.Count == 0) return;

            if (MessageBox.Show(this, $"Send {keys.Count} stored spools to {ServerName}? A spool {ServerName} already has (same tag) is updated with the values stored here, otherwise it is added. The spools stay on this computer, marked as sent.",
                    $"Send to {ServerName}", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            sendCancellation = new CancellationTokenSource();
            HasSent = true;
            UpdateButtons();

            // A health check may have run into an error since the last scan; Init reconnects (or reports why it can't).
            lblCount.Text = $"Connecting to {ServerName} …";
            await server.Init();

            if (server.Status != ManagerStatusType.Ready)
            {
                MessageBox.Show(this, $"Can't reach {ServerName}. Check its url and try again.", $"Send to {ServerName}", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Progress<T> reports on this (the ui) thread.
            var progress = new Progress<SpoolUploadProgress>(p => lblCount.Text = p.Current == null ? $"Sent {p.Done} of {p.Total}" : $"Sending {p.Done + 1} of {p.Total}: {p.Current.Title}");

            var summary = await manager.SendSpoolsAsync(target, keys, progress, sendCancellation.Token);

            var failures = summary.Failures.Select(x => $"{x.Spool.Title} ({x.Spool.Key}): {x.Message}").ToList();
            var weightsKept = summary.WeightsKept.Select(x => $"{x.Spool.Title} ({x.Spool.Key}): {x.StatusText}").ToList();
            var text = summary.Text;

            if (failures.Count > 0) text += $"\r\n\r\nNot sent:\r\n{string.Join("\r\n", failures.Take(20))}{(failures.Count > 20 ? $"\r\n… and {failures.Count - 20} more, see the log." : "")}";
            if (weightsKept.Count > 0) text += $"\r\n\r\n{ServerName} had less filament left than stored here, so it kept its weight:\r\n{string.Join("\r\n", weightsKept.Take(20))}{(weightsKept.Count > 20 ? $"\r\n… and {weightsKept.Count - 20} more, see the log." : "")}";

            MessageBox.Show(this, text, $"Send to {ServerName}", MessageBoxButtons.OK, failures.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Send failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (sendCancellation != null)
            {
                sendCancellation.Dispose();
                sendCancellation = null;
            }

            btnSend.Enabled = true;
            LoadSpools();

            if (closeAfterSend) Close();
        }
    }

    /// <summary>Enables the buttons for the current list and selection; while spools are being sent only stopping it is offered.</summary>
    private void UpdateButtons()
    {
        btnEdit.Enabled = !IsSending && dgvSpools.SelectedRows.Count == 1;
        btnDelete.Enabled = !IsSending && dgvSpools.SelectedRows.Count > 0;
        btnDeleteAll.Enabled = !IsSending && allSpools.Count > 0;
        btnImport.Enabled = !IsSending;
        btnExport.Enabled = allSpools.Count > 0;
        UpdateSendButton();
    }

    private void EditSelected()
    {
        if (IsSending) return;

        if (SelectedKeys is not [var key]) return;

        // Deleted in the meantime (the list reloads on every change, so only a race gets here).
        if (manager.FindSpool(key) is not { } spool) return;

        using var form = new LocalSpoolEditForm(manager, spool);
        form.ShowDialog(this);
    }

    private async Task DeleteSelectedAsync()
    {
        var keys = SelectedKeys;

        if (keys.Count == 0 || IsSending) return;

        var question = keys.Count == 1
            ? $"Delete {allSpools.FirstOrDefault(x => x.Key == keys[0])?.Title ?? keys[0]} from this computer?"
            : $"Delete the {keys.Count} selected spools from this computer?";

        if (MessageBox.Show(this, question, "Delete spool", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;

        foreach (var key in keys) await manager.RemoveSpoolAsync(key);
    }

    #endregion

    #region CSV

    /// <summary>Writes every kept spool to a csv file the user picks. Returns what was done, or null when there was nothing to do.</summary>
    internal static string? ExportCsv(IWin32Window owner, NoBackendManager manager, Action<LogLevel, string> log)
    {
        var spools = manager.Spools;

        if (spools.Count == 0)
        {
            log(LogLevel.Warning, "There are no spools to export yet.");
            return null;
        }

        using var dialog = new SaveFileDialog();
        dialog.Filter = "CSV files (*.csv)|*.csv";
        dialog.FileName = LocalSpoolCsv.FileName(DateTime.Now);

        if (dialog.ShowDialog(owner) != DialogResult.OK) return null;

        using (var stream = File.Create(dialog.FileName)) LocalSpoolCsv.Write(stream, spools);

        var message = $"Exported {spools.Count} spools to {dialog.FileName}";
        log(LogLevel.Success, message);

        return message;
    }

    /// <summary>
    /// Adds the spools of a csv file the user picks to the stored inventory (one already stored is replaced). Rows that
    /// can't be read are logged and skipped. Returns the outcome, or null when cancelled or not storing locally.
    /// </summary>
    internal static async Task<string?> ImportCsvAsync(IWin32Window owner, NoBackendManager manager, Action<LogLevel, string> log)
    {
        if (!manager.IsStoreLoaded) return null;

        using var dialog = new OpenFileDialog();
        dialog.Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*";

        if (dialog.ShowDialog(owner) != DialogResult.OK) return null;

        LocalSpoolCsvReadResult result;

        using (var stream = File.OpenRead(dialog.FileName)) result = LocalSpoolCsv.Read(stream);

        var fileName = Path.GetFileName(dialog.FileName);

        foreach (var error in result.Errors) log(LogLevel.Warning, $"{fileName}: {error}");

        if (result.Spools.Count == 0)
        {
            var failed = $"No spools imported from {fileName}. {result.Errors.FirstOrDefault()}".TrimEnd();
            log(LogLevel.Error, failed);
            return failed;
        }

        var (added, updated) = await manager.ImportSpoolsAsync(result.Spools);

        var message = $"Imported {added} new and {updated} existing spools from {fileName}.";
        if (result.Errors.Count > 0) message += $" {result.Errors.Count} rows skipped, see the log.";

        log(LogLevel.Success, message);

        return message;
    }

    #endregion

    #region UI Events

    // Raised on the thread that changed the store — a scan comes from the NFC reader's.
    private void ManagerOnSpoolsChanged()
    {
        if (!IsHandleCreated || IsDisposed) return;

        BeginInvoke(new MethodInvoker(LoadSpools));
    }

    private void txtSearch_TextChanged(object? sender, EventArgs e) => ApplyFilter();

    private void cmbLocation_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (!updatingLocations) ApplyFilter();
    }

    private void btnClearFilter_Click(object? sender, EventArgs e)
    {
        // Each raises its changed event, which filters again.
        txtSearch.Clear();
        cmbLocation.SelectedIndex = 0;
    }

    private void dgvSpools_SelectionChanged(object? sender, EventArgs e) => UpdateButtons();

    private void dgvSpools_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;

        EditSelected();
    }

    private async void dgvSpools_KeyDown(object? sender, KeyEventArgs e)
    {
        try
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                EditSelected();
            }
            else if (e.KeyCode == Keys.Delete)
            {
                e.SuppressKeyPress = true;
                await DeleteSelectedAsync();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Delete failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void btnEdit_Click(object? sender, EventArgs e) => EditSelected();

    private async void btnDelete_Click(object? sender, EventArgs e)
    {
        try
        {
            await DeleteSelectedAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Delete failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void btnDeleteAll_Click(object? sender, EventArgs e)
    {
        try
        {
            var count = manager.Spools.Count;

            if (count == 0) return;

            if (MessageBox.Show(this, $"Delete all {count} stored spools from this computer? Export them to CSV first if you want to keep them.", "Delete all spools", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;

            await manager.ClearSpoolsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Delete failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void btnImport_Click(object? sender, EventArgs e)
    {
        try
        {
            var message = await ImportCsvAsync(this, manager, log);

            if (message != null) MessageBox.Show(this, message, "Import CSV", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            log(LogLevel.Error, ex.ToString());
            MessageBox.Show(this, ex.Message, "Import failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void btnExport_Click(object? sender, EventArgs e)
    {
        try
        {
            var message = ExportCsv(this, manager, log);

            if (message != null) MessageBox.Show(this, message, "Export CSV", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            log(LogLevel.Error, ex.ToString());
            MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    #endregion
}
