using BambuMan.Shared.Managers;
using BambuMan.Shared.Models;
using System.Globalization;

namespace BambuMan.Desktop;

/// <summary>
/// Edit one stored spool: the same fields as the Android stored spool page (weight, empty weight, price, buy date,
/// lot nr, location), with the read-only info a scan shows below them.
/// </summary>
public class LocalSpoolEditForm : Form
{
    private readonly NoBackendManager manager;
    private readonly LocalSpool spool;
    private readonly string key;

    private readonly TextBox txtWeight = new();
    private readonly TextBox txtEmptyWeight = new();
    private readonly TextBox txtPrice = new();
    private readonly DateTimePicker dtpBuyDate = new();
    private readonly TextBox txtLotNr = new();
    private readonly ComboBox cmbLocation = new();
    private readonly TextBox txtInfo = new();
    private readonly ErrorProvider errors = new();

    public LocalSpoolEditForm(NoBackendManager manager, LocalSpool spool)
    {
        this.manager = manager;
        this.spool = spool;
        key = spool.Key!;

        var title = string.Join(" ", new[] { spool.Info.DetailedFilamentType ?? spool.Info.FilamentType, spool.Name }.Where(x => !string.IsNullOrEmpty(x)));

        Text = $"Edit spool - {title}";
        Size = new Size(520, 640);
        MinimumSize = new Size(420, 480);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;

        errors.BlinkStyle = ErrorBlinkStyle.NeverBlink;

        #region Header

        var lblTitle = new Label { Text = title, AutoSize = true, Font = new Font(Font.FontFamily, 11, FontStyle.Bold), Margin = new Padding(3, 3, 3, 0) };
        var lblSub = new Label { Text = $"{key} · Last scanned {spool.LastScanned:yyyy-MM-dd HH:mm}", AutoSize = true, ForeColor = SystemColors.GrayText };

        var header = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(6) };
        header.Controls.AddRange([lblTitle, lblSub]);

        #endregion

        #region Fields

        txtWeight.Text = Format(spool.Weight);
        txtWeight.PlaceholderText = "Not weighed";

        txtEmptyWeight.Text = Format(spool.EmptyWeight);
        txtEmptyWeight.PlaceholderText = $"{BambuddyManager.CoreWeight} (standard core)";

        txtPrice.Text = Format(spool.Price);

        // Unchecked is no buy date.
        dtpBuyDate.Format = DateTimePickerFormat.Short;
        dtpBuyDate.ShowCheckBox = true;
        dtpBuyDate.Value = spool.BuyDate ?? DateTime.Today;
        dtpBuyDate.Checked = spool.BuyDate != null;

        txtLotNr.Text = spool.LotNr ?? "";

        cmbLocation.DropDownStyle = ComboBoxStyle.DropDown;
        cmbLocation.Items.AddRange(manager.ExistingLocations);
        cmbLocation.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        cmbLocation.AutoCompleteSource = AutoCompleteSource.ListItems;
        cmbLocation.Text = spool.Location ?? "";

        var fields = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(6, 0, 24, 6) };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddField(fields, "Weight (g):", txtWeight);
        AddField(fields, "Empty weight (g):", txtEmptyWeight);
        AddField(fields, "Price:", txtPrice);

        // Which optional fields the store keeps; it keeps both, the check mirrors the Android edit card.
        var editFields = manager.EditFields;

        if (editFields.BuyDate) AddField(fields, "Buy date:", dtpBuyDate);
        if (editFields.LotNr) AddField(fields, "Lot nr:", txtLotNr);
        AddField(fields, "Location:", cmbLocation);

        #endregion

        #region Info

        txtInfo.Dock = DockStyle.Fill;
        txtInfo.Multiline = true;
        txtInfo.ReadOnly = true;
        txtInfo.ScrollBars = ScrollBars.Vertical;
        txtInfo.BorderStyle = BorderStyle.None;
        txtInfo.BackColor = SystemColors.Window;

        var gbInfo = new GroupBox { Text = "Spool info", Dock = DockStyle.Fill, Padding = new Padding(8) };
        gbInfo.Controls.Add(txtInfo);

        #endregion

        #region Buttons

        var btnSave = new Button { Text = "Save changes", AutoSize = true };
        btnSave.Click += btnSave_Click;
        AcceptButton = btnSave;

        var btnCancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        CancelButton = btnCancel;

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6), FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.AddRange([btnCancel, btnSave]);

        #endregion

        // Docked from the last added inwards: header, then the fields, buttons at the bottom, the info fills the rest.
        Controls.Add(gbInfo);
        Controls.Add(buttons);
        Controls.Add(fields);
        Controls.Add(header);
    }

    protected override async void OnLoad(EventArgs e)
    {
        try
        {
            base.OnLoad(e);

            // Matched against the catalog again, so it shows what a scan shows.
            var info = await manager.DescribeAsync(spool);

            if (!IsDisposed) txtInfo.Text = string.Join(Environment.NewLine, info.Rows.Select(x => $"{x.Label}: {x.Value}"));
        }
        catch (Exception ex)
        {
            if (!IsDisposed) txtInfo.Text = $"Could not read the spool info: {ex.Message}";
        }
    }

    #region Methods

    private static string Format(decimal? value) => value?.ToString("0.###", CultureInfo.CurrentCulture) ?? "";

    private static void AddField(TableLayoutPanel panel, string label, Control field)
    {
        field.Dock = DockStyle.Fill;
        panel.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 3, 3) });
        panel.Controls.Add(field);
    }

    /// <summary>
    /// The number in <paramref name="field"/>, null when it is empty. Marks the field and returns false when it isn't a
    /// number or is negative — the same check the Android edit card does. Group separators aren't accepted: with them
    /// <c>1.5</c> typed on a German system (or <c>1,5</c> on an English one) would silently become 15.
    /// </summary>
    private bool TryRead(TextBox field, string name, out decimal? value)
    {
        value = null;
        errors.SetError(field, "");

        if (string.IsNullOrWhiteSpace(field.Text)) return true;

        if (!decimal.TryParse(field.Text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed))
        {
            errors.SetError(field, $"{name} must be a number.");
            return false;
        }

        if (parsed < 0)
        {
            errors.SetError(field, $"{name} can't be negative.");
            return false;
        }

        value = parsed;
        return true;
    }

    #endregion

    #region UI Events

    private async void btnSave_Click(object? sender, EventArgs e)
    {
        try
        {
            // Every field is checked, so each wrong one shows its message at once.
            var weightOk = TryRead(txtWeight, "Weight", out var weight);
            var emptyWeightOk = TryRead(txtEmptyWeight, "Empty weight", out var emptyWeight);
            var priceOk = TryRead(txtPrice, "Price", out var price);

            if (!weightOk || !emptyWeightOk || !priceOk) return;

            var input = new SpoolEditInput(
                weight,
                emptyWeight,
                price,
                dtpBuyDate.Checked ? dtpBuyDate.Value.Date : null,
                txtLotNr.Text,
                cmbLocation.Text);

            if (!await manager.UpdateSpoolAsync(key, input))
            {
                MessageBox.Show(this, "This spool is no longer stored.", "Save spool", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            DialogResult = DialogResult.OK;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Saving the spool failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    #endregion
}
