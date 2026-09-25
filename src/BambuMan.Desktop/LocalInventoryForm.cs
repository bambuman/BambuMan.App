using BambuMan.Shared.Managers;
using BambuMan.Shared.Models;
using System.Globalization;

namespace BambuMan.Desktop;

/// <summary>The spools the no-backend mode keeps on this computer ("Store spools locally"), with delete.</summary>
public class LocalInventoryForm : Form
{
    private readonly NoBackendManager manager;
    private readonly DataGridView dgvSpools = new();
    private readonly Label lblCount = new();

    public LocalInventoryForm(NoBackendManager manager)
    {
        this.manager = manager;

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

        var btnDelete = new Button { Text = "Delete selected", AutoSize = true };
        btnDelete.Click += btnDelete_Click;

        var btnDeleteAll = new Button { Text = "Delete all", AutoSize = true };
        btnDeleteAll.Click += btnDeleteAll_Click;

        var btnClose = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
        CancelButton = btnClose;

        lblCount.AutoSize = true;
        lblCount.Anchor = AnchorStyles.Left;
        lblCount.Margin = new Padding(3, 8, 20, 3);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6) };
        buttons.Controls.AddRange([lblCount, btnDelete, btnDeleteAll, btnClose]);

        Controls.Add(dgvSpools);
        Controls.Add(buttons);

        LoadSpools();
    }

    private void LoadSpools()
    {
        var spools = manager.Spools.Where(x => x.Key != null).OrderByDescending(x => x.LastScanned).ToList();

        dgvSpools.Rows.Clear();

        foreach (var spool in spools)
        {
            var info = spool.Info;
            var index = dgvSpools.Rows.Add(
                "",
                info.DetailedFilamentType ?? info.FilamentType,
                spool.Name,
                spool.Location,
                spool.Price?.ToString("0.00", CultureInfo.CurrentCulture),
                info.TrayUid,
                info.SerialNumber,
                spool.LastScanned.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture));

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
