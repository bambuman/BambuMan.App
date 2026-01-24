using BambuMan.Shared;
using BambuMan.Shared.Enums;
using Microsoft.Win32;
using Newtonsoft.Json;
using SpoolMan.Api.Model;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using LogLevel = BambuMan.Shared.Enums.LogLevel;

namespace BambuMan.Desktop;

public partial class MainForm : Form
{
    private const string RegSubKey = "BambuMan";
    private const string RegKeyShowApduCommands = "ShowApduCommands";
    private const string RegKeyShowLogsNfcLogs = "ShowLogsNfcLogs";
    private const string RegKeyWriteJsonFiles = "WriteJsonFiles";
    private const string RegKeyLogSpoolmanApi = "LogSpoolmanApi";
    private const string RegKeySpoolmanUrl = "SpoolmanUrl";
    private const string RegKeyUnknownFilamentEnabled = "UnknownFilamentEnabled";
    private const string RegKeyFullTagScanAndUpload = "FullTagScanAndUpload";

    private const string RegKeyPrice = "Price";
    private const string RegKeyLocation = "Location";

    private readonly NfcReader? nfcReader;
    private readonly SpoolmanManager? spoolmanManager;
    private readonly TagApiService? tagApiService;
    private Spool? currentSpool;
    private BambuFillamentInfo? currentBambuFillamentInfo;

    public MainForm()
    {
        InitializeComponent();

#if !DEBUG
        testTagToolStripMenuItem.Visible = false;
        testTagsToolStripMenuItem.Visible = false;
#endif

        showADBCommandsToolStripMenuItem.Checked = GetRegistryValue(RegKeyShowApduCommands, false);
        showNfcLogsToolStripMenuItem.Checked = GetRegistryValue(RegKeyShowLogsNfcLogs, false);
        writeJsonFilesOnReadToolStripMenuItem.Checked = GetRegistryValue(RegKeyWriteJsonFiles, false);
        logSpoolmanApiToolStripMenuItem.Checked = GetRegistryValue(RegKeyLogSpoolmanApi, false);
        unknownFilamentEnabledToolStripMenuItem.Checked = GetRegistryValue(RegKeyUnknownFilamentEnabled, true);
        fullTagScanAndUploadToolStripMenuItem.Checked = GetRegistryValue(RegKeyFullTagScanAndUpload, false);
        txtSpoolmanUrl.Text = GetRegistryValue(RegKeySpoolmanUrl, string.Empty);
        nudPrice.Value = GetRegistryValue(RegKeyPrice, 12.0m);
        txtLocation.Text = GetRegistryValue(RegKeyLocation, string.Empty);

        nfcReader = new NfcReader
        {
            ShowLogs = showNfcLogsToolStripMenuItem.Checked,
            ShowApduCommands = showADBCommandsToolStripMenuItem.Checked,
            WriteJsonFiles = writeJsonFilesOnReadToolStripMenuItem.Checked,
            FullTagScanAndUpload = fullTagScanAndUploadToolStripMenuItem.Checked,
        };

        nfcReader.OnLogMessage += NfcReaderOnOnLogMessage;
        nfcReader.OnSpoolFound += NfcReaderOnOnSpoolFound;

        nfcReader.Start();

        spoolmanManager = new SpoolmanManager(null)
        {
            ShowLogs = logSpoolmanApiToolStripMenuItem.Checked,
            ApiUrl = txtSpoolmanUrl.Text,
            UnknownFilamentEnabled = unknownFilamentEnabledToolStripMenuItem.Checked
        };

        spoolmanManager.OnStatusChanged += SpoolmanManagerOnStatusChanged;
        spoolmanManager.OnLogMessage += SpoolmanManagerOnLogMessage;
        spoolmanManager.OnSpoolFound += SpoolmanManagerOnSpoolFound;

        tagApiService = new TagApiService(new HttpClient()) { LogAction = AppendText };
    }

    protected override async void OnLoad(EventArgs e)
    {
        try
        {
            base.OnLoad(e);
            if (spoolmanManager != null) await spoolmanManager.Init();
        }
        catch (Exception ex)
        {
            AppendText(LogLevel.Error, ex.ToString());
        }
    }

    #region Events

    private async void NfcReaderOnOnSpoolFound(BambuFillamentInfo info)
    {
        try
        {
            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(delegate { NfcReaderOnOnSpoolFound(info); }));
                return;
            }

            var json = JsonConvert.SerializeObject(info, Formatting.Indented);
            AppendText(LogLevel.Information, json);

            if (spoolmanManager != null) await spoolmanManager.InventorySpool(info, dtpBuyDate.Value, nudPrice.Value, txtLotNr.Text, txtLocation.Text);
            if (fullTagScanAndUploadToolStripMenuItem.Checked && tagApiService != null) await tagApiService.UploadNfcTagAsync(info);
        }
        catch (Exception ex)
        {
            AppendText(LogLevel.Error, ex.ToString());
        }
    }

    private void NfcReaderOnOnLogMessage(LogLevel level, string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new MethodInvoker(delegate { NfcReaderOnOnLogMessage(level, message); }));
            return;
        }

        AppendText(level, message);
    }

    private void SpoolmanManagerOnLogMessage(LogLevel level, string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new MethodInvoker(delegate { SpoolmanManagerOnLogMessage(level, message); }));
            return;
        }

        AppendText(level, message);
    }

    private void SpoolmanManagerOnSpoolFound(Spool spool, BambuFillamentInfo info)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new MethodInvoker(delegate { SpoolmanManagerOnSpoolFound(spool, info); }));
            return;
        }

        nudEmptyWeight.Value = spool.SpoolWeight ?? spool.Filament.SpoolWeight ?? spool.Filament.Vendor?.EmptySpoolWeight ?? 0;
        nudInitialWeight.Value = spool.InitialWeight ?? spool.Filament.Weight ?? 0;
        nudSpoolWeight.Value = Math.Max(0, nudEmptyWeight.Value + nudInitialWeight.Value - spool.UsedWeight);
        nudSpoolPrice.Value = spool.Price ?? spool.Filament.Price ?? 0;
        txtLotNr.Text = spool.LotNr;
        txtSpoolLocation.Text = spool.Location;
        dtpSpoolBuyDate.Value = spool.Extra.TryGetValue("buy_date", out var buyDate) ? DateTime.Parse(buyDate.Replace("\"", "")) : DateTime.Today;

        gbSpoolInfo.Enabled = true;

        nudSpoolWeight.Focus();
        nudSpoolWeight.Select(0, 40);

        currentSpool = spool;
        currentBambuFillamentInfo = info;
    }

    private void SpoolmanManagerOnStatusChanged()
    {
        if (InvokeRequired)
        {
            BeginInvoke(new MethodInvoker(SpoolmanManagerOnStatusChanged));
            return;
        }

        tsslStatus.Text = (spoolmanManager?.Status ?? SpoolmanManagerStatusType.Initializing).GetDescriptionAttr();
    }

    #endregion

    #region Registry read and write

    private TClass? GetRegistryValue<TClass>(string key, TClass? defaultValue)
    {
        if (!OperatingSystem.IsWindows()) return defaultValue;

        var baseKey = Registry.CurrentUser;

        using var software = baseKey.OpenSubKey("Software", true) ?? baseKey.CreateSubKey("Software");
        using var subKey = software.OpenSubKey(RegSubKey, true) ?? software.CreateSubKey(RegSubKey);

        if (defaultValue is bool boolValue) return (TClass)(object)(subKey.GetValue(key, boolValue).ToString() == "True");
        if (defaultValue is decimal decimalValue) return (TClass)(object)(decimal.TryParse(subKey.GetValue(key, decimalValue).ToString(), NumberStyles.Any, CultureInfo.CurrentCulture, out var result) ? result : defaultValue);

        return (TClass?)subKey.GetValue(key, defaultValue);
    }

    private TClass SetRegistryValue<TClass>(string key, TClass value)
    {
        if (!OperatingSystem.IsWindows()) return value;

        var baseKey = Registry.CurrentUser;

        using var software = baseKey.OpenSubKey("Software", true) ?? baseKey.CreateSubKey("Software");
        using var subKey = software.OpenSubKey(RegSubKey, true) ?? software.CreateSubKey(RegSubKey);

        subKey.SetValue(key, value ?? throw new ArgumentNullException(nameof(value)));

        return value;
    }

    #endregion

    #region UI Events

    private void showADBCommandsToolStripMenuItem_CheckStateChanged(object? sender, EventArgs e)
    {
        var value = SetRegistryValue(RegKeyShowApduCommands, showADBCommandsToolStripMenuItem.Checked);
        if (nfcReader != null) nfcReader.ShowApduCommands = value;
    }

    private void showNfcLogsToolStripMenuItem_CheckStateChanged(object? sender, EventArgs e)
    {
        var value = SetRegistryValue(RegKeyShowLogsNfcLogs, showNfcLogsToolStripMenuItem.Checked);
        if (nfcReader != null) nfcReader.ShowLogs = value;
    }

    private void writeJsonFilesOnReadToolStripMenuItem_CheckStateChanged(object? sender, EventArgs e)
    {
        var value = SetRegistryValue(RegKeyWriteJsonFiles, writeJsonFilesOnReadToolStripMenuItem.Checked);
        if (nfcReader != null) nfcReader.WriteJsonFiles = value;
    }

    private void logSpoolmanApiToolStripMenuItem_CheckStateChanged(object sender, EventArgs e)
    {
        var value = SetRegistryValue(RegKeyLogSpoolmanApi, logSpoolmanApiToolStripMenuItem.Checked);
        if (spoolmanManager != null) spoolmanManager.ShowLogs = value;
    }

    private void unknownFilamentEnabledToolStripMenuItem_CheckStateChanged(object sender, EventArgs e)
    {
        var value = SetRegistryValue(RegKeyUnknownFilamentEnabled, unknownFilamentEnabledToolStripMenuItem.Checked);
        if (spoolmanManager != null) spoolmanManager.UnknownFilamentEnabled = value;
    }

    private void fullTagScanAndUploadToolStripMenuItem_CheckStateChanged(object sender, EventArgs e)
    {
        var value = SetRegistryValue(RegKeyFullTagScanAndUpload, fullTagScanAndUploadToolStripMenuItem.Checked);
        if (nfcReader != null) nfcReader.FullTagScanAndUpload = value;
    }

    private void nudPrice_ValueChanged(object sender, EventArgs e)
    {
        SetRegistryValue(RegKeyPrice, nudPrice.Value);
    }

    private void txtLocation_TextChanged(object sender, EventArgs e)
    {
        SetRegistryValue(RegKeyLocation, txtLocation.Text);
    }

    private async void btnSetUrl_Click(object sender, EventArgs e)
    {
        try
        {
            SetRegistryValue(RegKeySpoolmanUrl, txtSpoolmanUrl.Text);

            if (spoolmanManager == null) return;

            spoolmanManager.ApiUrl = txtSpoolmanUrl.Text;
            await spoolmanManager.Init();
        }
        catch (Exception ex)
        {
            AppendText(LogLevel.Error, ex.ToString());
        }
    }

    private void clearLogsToolStripMenuItem_Click(object? sender, EventArgs e)
    {
        lock (rtbLogs)
        {
            rtbLogs.Clear();
        }
    }

    private async void updateSpool_Click(object sender, EventArgs e)
    {
        try
        {
            if (spoolmanManager == null || currentSpool == null) return;

            await spoolmanManager.UpdateSpool(
                currentSpool,
                dtpBuyDate.Value,
                nudSpoolPrice.Value,
                txtLotNr.Text,
                txtLocation.Text,
                nudEmptyWeight.Value,
                nudInitialWeight.Value,
                nudSpoolWeight.Value,
                currentBambuFillamentInfo?.TrayUid,
                currentBambuFillamentInfo?.ProductionDateTime);

            gbSpoolInfo.Enabled = false;
        }
        catch (Exception ex)
        {
            AppendText(LogLevel.Error, ex.ToString());
        }
    }

    private void nudSpoolWeight_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter) return;
        updateSpool_Click(this, EventArgs.Empty);
    }

    private void eXitToolStripMenuItem_Click(object? sender, EventArgs e)
    {
        Application.Exit();
    }

    #endregion

    #region Helpers

    public void AppendText(LogLevel level, string text)
    {
        lock (rtbLogs)
        {
            var color = level switch
            {
                LogLevel.None or LogLevel.Trace or LogLevel.Debug => Color.DimGray,
                LogLevel.Information => Color.Black,
                LogLevel.Success => Color.DarkGreen,
                LogLevel.Warning => Color.DarkOrange,
                LogLevel.Error or LogLevel.Critical => Color.Maroon,
                _ => throw new ArgumentOutOfRangeException(nameof(level), level, null)
            };

            rtbLogs.SuspendLayout();
            rtbLogs.SelectionColor = color;
            rtbLogs.AppendText($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]: {text}\r\n");
            rtbLogs.ScrollToCaret();
            rtbLogs.ResumeLayout();
        }
    }

    #endregion

    [SuppressMessage("ReSharper", "StringLiteralTypo")]
    private async void testTagToolStripMenuItem_Click(object sender, EventArgs e)
    {
        #region Decode raw files

        //        try
        //        {
        //            var files = new DirectoryInfo(@"C:\_Repos\_bambuman\Bambu-Lab-RFID-Library\bin").GetFiles("*", SearchOption.AllDirectories);

        //            var classes = new List<string>();

        //            foreach (var materialGroup in files.GroupBy(x => x.Directory?.Parent?.Parent?.Name ?? ""))
        //            {
        //                var className = ConvertToCamelCase(materialGroup.Key);

        //                var methods = new List<string>();

        //                foreach (var typeGroup in materialGroup.GroupBy(x => x.Directory?.Parent?.Name ?? ""))
        //                {
        //                    var methodName = ConvertToCamelCase(typeGroup.Key);

        //                    var dataFile = typeGroup.First();

        //                    var data = await File.ReadAllBytesAsync(dataFile.FullName);

        //                    var blockData = new byte[20][];

        //                    var blockNum = 0;

        //                    for (var i = 0; i < 5; i++)
        //                    {
        //                        for (var ii = 0; ii < 3; ii++)
        //                        {
        //                            blockData[blockNum] = data.Skip(blockNum * 16).Take(16).ToArray();
        //                            //SendCmd("Read Binary: ", reader, [0xFF, 0xB0, 0x00, (byte)blockNum, 0x10]) ?? [16];
        //                            blockNum++;
        //                        }

        //                        blockNum++;
        //                    }

        //                    var info = new BambuFillamentInfo();
        //                    info.ParseData(blockData, true);

        //                    var infoJson = JsonConvert.SerializeObject(info, Formatting.None);

        //                    methods.Add(@$"
        //        [Fact(DisplayName = ""{methodName}"")]
        //        public async Task {methodName}()
        //        {{
        //            var json = ""{infoJson.Replace("\"", "\\\"")}"";

        //            var (_, external) = await GetExternalFilament(json);

        //            Assert.Equal(""{typeGroup.Key}"", external?.Name);
        //            Assert.Equal(""{info.DetailedFilamentType}"", external?.Material);
        //        }}");
        //                }


        //                classes.Add(@$"namespace BambuMan.Shared.Test.Filaments
        //{{
        //    [Trait(""Category"", ""{materialGroup.Key}"")]
        //    public class {className}Test : BaseTest
        //    {{
        //{string.Join("\r\n", methods)}
        //    }}
        //}}
        //");
        //            }


        //            File.WriteAllText("D:\\tests.txt", string.Join("\r\n", classes));
        //        }
        //        catch (Exception ex)
        //        {
        //            AppendText(LogLevel.Error, ex.ToString());
        //        }

        #endregion

        //var json = "{\"SerialNumber\":\"4A5EAD3B\",\"TagManufacturerData\":\"gggEAAStJvpSS1SQ\",\"MaterialVariantIdentifier\":\"A00-M0\",\"UniqueMaterialIdentifier\":\"FA00\",\"FilamentType\":\"PLA\",\"DetailedFilamentType\":\"PLA Basic\",\"Color\":\"9CDBD9FF\",\"SpoolWeight\":1000,\"FilamentDiameter\":1.75,\"DryingTemperature\":55,\"DryingTime\":8,\"BedTemperatureType\":1,\"BedTemperature\":35,\"MaxTemperatureForHotend\":230,\"MinTemperatureForHotend\":190,\"XCamInfo\":\"iBPQB+gD6APNzEw/\",\"NozzleDiameter\":0.2,\"TrayUid\":\"FEE8B504E67B4147A855166BE75D9E6C\",\"SpoolWidth\":6625,\"ProductionDateTime\":\"2024-04-02T15:21:00\",\"ProductionDateTimeShort\":\"24_04_02_15\",\"FilamentLength\":330,\"FormatIdentifier\":2,\"ColorCount\":2,\"SecondColor\":\"FFFFFFFF\"}";
        //var json = "{\"SerialNumber\":\"B71F10BA\",\"TagManufacturerData\":\"AggEAAPyh3mG90eQ\",\"MaterialVariantIdentifier\":\"N04-K0\",\"UniqueMaterialIdentifier\":\"FN04\",\"FilamentType\":\"PA-CF\",\"DetailedFilamentType\":\"PAHT-CF\",\"Color\":\"000000FF\",\"SpoolWeight\":500,\"FilamentDiameter\":1.75,\"DryingTemperature\":80,\"DryingTime\":12,\"BedTemperatureType\":2,\"BedTemperature\":100,\"MaxTemperatureForHotend\":280,\"MinTemperatureForHotend\":270,\"XCamInfo\":\"NCHsLOgD6AMzMzM/\",\"NozzleDiameter\":0.2,\"TrayUid\":\"15D84264B4EA49B2BEE0C4405C732B8B\",\"SpoolWidth\":666,\"ProductionDateTime\":\"2022-12-01T02:34:00\",\"ProductionDateTimeShort\":\"A221201\",\"FilamentLength\":196,\"FormatIdentifier\":0,\"ColorCount\":0,\"SecondColor\":\"00000000\",\"SkuStart\":\"N04-K0-1.75-500\"}";
        //var json = "{\"SerialNumber\":\"DAC888FB\",\"TagManufacturerData\":\"YQgEAASo9nmRwduQ\",\"MaterialVariantIdentifier\":\"G01-C0\",\"UniqueMaterialIdentifier\":\"FG01\",\"FilamentType\":\"PETG\",\"DetailedFilamentType\":\"PETG Translucent\",\"Color\":\"00000000\",\"SpoolWeight\":1000,\"FilamentDiameter\":1.75,\"DryingTemperature\":65,\"DryingTime\":8,\"BedTemperatureType\":0,\"BedTemperature\":0,\"MaxTemperatureForHotend\":260,\"MinTemperatureForHotend\":230,\"XCamInfo\":\"pDiAPrwCIAMAAIA/\",\"NozzleDiameter\":0.2,\"TrayUid\":\"697C12C070E044FAA79740483B46139A\",\"SpoolWidth\":2875,\"ProductionDateTime\":\"2024-08-17T16:11:00\",\"ProductionDateTimeShort\":\"20240817\",\"FilamentLength\":330,\"FormatIdentifier\":2,\"ColorCount\":1,\"SecondColor\":\"00000000\",\"SkuStart\":\"G01-C0-1.75-1000\"}";
        //var json = "{\"SerialNumber\":\"3526771C\",\"TagManufacturerData\":\"eAgEAAPFE3AN9t2Q\",\"MaterialVariantIdentifier\":\"S00-W0\",\"UniqueMaterialIdentifier\":\"FS00\",\"FilamentType\":\"Support\",\"DetailedFilamentType\":\"Support W\",\"Color\":\"FFFFFFFF\",\"SpoolWeight\":250,\"FilamentDiameter\":1.75,\"DryingTemperature\":55,\"DryingTime\":8,\"BedTemperatureType\":1,\"BedTemperature\":40,\"MaxTemperatureForHotend\":220,\"MinTemperatureForHotend\":220,\"XCamInfo\":\"NCHQB+gD6AOamRk/\",\"NozzleDiameter\":0.2,\"TrayUid\":\"1EACC234E93F49268139474ACBA4E144\",\"SpoolWidth\":1149,\"ProductionDateTime\":\"2022-07-11T22:50:00\",\"ProductionDateTimeShort\":\"2207090310\",\"FilamentLength\":80,\"FormatIdentifier\":0,\"ColorCount\":0,\"SecondColor\":\"00000000\",\"SkuStart\":\"S00-W0-1.75-250\"}";
        //var json = "{\"SerialNumber\":\"40AD215F\",\"TagManufacturerData\":\"kwgEAAPrlfTEai6Q\",\"MaterialVariantIdentifier\":\"A50-B9\",\"UniqueMaterialIdentifier\":\"FA50\",\"FilamentType\":\"PLA-CF\",\"DetailedFilamentType\":\"PLA-CF\",\"Color\":\"6E88BCFF\",\"SpoolWeight\":1000,\"FilamentDiameter\":1.75,\"DryingTemperature\":55,\"DryingTime\":8,\"BedTemperatureType\":1,\"BedTemperature\":45,\"MaxTemperatureForHotend\":240,\"MinTemperatureForHotend\":200,\"XCamInfo\":\"0AfQB4QD6ANmZmY/\",\"NozzleDiameter\":0.2,\"TrayUid\":\"2651585B582F4AF09061714A5AC2E342\",\"SpoolWidth\":666,\"ProductionDateTime\":\"2023-06-12T16:23:00\",\"ProductionDateTimeShort\":\"202306012\",\"FilamentLength\":350,\"FormatIdentifier\":2,\"ColorCount\":1,\"SecondColor\":\"00000000\",\"SkuStart\":\"A50-B9-1.75-1000\"}";
        //var json = "{\"SerialNumber\":\"7AEDA1FD\",\"TagManufacturerData\":\"ywgEAAThczaFQziQ\",\"MaterialVariantIdentifier\":\"C01-W0\",\"UniqueMaterialIdentifier\":\"FC01\",\"FilamentType\":\"PC\",\"DetailedFilamentType\":\"PC FR\",\"Color\":\"FFFFFFFF\",\"SpoolWeight\":1000,\"FilamentDiameter\":1.75,\"DryingTemperature\":80,\"DryingTime\":8,\"BedTemperatureType\":0,\"BedTemperature\":0,\"MaxTemperatureForHotend\":280,\"MinTemperatureForHotend\":260,\"XCamInfo\":\"AAAAAAAAAAAAAAAA\",\"NozzleDiameter\":0.2,\"TrayUid\":\"F6D149EC530E4E82B4EFFD6C45EDC4A0\",\"SpoolWidth\":6625,\"ProductionDateTime\":\"2024-12-12T12:26:00\",\"ProductionDateTimeShort\":\"24_12_12_12\",\"FilamentLength\":350,\"FormatIdentifier\":2,\"ColorCount\":1,\"SecondColor\":\"00000000\",\"SkuStart\":\"C01-W0-1.75-1000\"}";
        //var json = "{\"SerialNumber\":\"83EC9A1C\",\"TagManufacturerData\":\"6QgEAAR8EZ2x4zmQ\",\"MaterialVariantIdentifier\":\"A17-R1\",\"UniqueMaterialIdentifier\":\"FA17\",\"FilamentType\":\"PLA\",\"DetailedFilamentType\":\"PLA Translucent\",\"Color\":\"F5B6CD80\",\"SpoolWeight\":1000,\"FilamentDiameter\":1.75,\"DryingTemperature\":55,\"DryingTime\":8,\"BedTemperatureType\":0,\"BedTemperature\":0,\"MaxTemperatureForHotend\":240,\"MinTemperatureForHotend\":200,\"XCamInfo\":\"AAAAAAAAAAAAAAAA\",\"NozzleDiameter\":0.2,\"TrayUid\":\"2DC9E553D1924FA89FBB893C9E921DBA\",\"SpoolWidth\":666,\"ProductionDateTime\":\"2024-12-20T09:40:00\",\"ProductionDateTimeShort\":\"20241220\",\"FilamentLength\":345,\"FormatIdentifier\":2,\"ColorCount\":1,\"SecondColor\":\"00000000\",\"SkuStart\":\"A17-R1-1.75-1000\"}";
        //var json = "{\"SerialNumber\":\"5B1449F6\",\"TagManufacturerData\":\"8AgEAATrOVf5DLaQ\",\"MaterialVariantIdentifier\":\"A16-G0\",\"UniqueMaterialIdentifier\":\"FA16\",\"FilamentType\":\"PLA\",\"DetailedFilamentType\":\"PLA Wood\",\"Color\":\"918669FF\",\"SpoolWeight\":1000,\"FilamentDiameter\":1.75,\"DryingTemperature\":60,\"DryingTime\":6,\"BedTemperatureType\":0,\"BedTemperature\":0,\"MaxTemperatureForHotend\":230,\"MinTemperatureForHotend\":190,\"XCamInfo\":\"AAAAAAAAAAAAAAAA\",\"NozzleDiameter\":0.2,\"TrayUid\":\"4663E9ADF9CC454380EB58CE627BFE72\",\"SpoolWidth\":1536,\"ProductionDateTime\":\"2025-03-11T00:38:00\",\"ProductionDateTimeShort\":\"25_03_11_00\",\"FilamentLength\":330,\"FormatIdentifier\":2,\"ColorCount\":1,\"SecondColor\":\"00000000\",\"SkuStart\":\"A16-G0-1.75-1000\"}";
        //var json = "{\"SerialNumber\":\"C3DB40A2\",\"TagManufacturerData\":\"+ggEAARS8na7x5uQ\",\"MaterialVariantIdentifier\":\"A00-D0\",\"UniqueMaterialIdentifier\":\"FA00\",\"FilamentType\":\"PLA\",\"DetailedFilamentType\":\"PLA Basic\",\"Color\":\"8E9089FF\",\"SpoolWeight\":1000,\"FilamentDiameter\":1.75,\"DryingTemperature\":55,\"DryingTime\":8,\"BedTemperatureType\":0,\"BedTemperature\":0,\"MaxTemperatureForHotend\":230,\"MinTemperatureForHotend\":190,\"XCamInfo\":\"0AfQB+gD6AOamRk/\",\"NozzleDiameter\":0.2,\"TrayUid\":\"F1FACEE5124249F6AEB7DCEC0AAE0C4F\",\"SpoolWidth\":2875,\"ProductionDateTime\":\"2025-01-20T19:14:00\",\"ProductionDateTimeShort\":\"20250120\",\"FilamentLength\":330,\"FormatIdentifier\":2,\"ColorCount\":1,\"SecondColor\":\"00000000\",\"SkuStart\":\"A00-D0-1.75-1000\"}";
        //var json = "{\"SerialNumber\":\"83EC9A1C\",\"TagManufacturerData\":\"6QgEAAR8EZ2x4zmQ\",\"MaterialVariantIdentifier\":\"A17-R1\",\"UniqueMaterialIdentifier\":\"FA17\",\"FilamentType\":\"PLA\",\"DetailedFilamentType\":\"PLA Translucent\",\"Color\":\"F5B6CD80\",\"SpoolWeight\":1000,\"FilamentDiameter\":1.75,\"DryingTemperature\":55,\"DryingTime\":8,\"BedTemperatureType\":0,\"BedTemperature\":0,\"MaxTemperatureForHotend\":240,\"MinTemperatureForHotend\":200,\"XCamInfo\":\"AAAAAAAAAAAAAAAA\",\"NozzleDiameter\":0.2,\"TrayUid\":\"2DC9E553D1924FA89FBB893C9E921DBA\",\"SpoolWidth\":666,\"ProductionDateTime\":\"2024-12-20T09:40:00\",\"ProductionDateTimeShort\":\"20241220\",\"FilamentLength\":345,\"FormatIdentifier\":2,\"ColorCount\":1,\"SecondColor\":\"00000000\",\"SkuStart\":\"A17-R1-1.75-1000\"}";

        var json = "{\"SerialNumber\":\"5B1449F6\",\"TagManufacturerData\":\"8AgEAATrOVf5DLaQ\",\"MaterialVariantIdentifier\":\"A16-G0\",\"UniqueMaterialIdentifier\":\"FA16\",\"FilamentType\":\"PLA\",\"DetailedFilamentType\":\"PLA Wood\",\"Color\":\"918669FF\",\"SpoolWeight\":1000,\"FilamentDiameter\":1.75,\"DryingTemperature\":60,\"DryingTime\":6,\"BedTemperatureType\":0,\"BedTemperature\":0,\"MaxTemperatureForHotend\":230,\"MinTemperatureForHotend\":190,\"XCamInfo\":\"AAAAAAAAAAAAAAAA\",\"NozzleDiameter\":0.2,\"TrayUid\":\"4663E9ADF9CC454380EB58CE627BFE72\",\"SpoolWidth\":1536,\"ProductionDateTime\":\"2025-03-11T00:38:00\",\"ProductionDateTimeShort\":\"25_03_11_00\",\"FilamentLength\":330,\"FormatIdentifier\":2,\"ColorCount\":1,\"SecondColor\":\"00000000\",\"SkuStart\":\"A16-G0-1.75-1000\"}";

        var bambuFillamentInfo = JsonConvert.DeserializeObject<BambuFillamentInfo>(json);

        json = JsonConvert.SerializeObject(bambuFillamentInfo, Formatting.Indented);
        AppendText(LogLevel.Information, json);

        if (spoolmanManager != null) await spoolmanManager.InventorySpool(bambuFillamentInfo!, DateTime.Today, 12, string.Empty, string.Empty);

        await Task.Delay(2000);

        json = "{\"SerialNumber\":\"0509F50D\",\"TagManufacturerData\":\"9AgEAASm165pzJOQ\",\"MaterialVariantIdentifier\":\"A16-R0\",\"UniqueMaterialIdentifier\":\"FA16\",\"FilamentType\":\"PLA\",\"DetailedFilamentType\":\"PLA Wood\",\"Color\":\"3F231CFF\",\"SpoolWeight\":1000,\"FilamentDiameter\":1.75,\"DryingTemperature\":60,\"DryingTime\":6,\"BedTemperatureType\":0,\"BedTemperature\":0,\"MaxTemperatureForHotend\":230,\"MinTemperatureForHotend\":190,\"XCamInfo\":\"AAAAAAAAAAAAAAAA\",\"NozzleDiameter\":0.2,\"TrayUid\":\"26E72842404F41F2A227FC7276299DFA\",\"SpoolWidth\":1536,\"ProductionDateTime\":\"2025-03-24T14:32:00\",\"ProductionDateTimeShort\":\"25_03_24_14\",\"FilamentLength\":330,\"FormatIdentifier\":2,\"ColorCount\":1,\"SecondColor\":\"00000000\",\"SkuStart\":\"A16-R0-1.75-1000\"}";

        bambuFillamentInfo = JsonConvert.DeserializeObject<BambuFillamentInfo>(json);

        json = JsonConvert.SerializeObject(bambuFillamentInfo, Formatting.Indented);
        AppendText(LogLevel.Information, json);

        if (spoolmanManager != null) await spoolmanManager.InventorySpool(bambuFillamentInfo!, DateTime.Today, 12, string.Empty, string.Empty);
    }

    [SuppressMessage("ReSharper", "StringLiteralTypo")]
    private async void testTagsToolStripMenuItem_Click(object sender, EventArgs e)
    {
        try
        {
            var files = new DirectoryInfo(@"C:\_Repos\_bambuman\Bambu-Lab-RFID-Library\").GetFiles("*.bin", SearchOption.AllDirectories);
            //var files = new DirectoryInfo(@"C:\_Repos\_bambuman\non_match_nfc\").GetFiles("*.bin", SearchOption.AllDirectories);

            foreach (var file in files)
            {
                var data = await File.ReadAllBytesAsync(file.FullName);
                if (data.Length != 1024) continue;

                var uid = data[..4];
                var bambuFillamentInfo = new BambuFillamentInfo(uid);
                bambuFillamentInfo.ParseData(data);

                if (spoolmanManager != null)
                {
                    var ok = await spoolmanManager.InventorySpool(bambuFillamentInfo, DateTime.Today, 12, string.Empty, string.Empty);

                    if (ok && fullTagScanAndUploadToolStripMenuItem.Checked && tagApiService != null) await tagApiService.UploadNfcTagAsync(bambuFillamentInfo);

                    if (!ok) File.Copy(file.FullName, Path.Combine("C:\\_Repos\\_bambuman\\non_match_nfc", $"{DateTime.Now:yyyy-MM-dd_HHmmss}_{file.Name}"));
                    //if (ok) File.Delete(file.FullName);
                }

                await Task.Delay(1000);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.ToString());
        }
    }
}