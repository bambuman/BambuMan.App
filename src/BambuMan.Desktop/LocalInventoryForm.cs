using BambuMan.Shared.Enums;
using BambuMan.Shared.Interfaces;
using BambuMan.Shared.Managers;
using BambuMan.Shared.Models;
using System.Globalization;

namespace BambuMan.Desktop;

/// <summary>
/// The spools the no-backend mode keeps on this computer ("Store spools locally"), with delete — and, with Spoolman or
/// Bambuddy selected, sending them there.
/// </summary>
public class LocalInventoryForm : Form
{
    private readonly NoBackendManager manager;
    private readonly BaseManager? server;
    private readonly DataGridView dgvSpools = new();
    private readonly Label lblCount = new();
    private readonly Button btnSend = new() { AutoSize = true };
    private readonly Button btnDelete = new() { Text = "Delete selected", AutoSize = true };
    private readonly Button btnDeleteAll = new() { Text = "Delete all", AutoSize = true };
    private CancellationTokenSource? sendCancellation;

    /// <summary>True while stored spools are being sent; the main form holds back scans meanwhile.</summary>
    public bool IsSending => sendCancellation != null;

    /// <summary>
    /// Whether spools were sent from this window. The server's "current spool" is then the last one sent, so a spool still shown
    /// in the main form's edit panel must not be saved.
    /// </summary>
    public bool HasSent { get; private set; }

    /// <param name="server">The selected backend, when it is a server the spools can be sent to; null with no backend.</param>
    public LocalInventoryForm(NoBackendManager manager, BaseManager? server)
    {
        this.manager = manager;
        this.server = server is ISpoolUploadTarget ? server : null;

        Text = "Stored spools";
        Size = new Size(1000, 500);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;

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
        dgvSpools.Columns.Add("Price", "Price");
        dgvSpools.Columns.Add("TrayUid", "Tray uid");
        dgvSpools.Columns.Add("TagUid", "Tag uid");
        dgvSpools.Columns.Add("LastScanned", "Last scanned");
        dgvSpools.Columns.Add("Sent", "Sent");

        btnSend.Click += btnSend_Click;
        btnSend.Visible = this.server != null;

        btnDelete.Click += btnDelete_Click;
        btnDeleteAll.Click += btnDeleteAll_Click;

        var btnClose = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
        CancelButton = btnClose;

        lblCount.AutoSize = true;
        lblCount.Anchor = AnchorStyles.Left;
        lblCount.Margin = new Padding(3, 8, 20, 3);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6) };
        buttons.Controls.AddRange([lblCount, btnSend, btnDelete, btnDeleteAll, btnClose]);

        Controls.Add(dgvSpools);
        Controls.Add(buttons);

        LoadSpools();
    }

    private string ServerName => server?.Backend.DisplayName() ?? string.Empty;

    private void LoadSpools()
    {
        var spools = manager.Spools.Where(x => x.Key != null).OrderByDescending(x => x.LastScanned).ToList();

        dgvSpools.Rows.Clear();

        foreach (var spool in spools)
        {
            var info = spool.Info;
            var index = dgvSpools.Rows.Add(
                "",
                info.DetailedFilamentType ?? info.FilamentType ?? "",
                spool.Name ?? "",
                spool.Location ?? "",
                spool.Price?.ToString("0.00", CultureInfo.CurrentCulture) ?? "",
                info.TrayUid ?? "",
                info.SerialNumber,
                spool.LastScanned.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture),
                spool.SentTo == null ? "" : $"{spool.SentTo.Value.DisplayName()} {spool.SentAt?.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture)}");

            var row = dgvSpools.Rows[index];
            row.Tag = spool.Key;

            // Same colour the read-only card shows for this tag, as #AARRGGBB.
            if (SpoolDisplayInfo.From(info, null).ColorHexes.FirstOrDefault() is { } argb)
            {
                var color = Color.FromArgb(int.Parse(argb[1..], NumberStyles.HexNumber));
                row.Cells[0].Style.BackColor = color;
                row.Cells[0].Style.SelectionBackColor = color;
            }
        }

        lblCount.Text = $"Stored spools: {spools.Count}";

        // Nothing selected sends them all; the grid would otherwise select the first row by itself.
        dgvSpools.ClearSelection();
        UpdateSendButton();
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
        btnSend.Text = selected > 0 ? $"Send {selected} selected to {ServerName}" : $"Send all to {ServerName}";
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        dgvSpools.SelectionChanged += (_, _) => UpdateSendButton();
        dgvSpools.ClearSelection();
        UpdateSendButton();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Closing mid-send would leave it running unseen: stop it after the spool being sent now, and close once it has.
        if (sendCancellation != null)
        {
            sendCancellation.Cancel();
            btnSend.Enabled = false;
            e.Cancel = true;
        }

        base.OnFormClosing(e);
    }

    /// <summary>Sends the selected spools (all of them when none is selected) to the selected server; a second click stops it.</summary>
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

            var selected = dgvSpools.SelectedRows.Cast<DataGridViewRow>().Select(x => x.Tag).OfType<string>().ToList();
            var keys = selected.Count > 0 ? selected : manager.Spools.Select(x => x.Key).OfType<string>().ToList();

            if (keys.Count == 0) return;

            if (MessageBox.Show(this, $"Send {keys.Count} stored spools to {ServerName}? A spool {ServerName} already has (same tag) is updated with the values stored here, otherwise it is added. The spools stay on this computer, marked as sent.",
                    $"Send to {ServerName}", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            sendCancellation = new CancellationTokenSource();
            HasSent = true;
            btnDelete.Enabled = false;
            btnDeleteAll.Enabled = false;
            UpdateSendButton();

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
            var text = failures.Count == 0 ? summary.Text : $"{summary.Text}\r\n\r\nNot sent:\r\n{string.Join("\r\n", failures.Take(20))}{(failures.Count > 20 ? $"\r\n… and {failures.Count - 20} more, see the log." : "")}";

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
            btnDelete.Enabled = true;
            btnDeleteAll.Enabled = true;
            LoadSpools();
        }
    }

    private async void btnDelete_Click(object? sender, EventArgs e)
    {
        try
        {
            var keys = dgvSpools.SelectedRows.Cast<DataGridViewRow>().Select(x => x.Tag).OfType<string>().ToList();

            foreach (var key in keys) await manager.RemoveSpoolAsync(key);

            LoadSpools();
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

            LoadSpools();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Delete failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
