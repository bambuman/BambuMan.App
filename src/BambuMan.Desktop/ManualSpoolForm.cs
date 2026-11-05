using BambuMan.Shared.Matcher;
using BambuMan.Shared.Models;
using System.Globalization;
using ExternalFilament = SpoolMan.Api.Model.ExternalFilament;

namespace BambuMan.Desktop;

/// <summary>
/// Picks the filament of a spool whose tag can't be read (foil bag, damaged tag): a catalog filament found by search, or
/// an unknown one described by material, colour and label weight. The spool itself is then saved by the main form.
/// </summary>
public class ManualSpoolForm : Form
{
    private const int MaxResults = 200;

    private readonly IReadOnlyList<ExternalFilament> filaments;

    private readonly TextBox txtSearch = new() { Dock = DockStyle.Top, PlaceholderText = "Search material, name or colour" };
    private readonly ListBox lbFilaments = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly CheckBox chkUnknown = new() { Text = "Unknown filament (not in the list)", AutoSize = true };
    private readonly ComboBox cbMaterial = new() { Width = 120, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly Button btnColor = new() { Text = "Colour...", AutoSize = true };
    private readonly Panel pnlColor = new() { Width = 24, Height = 24, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.Gray };
    private readonly NumericUpDown nudLabelWeight = new() { Minimum = 1, Maximum = 10000, Value = 1000, Width = 70 };
    private readonly Button btnOk = new() { Text = "Add spool", AutoSize = true };

    /// <summary>The spool to add, once the dialog closed with OK.</summary>
    public ManualSpool? Result { get; private set; }

    public ManualSpoolForm(IReadOnlyList<ExternalFilament> filaments)
    {
        this.filaments = filaments;

        Text = "Add spool manually";
        Size = new Size(520, 560);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;

        cbMaterial.Items.AddRange(filaments.Select(x => x.Material).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).Cast<object>().ToArray());

        var unknownPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(3) };
        unknownPanel.Controls.AddRange([chkUnknown, new Label { Text = "Material:", AutoSize = true, Margin = new Padding(12, 6, 0, 0) }, cbMaterial, btnColor, pnlColor,
            new Label { Text = "Label weight (g):", AutoSize = true, Margin = new Padding(12, 6, 0, 0) }, nudLabelWeight]);

        var btnCancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        CancelButton = btnCancel;
        AcceptButton = btnOk;

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        buttons.Controls.AddRange([btnCancel, btnOk]);

        Controls.Add(lbFilaments);
        Controls.Add(txtSearch);
        Controls.Add(unknownPanel);
        Controls.Add(buttons);

        txtSearch.TextChanged += (_, _) => ApplySearch();
        lbFilaments.DoubleClick += (_, _) => Accept();
        chkUnknown.CheckedChanged += (_, _) => UpdateEnabled();
        btnColor.Click += btnColor_Click;
        btnOk.Click += (_, _) => Accept();

        ApplySearch();
        UpdateEnabled();
    }

    private void ApplySearch()
    {
        var matches = ExternalFilamentMatcher.Search(filaments, txtSearch.Text);

        lbFilaments.BeginUpdate();
        lbFilaments.Items.Clear();
        lbFilaments.Items.AddRange(matches.Take(MaxResults).Select(x => new FilamentItem(x)).Cast<object>().ToArray());
        lbFilaments.EndUpdate();

        if (lbFilaments.Items.Count > 0) lbFilaments.SelectedIndex = 0;
    }

    private void UpdateEnabled()
    {
        var unknown = chkUnknown.Checked;

        txtSearch.Enabled = !unknown;
        lbFilaments.Enabled = !unknown;
        cbMaterial.Enabled = unknown;
        btnColor.Enabled = unknown;
        nudLabelWeight.Enabled = unknown;
    }

    private void btnColor_Click(object? sender, EventArgs e)
    {
        using var dialog = new ColorDialog { Color = pnlColor.BackColor, FullOpen = true };

        if (dialog.ShowDialog(this) == DialogResult.OK) pnlColor.BackColor = dialog.Color;
    }

    private void Accept()
    {
        if (chkUnknown.Checked)
        {
            if (string.IsNullOrWhiteSpace(cbMaterial.Text))
            {
                MessageBox.Show(this, "Enter the material, e.g. PLA.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var color = pnlColor.BackColor;
            Result = ManualSpool.Unknown(cbMaterial.Text, $"{color.R:X2}{color.G:X2}{color.B:X2}", (int)nudLabelWeight.Value);
        }
        else
        {
            if (lbFilaments.SelectedItem is not FilamentItem item) return;

            Result = ManualSpool.FromCatalog(item.Filament);
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>A catalog filament as a list entry, e.g. <c>PLA Matte Ice Blue · 1000 g · 1.75 mm · #A3D8E1</c>.</summary>
    private record FilamentItem(ExternalFilament Filament)
    {
        // The list box shows ToString().
        public override string ToString() => string.Join(" · ", new[]
        {
            $"{Filament.Material} {Filament.Name}",
            $"{Filament.Weight.ToString("0", CultureInfo.InvariantCulture)} g",
            $"{Filament.Diameter.ToString("0.00", CultureInfo.InvariantCulture)} mm",
            Filament.ColorHex == null ? null : $"#{Filament.ColorHex}"
        }.Where(x => x != null));
    }
}
