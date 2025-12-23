namespace BambuMan.Desktop
{
    partial class MainForm
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            menuStrip1 = new MenuStrip();
            fileToolStripMenuItem = new ToolStripMenuItem();
            eXitToolStripMenuItem = new ToolStripMenuItem();
            optionsToolStripMenuItem = new ToolStripMenuItem();
            showNfcLogsToolStripMenuItem = new ToolStripMenuItem();
            showADBCommandsToolStripMenuItem = new ToolStripMenuItem();
            writeJsonFilesOnReadToolStripMenuItem = new ToolStripMenuItem();
            logSpoolmanApiToolStripMenuItem = new ToolStripMenuItem();
            unknownFilamentEnabledToolStripMenuItem = new ToolStripMenuItem();
            fullTagScanAndUploadToolStripMenuItem = new ToolStripMenuItem();
            clearLogsToolStripMenuItem = new ToolStripMenuItem();
            testTagToolStripMenuItem = new ToolStripMenuItem();
            txtSpoolmanUrl = new TextBox();
            lblLogs = new Label();
            statusStrip1 = new StatusStrip();
            tsslStatus = new ToolStripStatusLabel();
            rtbLogs = new RichTextBox();
            lblBuyDate = new Label();
            dtpBuyDate = new DateTimePicker();
            lblPrice = new Label();
            nudPrice = new NumericUpDown();
            lblLotNr = new Label();
            lblLocation = new Label();
            txtLotNr = new TextBox();
            txtLocation = new TextBox();
            gbSpoolInfo = new GroupBox();
            lblSppolBuydate = new Label();
            dtpSpoolBuyDate = new DateTimePicker();
            btnUpdate = new Button();
            label1 = new Label();
            lblSpoolWeight = new Label();
            txtSpoolLocation = new TextBox();
            label5 = new Label();
            nudSpoolPrice = new NumericUpDown();
            txtSpoolLotNr = new TextBox();
            nudSpoolWeight = new NumericUpDown();
            lblSpoolPrice = new Label();
            lblInitialWeight = new Label();
            nudInitialWeight = new NumericUpDown();
            lblEmptyWeight = new Label();
            nudEmptyWeight = new NumericUpDown();
            gbImportSettings = new GroupBox();
            lblSpoolmanUrl = new Label();
            btnSetUrl = new Button();
            menuStrip1.SuspendLayout();
            statusStrip1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)nudPrice).BeginInit();
            gbSpoolInfo.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)nudSpoolPrice).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudSpoolWeight).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudInitialWeight).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudEmptyWeight).BeginInit();
            gbImportSettings.SuspendLayout();
            SuspendLayout();
            // 
            // menuStrip1
            // 
            menuStrip1.Items.AddRange(new ToolStripItem[] { fileToolStripMenuItem, optionsToolStripMenuItem, clearLogsToolStripMenuItem, testTagToolStripMenuItem });
            menuStrip1.Location = new Point(0, 0);
            menuStrip1.Name = "menuStrip1";
            menuStrip1.Size = new Size(1300, 24);
            menuStrip1.TabIndex = 0;
            menuStrip1.Text = "menuStrip1";
            // 
            // fileToolStripMenuItem
            // 
            fileToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { eXitToolStripMenuItem });
            fileToolStripMenuItem.Name = "fileToolStripMenuItem";
            fileToolStripMenuItem.Size = new Size(37, 20);
            fileToolStripMenuItem.Text = "File";
            // 
            // eXitToolStripMenuItem
            // 
            eXitToolStripMenuItem.Name = "eXitToolStripMenuItem";
            eXitToolStripMenuItem.Size = new Size(92, 22);
            eXitToolStripMenuItem.Text = "Exit";
            eXitToolStripMenuItem.Click += eXitToolStripMenuItem_Click;
            // 
            // optionsToolStripMenuItem
            // 
            optionsToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { showNfcLogsToolStripMenuItem, showADBCommandsToolStripMenuItem, writeJsonFilesOnReadToolStripMenuItem, logSpoolmanApiToolStripMenuItem, unknownFilamentEnabledToolStripMenuItem, fullTagScanAndUploadToolStripMenuItem });
            optionsToolStripMenuItem.Name = "optionsToolStripMenuItem";
            optionsToolStripMenuItem.Size = new Size(61, 20);
            optionsToolStripMenuItem.Text = "Options";
            // 
            // showNfcLogsToolStripMenuItem
            // 
            showNfcLogsToolStripMenuItem.Checked = true;
            showNfcLogsToolStripMenuItem.CheckOnClick = true;
            showNfcLogsToolStripMenuItem.CheckState = CheckState.Checked;
            showNfcLogsToolStripMenuItem.Name = "showNfcLogsToolStripMenuItem";
            showNfcLogsToolStripMenuItem.Size = new Size(219, 22);
            showNfcLogsToolStripMenuItem.Text = "Show Nfc logs";
            showNfcLogsToolStripMenuItem.CheckStateChanged += showNfcLogsToolStripMenuItem_CheckStateChanged;
            // 
            // showADBCommandsToolStripMenuItem
            // 
            showADBCommandsToolStripMenuItem.Checked = true;
            showADBCommandsToolStripMenuItem.CheckOnClick = true;
            showADBCommandsToolStripMenuItem.CheckState = CheckState.Checked;
            showADBCommandsToolStripMenuItem.Name = "showADBCommandsToolStripMenuItem";
            showADBCommandsToolStripMenuItem.Size = new Size(219, 22);
            showADBCommandsToolStripMenuItem.Text = "Show Apdu Commands";
            showADBCommandsToolStripMenuItem.CheckStateChanged += showADBCommandsToolStripMenuItem_CheckStateChanged;
            // 
            // writeJsonFilesOnReadToolStripMenuItem
            // 
            writeJsonFilesOnReadToolStripMenuItem.Checked = true;
            writeJsonFilesOnReadToolStripMenuItem.CheckOnClick = true;
            writeJsonFilesOnReadToolStripMenuItem.CheckState = CheckState.Checked;
            writeJsonFilesOnReadToolStripMenuItem.Name = "writeJsonFilesOnReadToolStripMenuItem";
            writeJsonFilesOnReadToolStripMenuItem.Size = new Size(219, 22);
            writeJsonFilesOnReadToolStripMenuItem.Text = "Write Json Files On Read";
            writeJsonFilesOnReadToolStripMenuItem.CheckStateChanged += writeJsonFilesOnReadToolStripMenuItem_CheckStateChanged;
            // 
            // logSpoolmanApiToolStripMenuItem
            // 
            logSpoolmanApiToolStripMenuItem.Checked = true;
            logSpoolmanApiToolStripMenuItem.CheckOnClick = true;
            logSpoolmanApiToolStripMenuItem.CheckState = CheckState.Checked;
            logSpoolmanApiToolStripMenuItem.Name = "logSpoolmanApiToolStripMenuItem";
            logSpoolmanApiToolStripMenuItem.Size = new Size(219, 22);
            logSpoolmanApiToolStripMenuItem.Text = "Log Spoolman Api";
            logSpoolmanApiToolStripMenuItem.CheckStateChanged += logSpoolmanApiToolStripMenuItem_CheckStateChanged;
            // 
            // unknownFilamentEnabledToolStripMenuItem
            // 
            unknownFilamentEnabledToolStripMenuItem.Checked = true;
            unknownFilamentEnabledToolStripMenuItem.CheckOnClick = true;
            unknownFilamentEnabledToolStripMenuItem.CheckState = CheckState.Checked;
            unknownFilamentEnabledToolStripMenuItem.Name = "unknownFilamentEnabledToolStripMenuItem";
            unknownFilamentEnabledToolStripMenuItem.Size = new Size(219, 22);
            unknownFilamentEnabledToolStripMenuItem.Text = "Unknown Filament Enabled";
            unknownFilamentEnabledToolStripMenuItem.CheckStateChanged += unknownFilamentEnabledToolStripMenuItem_CheckStateChanged;
            // 
            // fullTagScanAndUploadToolStripMenuItem
            // 
            fullTagScanAndUploadToolStripMenuItem.CheckOnClick = true;
            fullTagScanAndUploadToolStripMenuItem.Name = "fullTagScanAndUploadToolStripMenuItem";
            fullTagScanAndUploadToolStripMenuItem.Size = new Size(219, 22);
            fullTagScanAndUploadToolStripMenuItem.Text = "Full tag scan and upload";
            fullTagScanAndUploadToolStripMenuItem.CheckStateChanged += fullTagScanAndUploadToolStripMenuItem_CheckStateChanged;
            // 
            // clearLogsToolStripMenuItem
            // 
            clearLogsToolStripMenuItem.Name = "clearLogsToolStripMenuItem";
            clearLogsToolStripMenuItem.Size = new Size(71, 20);
            clearLogsToolStripMenuItem.Text = "Clear logs";
            clearLogsToolStripMenuItem.Click += clearLogsToolStripMenuItem_Click;
            // 
            // testTagToolStripMenuItem
            // 
            testTagToolStripMenuItem.Name = "testTagToolStripMenuItem";
            testTagToolStripMenuItem.Size = new Size(60, 20);
            testTagToolStripMenuItem.Text = "Test tag";
            testTagToolStripMenuItem.Click += testTagToolStripMenuItem_Click;
            // 
            // txtSpoolmanUrl
            // 
            txtSpoolmanUrl.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtSpoolmanUrl.Location = new Point(6, 81);
            txtSpoolmanUrl.Name = "txtSpoolmanUrl";
            txtSpoolmanUrl.Size = new Size(735, 23);
            txtSpoolmanUrl.TabIndex = 2;
            // 
            // lblLogs
            // 
            lblLogs.AutoSize = true;
            lblLogs.Location = new Point(12, 146);
            lblLogs.Name = "lblLogs";
            lblLogs.Size = new Size(35, 15);
            lblLogs.TabIndex = 3;
            lblLogs.Text = "Logs:";
            // 
            // statusStrip1
            // 
            statusStrip1.Items.AddRange(new ToolStripItem[] { tsslStatus });
            statusStrip1.Location = new Point(0, 709);
            statusStrip1.Name = "statusStrip1";
            statusStrip1.Size = new Size(1300, 22);
            statusStrip1.TabIndex = 4;
            statusStrip1.Text = "statusStrip1";
            // 
            // tsslStatus
            // 
            tsslStatus.Name = "tsslStatus";
            tsslStatus.Size = new Size(76, 17);
            tsslStatus.Text = "Initializing ....";
            // 
            // rtbLogs
            // 
            rtbLogs.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            rtbLogs.Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point, 186);
            rtbLogs.Location = new Point(12, 164);
            rtbLogs.Name = "rtbLogs";
            rtbLogs.ReadOnly = true;
            rtbLogs.Size = new Size(1276, 542);
            rtbLogs.TabIndex = 1;
            rtbLogs.Text = "";
            rtbLogs.WordWrap = false;
            // 
            // lblBuyDate
            // 
            lblBuyDate.AutoSize = true;
            lblBuyDate.Location = new Point(6, 19);
            lblBuyDate.Name = "lblBuyDate";
            lblBuyDate.Size = new Size(56, 15);
            lblBuyDate.TabIndex = 6;
            lblBuyDate.Text = "Buy date:";
            // 
            // dtpBuyDate
            // 
            dtpBuyDate.Format = DateTimePickerFormat.Short;
            dtpBuyDate.Location = new Point(6, 37);
            dtpBuyDate.Name = "dtpBuyDate";
            dtpBuyDate.Size = new Size(96, 23);
            dtpBuyDate.TabIndex = 7;
            // 
            // lblPrice
            // 
            lblPrice.AutoSize = true;
            lblPrice.Location = new Point(108, 19);
            lblPrice.Name = "lblPrice";
            lblPrice.Size = new Size(36, 15);
            lblPrice.TabIndex = 9;
            lblPrice.Text = "Price:";
            // 
            // nudPrice
            // 
            nudPrice.DecimalPlaces = 2;
            nudPrice.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            nudPrice.Location = new Point(108, 37);
            nudPrice.Maximum = new decimal(new int[] { 500000, 0, 0, 0 });
            nudPrice.Name = "nudPrice";
            nudPrice.Size = new Size(72, 23);
            nudPrice.TabIndex = 10;
            nudPrice.TextAlign = HorizontalAlignment.Right;
            nudPrice.Value = new decimal(new int[] { 12, 0, 0, 0 });
            nudPrice.ValueChanged += nudPrice_ValueChanged;
            // 
            // lblLotNr
            // 
            lblLotNr.AutoSize = true;
            lblLotNr.Location = new Point(186, 19);
            lblLotNr.Name = "lblLotNr";
            lblLotNr.Size = new Size(43, 15);
            lblLotNr.TabIndex = 11;
            lblLotNr.Text = "Lot Nr:";
            // 
            // lblLocation
            // 
            lblLocation.AutoSize = true;
            lblLocation.Location = new Point(326, 19);
            lblLocation.Name = "lblLocation";
            lblLocation.Size = new Size(56, 15);
            lblLocation.TabIndex = 12;
            lblLocation.Text = "Location:";
            // 
            // txtLotNr
            // 
            txtLotNr.Location = new Point(186, 37);
            txtLotNr.Name = "txtLotNr";
            txtLotNr.Size = new Size(134, 23);
            txtLotNr.TabIndex = 13;
            // 
            // txtLocation
            // 
            txtLocation.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtLocation.Location = new Point(326, 37);
            txtLocation.Name = "txtLocation";
            txtLocation.Size = new Size(496, 23);
            txtLocation.TabIndex = 14;
            txtLocation.TextChanged += txtLocation_TextChanged;
            // 
            // gbSpoolInfo
            // 
            gbSpoolInfo.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            gbSpoolInfo.Controls.Add(lblSppolBuydate);
            gbSpoolInfo.Controls.Add(dtpSpoolBuyDate);
            gbSpoolInfo.Controls.Add(btnUpdate);
            gbSpoolInfo.Controls.Add(label1);
            gbSpoolInfo.Controls.Add(lblSpoolWeight);
            gbSpoolInfo.Controls.Add(txtSpoolLocation);
            gbSpoolInfo.Controls.Add(label5);
            gbSpoolInfo.Controls.Add(nudSpoolPrice);
            gbSpoolInfo.Controls.Add(txtSpoolLotNr);
            gbSpoolInfo.Controls.Add(nudSpoolWeight);
            gbSpoolInfo.Controls.Add(lblSpoolPrice);
            gbSpoolInfo.Controls.Add(lblInitialWeight);
            gbSpoolInfo.Controls.Add(nudInitialWeight);
            gbSpoolInfo.Controls.Add(lblEmptyWeight);
            gbSpoolInfo.Controls.Add(nudEmptyWeight);
            gbSpoolInfo.Enabled = false;
            gbSpoolInfo.Location = new Point(846, 30);
            gbSpoolInfo.Name = "gbSpoolInfo";
            gbSpoolInfo.Size = new Size(442, 113);
            gbSpoolInfo.TabIndex = 15;
            gbSpoolInfo.TabStop = false;
            gbSpoolInfo.Text = "Spool info:";
            // 
            // lblSppolBuydate
            // 
            lblSppolBuydate.AutoSize = true;
            lblSppolBuydate.Location = new Point(6, 66);
            lblSppolBuydate.Name = "lblSppolBuydate";
            lblSppolBuydate.Size = new Size(56, 15);
            lblSppolBuydate.TabIndex = 15;
            lblSppolBuydate.Text = "Buy date:";
            // 
            // dtpSpoolBuyDate
            // 
            dtpSpoolBuyDate.Format = DateTimePickerFormat.Short;
            dtpSpoolBuyDate.Location = new Point(6, 84);
            dtpSpoolBuyDate.Name = "dtpSpoolBuyDate";
            dtpSpoolBuyDate.Size = new Size(96, 23);
            dtpSpoolBuyDate.TabIndex = 16;
            // 
            // btnUpdate
            // 
            btnUpdate.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnUpdate.Location = new Point(360, 66);
            btnUpdate.Name = "btnUpdate";
            btnUpdate.Size = new Size(75, 40);
            btnUpdate.TabIndex = 15;
            btnUpdate.Text = "Update Spool";
            btnUpdate.UseVisualStyleBackColor = true;
            btnUpdate.Click += updateSpool_Click;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(216, 66);
            label1.Name = "label1";
            label1.Size = new Size(56, 15);
            label1.TabIndex = 16;
            label1.Text = "Location:";
            // 
            // lblSpoolWeight
            // 
            lblSpoolWeight.AutoSize = true;
            lblSpoolWeight.Location = new Point(244, 19);
            lblSpoolWeight.Name = "lblSpoolWeight";
            lblSpoolWeight.Size = new Size(97, 15);
            lblSpoolWeight.TabIndex = 22;
            lblSpoolWeight.Text = "Spool weight (g):";
            // 
            // txtSpoolLocation
            // 
            txtSpoolLocation.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtSpoolLocation.Location = new Point(216, 84);
            txtSpoolLocation.Name = "txtSpoolLocation";
            txtSpoolLocation.Size = new Size(138, 23);
            txtSpoolLocation.TabIndex = 18;
            txtSpoolLocation.Enter += updateSpool_Click;
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Location = new Point(104, 66);
            label5.Name = "label5";
            label5.Size = new Size(43, 15);
            label5.TabIndex = 15;
            label5.Text = "Lot Nr:";
            // 
            // nudSpoolPrice
            // 
            nudSpoolPrice.DecimalPlaces = 2;
            nudSpoolPrice.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            nudSpoolPrice.Location = new Point(363, 37);
            nudSpoolPrice.Maximum = new decimal(new int[] { 500000, 0, 0, 0 });
            nudSpoolPrice.Name = "nudSpoolPrice";
            nudSpoolPrice.Size = new Size(72, 23);
            nudSpoolPrice.TabIndex = 18;
            nudSpoolPrice.TextAlign = HorizontalAlignment.Right;
            nudSpoolPrice.Value = new decimal(new int[] { 12, 0, 0, 0 });
            nudSpoolPrice.Enter += updateSpool_Click;
            // 
            // txtSpoolLotNr
            // 
            txtSpoolLotNr.Location = new Point(104, 84);
            txtSpoolLotNr.Name = "txtSpoolLotNr";
            txtSpoolLotNr.Size = new Size(106, 23);
            txtSpoolLotNr.TabIndex = 17;
            txtSpoolLotNr.Enter += updateSpool_Click;
            // 
            // nudSpoolWeight
            // 
            nudSpoolWeight.DecimalPlaces = 1;
            nudSpoolWeight.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            nudSpoolWeight.Location = new Point(244, 37);
            nudSpoolWeight.Maximum = new decimal(new int[] { 50000, 0, 0, 0 });
            nudSpoolWeight.Name = "nudSpoolWeight";
            nudSpoolWeight.Size = new Size(113, 23);
            nudSpoolWeight.TabIndex = 23;
            nudSpoolWeight.TextAlign = HorizontalAlignment.Right;
            nudSpoolWeight.Value = new decimal(new int[] { 1250, 0, 0, 0 });
            nudSpoolWeight.KeyDown += nudSpoolWeight_KeyDown;
            // 
            // lblSpoolPrice
            // 
            lblSpoolPrice.AutoSize = true;
            lblSpoolPrice.Location = new Point(363, 19);
            lblSpoolPrice.Name = "lblSpoolPrice";
            lblSpoolPrice.Size = new Size(36, 15);
            lblSpoolPrice.TabIndex = 0;
            lblSpoolPrice.Text = "Price:";
            // 
            // lblInitialWeight
            // 
            lblInitialWeight.AutoSize = true;
            lblInitialWeight.Location = new Point(125, 19);
            lblInitialWeight.Name = "lblInitialWeight";
            lblInitialWeight.Size = new Size(96, 15);
            lblInitialWeight.TabIndex = 21;
            lblInitialWeight.Text = "Initial weight (g):";
            // 
            // nudInitialWeight
            // 
            nudInitialWeight.DecimalPlaces = 1;
            nudInitialWeight.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            nudInitialWeight.Location = new Point(125, 37);
            nudInitialWeight.Maximum = new decimal(new int[] { 50000, 0, 0, 0 });
            nudInitialWeight.Name = "nudInitialWeight";
            nudInitialWeight.Size = new Size(113, 23);
            nudInitialWeight.TabIndex = 21;
            nudInitialWeight.TextAlign = HorizontalAlignment.Right;
            nudInitialWeight.Value = new decimal(new int[] { 1000, 0, 0, 0 });
            nudInitialWeight.Enter += updateSpool_Click;
            // 
            // lblEmptyWeight
            // 
            lblEmptyWeight.AutoSize = true;
            lblEmptyWeight.Location = new Point(6, 19);
            lblEmptyWeight.Name = "lblEmptyWeight";
            lblEmptyWeight.Size = new Size(101, 15);
            lblEmptyWeight.TabIndex = 20;
            lblEmptyWeight.Text = "Empty weight (g):";
            // 
            // nudEmptyWeight
            // 
            nudEmptyWeight.DecimalPlaces = 1;
            nudEmptyWeight.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            nudEmptyWeight.Location = new Point(6, 37);
            nudEmptyWeight.Maximum = new decimal(new int[] { 50000, 0, 0, 0 });
            nudEmptyWeight.Name = "nudEmptyWeight";
            nudEmptyWeight.Size = new Size(113, 23);
            nudEmptyWeight.TabIndex = 19;
            nudEmptyWeight.TextAlign = HorizontalAlignment.Right;
            nudEmptyWeight.Value = new decimal(new int[] { 250, 0, 0, 0 });
            nudEmptyWeight.Enter += updateSpool_Click;
            // 
            // gbImportSettings
            // 
            gbImportSettings.Controls.Add(lblSpoolmanUrl);
            gbImportSettings.Controls.Add(txtSpoolmanUrl);
            gbImportSettings.Controls.Add(lblBuyDate);
            gbImportSettings.Controls.Add(btnSetUrl);
            gbImportSettings.Controls.Add(dtpBuyDate);
            gbImportSettings.Controls.Add(nudPrice);
            gbImportSettings.Controls.Add(lblLocation);
            gbImportSettings.Controls.Add(txtLocation);
            gbImportSettings.Controls.Add(lblLotNr);
            gbImportSettings.Controls.Add(txtLotNr);
            gbImportSettings.Controls.Add(lblPrice);
            gbImportSettings.Location = new Point(12, 30);
            gbImportSettings.Name = "gbImportSettings";
            gbImportSettings.Size = new Size(828, 113);
            gbImportSettings.TabIndex = 16;
            gbImportSettings.TabStop = false;
            gbImportSettings.Text = "Import defaults:";
            // 
            // lblSpoolmanUrl
            // 
            lblSpoolmanUrl.AutoSize = true;
            lblSpoolmanUrl.Location = new Point(6, 63);
            lblSpoolmanUrl.Name = "lblSpoolmanUrl";
            lblSpoolmanUrl.Size = new Size(82, 15);
            lblSpoolmanUrl.TabIndex = 1;
            lblSpoolmanUrl.Text = "Spoolman Url:";
            // 
            // btnSetUrl
            // 
            btnSetUrl.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnSetUrl.Location = new Point(747, 83);
            btnSetUrl.Name = "btnSetUrl";
            btnSetUrl.Size = new Size(75, 23);
            btnSetUrl.TabIndex = 5;
            btnSetUrl.Text = "Change Url";
            btnSetUrl.UseVisualStyleBackColor = true;
            btnSetUrl.Click += btnSetUrl_Click;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1300, 731);
            Controls.Add(gbImportSettings);
            Controls.Add(gbSpoolInfo);
            Controls.Add(rtbLogs);
            Controls.Add(statusStrip1);
            Controls.Add(lblLogs);
            Controls.Add(menuStrip1);
            Icon = (Icon)resources.GetObject("$this.Icon");
            MainMenuStrip = menuStrip1;
            Name = "MainForm";
            Text = "BambuMan";
            menuStrip1.ResumeLayout(false);
            menuStrip1.PerformLayout();
            statusStrip1.ResumeLayout(false);
            statusStrip1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)nudPrice).EndInit();
            gbSpoolInfo.ResumeLayout(false);
            gbSpoolInfo.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)nudSpoolPrice).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudSpoolWeight).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudInitialWeight).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudEmptyWeight).EndInit();
            gbImportSettings.ResumeLayout(false);
            gbImportSettings.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private MenuStrip menuStrip1;
        private ToolStripMenuItem fileToolStripMenuItem;
        private ToolStripMenuItem eXitToolStripMenuItem;
        private TextBox txtSpoolmanUrl;
        private Label lblLogs;
        private StatusStrip statusStrip1;
        private ToolStripStatusLabel tsslStatus;
        private RichTextBox rtbLogs;
        private ToolStripMenuItem optionsToolStripMenuItem;
        private ToolStripMenuItem showADBCommandsToolStripMenuItem;
        private ToolStripMenuItem showNfcLogsToolStripMenuItem;
        private ToolStripMenuItem writeJsonFilesOnReadToolStripMenuItem;
        private ToolStripMenuItem clearLogsToolStripMenuItem;
        private ToolStripMenuItem logSpoolmanApiToolStripMenuItem;
        private Label lblBuyDate;
        private DateTimePicker dtpBuyDate;
        private Label lblPrice;
        private NumericUpDown nudPrice;
        private Label lblLotNr;
        private Label lblLocation;
        private TextBox txtLotNr;
        private TextBox txtLocation;
        private GroupBox gbSpoolInfo;
        private GroupBox gbImportSettings;
        private NumericUpDown nudSpoolPrice;
        private Label lblSpoolPrice;
        private NumericUpDown nudEmptyWeight;
        private Label lblInitialWeight;
        private NumericUpDown nudInitialWeight;
        private Label lblEmptyWeight;
        private Label label1;
        private Label lblSpoolWeight;
        private TextBox txtSpoolLocation;
        private Label label5;
        private TextBox txtSpoolLotNr;
        private NumericUpDown nudSpoolWeight;
        private Label lblSpoolmanUrl;
        private Button btnSetUrl;
        private Button btnUpdate;
        private Label lblSppolBuydate;
        private DateTimePicker dtpSpoolBuyDate;
        private ToolStripMenuItem testTagToolStripMenuItem;
        private ToolStripMenuItem unknownFilamentEnabledToolStripMenuItem;
        private ToolStripMenuItem fullTagScanAndUploadToolStripMenuItem;
    }
}
