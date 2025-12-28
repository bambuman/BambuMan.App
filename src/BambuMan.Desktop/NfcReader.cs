// ReSharper disable LocalizableElement

using BambuMan.Shared;
using Newtonsoft.Json;
using PCSC;
using PCSC.Exceptions;
using PCSC.Iso7816;
using PCSC.Monitoring;
using System.Diagnostics;
using LogLevel = BambuMan.Shared.Enums.LogLevel;

namespace BambuMan.Desktop;

public class NfcReader
{
    public delegate void LogMessageEventHandler(LogLevel level, string message);
    public delegate void SpoolFoundEventHandler(BambuFillamentInfo info);

    public event LogMessageEventHandler? OnLogMessage;
    public event SpoolFoundEventHandler? OnSpoolFound;

    private ISCardMonitor? monitor;

    public bool ShowApduCommands { get; set; }

    public bool ShowLogs { get; set; }

    public bool WriteJsonFiles { get; set; }

    public bool FullTagScanAndUpload { get; set; }

    public void Start()
    {
        var contextFactory = ContextFactory.Instance;

        using var context = contextFactory.Establish(SCardScope.System);

        var readerNames = context.GetReaders();

        if (ShowLogs) OnLogMessage?.Invoke(LogLevel.Debug, $"Currently connected readers: {string.Join(", ", readerNames)}");

        var monitorFactory = MonitorFactory.Instance;
        monitor = monitorFactory.Create(SCardScope.System);

        monitor.StatusChanged += MonitorOnStatusChanged;
        monitor.CardInserted += MonitorOnCardInserted;

        if (readerNames.Any()) monitor.Start(readerNames);
    }

    private void MonitorOnStatusChanged(object sender, StatusChangeEventArgs args)
    {
        if (ShowLogs) OnLogMessage?.Invoke(LogLevel.Debug, $"PCSC reader '{args.ReaderName}, new state {args.NewState}");
    }

    private void MonitorOnCardInserted(object sender, CardStatusEventArgs args)
    {
        try
        {
            using var ctx = ContextFactory.Instance.Establish(SCardScope.System);
            using var reader = ctx.ConnectReader(args.ReaderName, SCardShareMode.Shared, SCardProtocol.Any);

            var atr = BitConverter.ToString(args.Atr);

            if (atr != "3B-8F-80-01-80-4F-0C-A0-00-00-03-06-03-00-01-00-00-00-00-6A" && atr != "3B-8B-80-01-00-12-23-3F-53-65-49-44-0F-90-00-A0")
            {
                OnLogMessage?.Invoke(LogLevel.Warning, $"Non mifare nfc! ATR: {atr} ");
                return;
            }

            using (reader.Transaction(SCardReaderDisposition.Leave))
            {
                var start = DateTime.Now;

                var uidData = SendCmd("Get card UID:", reader, [0xff, 0xCA, 0x00, 0x00, 0x00]);

                if (uidData == null)
                {
                    OnLogMessage?.Invoke(LogLevel.Error, "Can't get UID");
                    return;
                }

                var bambuTagInfo = new BambuFillamentInfo(uidData);

                OnLogMessage?.Invoke(LogLevel.Information, $"NFC with UID: {bambuTagInfo.SerialNumber}");

                #region Generate Keys

                var keys = uidData.GetBambuKeys();

                if (ShowLogs) OnLogMessage?.Invoke(LogLevel.Debug, $"Mifare nfc keys: {string.Join(", ", keys.Select(key => BitConverter.ToString(key).Replace("-", "").ToLower()))}");

                #endregion

                #region Read Blocks

                var tagReadStart = DateTime.Now;

                var blockData = FullTagScanAndUpload ? new byte[64][] : new byte[20][];

                for (var i = 0; i < (FullTagScanAndUpload ? 16 : 5); i++)
                {
                    var blockNum = i * 4;

                    SendCmd("Load Key: ", reader, new byte[] { 0xFF, 0x82, 0x00, 0x00, 0x06 }.Concat(keys[i]).ToArray());
                    SendCmd("Authenticate: ", reader, [0xFF, 0x86, 0x00, 0x00, 0x05, 0x01, 0x00, (byte)blockNum, 0x60, 0x00]);

                    for (var ii = 0; ii < (FullTagScanAndUpload ? 4 : 3); ii++)
                    {
                        blockData[blockNum] = SendCmd("Read Binary: ", reader, [0xFF, 0xB0, 0x00, (byte)blockNum, 0x10]) ?? [16];
                        blockNum++;
                    }
                }

                #endregion

                #region Parse tag data

                bambuTagInfo.ReadTime = (DateTime.Now - tagReadStart).TotalMilliseconds;
                bambuTagInfo.ParseData(blockData, keys, fullRead: FullTagScanAndUpload);

                Debug.WriteLine($"Nfc read time: {bambuTagInfo.ReadTime:0.###}ms");

                #endregion

                if (WriteJsonFiles)
                {
                    if (!Directory.Exists("bambu_nfc_jsons")) Directory.CreateDirectory("bambu_nfc_jsons");
                    File.WriteAllText(Path.Combine("bambu_nfc_jsons", $"{DateTime.Now:yyyy-MM-dd_HHmmss}_{bambuTagInfo.TrayUid}.json"), JsonConvert.SerializeObject(bambuTagInfo, Formatting.Indented));
                }

                OnSpoolFound?.Invoke(bambuTagInfo);

                if (ShowLogs) OnLogMessage?.Invoke(LogLevel.Debug, $"Time taken: {(DateTime.Now - start).TotalMilliseconds:0.###}ms");
                else Debug.WriteLine($"Time taken: {(DateTime.Now - start).TotalMilliseconds:0.###}ms");
            }
        }
        catch (UnresponsiveCardException)
        {
            //ignore
        }
        catch (RemovedCardException)
        {
            //ignore
        }
        catch (ReaderUnavailableException)
        {
            //ignore
        }
        catch (Exception e)
        {
            OnLogMessage?.Invoke(LogLevel.Error, "Error getting pcsc card information: " + e);
        }
    }

    private byte[]? SendCmd(string info, ICardReader reader, byte[] command)
    {
        if (ShowApduCommands) Console.WriteLine($"{info} cmd: {BitConverter.ToString(command).Replace("-", " ")}");

        var receiveBuffer = new byte[256];

        var bytesReceived = reader.Transmit(SCardPCI.GetPci(reader.Protocol), command, command.Length, new SCardPCI(), receiveBuffer, receiveBuffer.Length);

        var responseApdu = new ResponseApdu(receiveBuffer, bytesReceived, IsoCase.Case2Short, reader.Protocol);

        var data = responseApdu.HasData ? responseApdu.GetData() : null;

        if (ShowApduCommands) OnLogMessage?.Invoke(LogLevel.Debug, $"{info} res: {responseApdu.SW1:X2}{responseApdu.SW2:X2}{(responseApdu.HasData ? $", Data: {BitConverter.ToString(data ?? []).Replace("-", "")}" : "")}");

        return data;
    }

}