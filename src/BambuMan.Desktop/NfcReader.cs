// ReSharper disable LocalizableElement

using BambuMan.Shared;
using BambuMan.Shared.Enums;
using BambuMan.Shared.Models;
using BambuMan.Shared.Nfc;
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
    public delegate void SpoolFoundEventHandler(ISpoolTag tag);

    public event LogMessageEventHandler? OnLogMessage;
    public event SpoolFoundEventHandler? OnSpoolFound;

    private const int RetryIntervalMs = 10000;

    private ISCardMonitor? monitor;
    private System.Timers.Timer? retryTimer;

    public bool ShowApduCommands { get; set; }

    public bool ShowLogs { get; set; }

    public bool WriteJsonFiles { get; set; }

    public bool FullTagScanAndUpload { get; set; }

    public bool IsRunning => monitor?.Monitoring == true;

    /// <summary>
    /// Connects to the PC/SC subsystem and starts monitoring the connected readers.
    /// Returns false when the Windows smart card service is not running or no reader is present — both are normal on a
    /// machine that has never had a reader plugged in — and schedules a retry so a reader connected later is picked up.
    /// </summary>
    public bool Start()
    {
        DisposeMonitor();

        try
        {
            var contextFactory = ContextFactory.Instance;

            using var context = contextFactory.Establish(SCardScope.System);

            var readerNames = context.GetReaders();

            if (ShowLogs) OnLogMessage?.Invoke(LogLevel.Debug, $"Currently connected readers: {string.Join(", ", readerNames)}");

            if (readerNames.Length == 0)
            {
                RetryLater("No smart card reader connected.");
                return false;
            }

            var monitorFactory = MonitorFactory.Instance;
            monitor = monitorFactory.Create(SCardScope.System);

            monitor.StatusChanged += MonitorOnStatusChanged;
            monitor.CardInserted += MonitorOnCardInserted;
            monitor.MonitorException += MonitorOnMonitorException;

            monitor.Start(readerNames);

            StopRetry();

            OnLogMessage?.Invoke(LogLevel.Success, $"Listening for tags on: {string.Join(", ", readerNames)}");

            return true;
        }
        catch (PCSCException e)
        {
            RetryLater($"Smart card subsystem unavailable ({e.SCardError}): {e.Message}");
            return false;
        }
    }

    public void Stop()
    {
        StopRetry();
        DisposeMonitor();
    }

    private void DisposeMonitor()
    {
        if (monitor == null) return;

        monitor.StatusChanged -= MonitorOnStatusChanged;
        monitor.CardInserted -= MonitorOnCardInserted;
        monitor.MonitorException -= MonitorOnMonitorException;

        monitor.Cancel();
        monitor.Dispose();
        monitor = null;
    }

    private void RetryLater(string reason)
    {
        OnLogMessage?.Invoke(LogLevel.Warning, $"{reason} Retrying every {RetryIntervalMs / 1000}s.");

        if (retryTimer != null) return;

        retryTimer = new System.Timers.Timer(RetryIntervalMs) { AutoReset = true };

        retryTimer.Elapsed += (_, _) =>
        {
            // Runs on a timer thread — nothing above it can catch, so it has to stay contained here.
            try
            {
                Start();
            }
            catch (Exception e)
            {
                OnLogMessage?.Invoke(LogLevel.Error, "Error restarting the pcsc reader: " + e);
            }
        };

        retryTimer.Start();
    }

    private void StopRetry()
    {
        if (retryTimer == null) return;

        retryTimer.Stop();
        retryTimer.Dispose();
        retryTimer = null;
    }

    private void MonitorOnStatusChanged(object sender, StatusChangeEventArgs args)
    {
        if (ShowLogs) OnLogMessage?.Invoke(LogLevel.Debug, $"PCSC reader '{args.ReaderName}, new state {args.NewState}");
    }

    private void MonitorOnMonitorException(object sender, PCSCException exception)
    {
        // The monitor thread is dead once this fires (reader unplugged, service stopped) — reconnect instead of going silent.
        RetryLater($"Reader monitoring stopped ({exception.SCardError}): {exception.Message}");
    }

    private void MonitorOnCardInserted(object sender, CardStatusEventArgs args)
    {
        try
        {
            using var ctx = ContextFactory.Instance.Establish(SCardScope.System);
            using var reader = ctx.ConnectReader(args.ReaderName, SCardShareMode.Shared, SCardProtocol.Any);

            var atr = BitConverter.ToString(args.Atr);

            if (IsUltralight(args.Atr))
            {
                ReadNtag(reader);
                return;
            }

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

                var bambuTagInfo = new BambuFilamentInfo(uidData);

                OnLogMessage?.Invoke(LogLevel.Information, $"NFC with UID: {bambuTagInfo.SerialNumber}");

                #region Generate Keys

                var aKeys = uidData.GetBambuAKeys();
                var bKeys = uidData.GetBambuBKeys();
                var keys = aKeys.Concat(bKeys).ToArray();

                if (ShowLogs) OnLogMessage?.Invoke(LogLevel.Debug, $"Mifare nfc keys: {string.Join(", ", aKeys.Select(key => BitConverter.ToString(key).Replace("-", "").ToLower()))}");

                #endregion

                #region Read Blocks

                var tagReadStart = DateTime.Now;

                var blockData = FullTagScanAndUpload ? new byte[64][] : new byte[20][];

                for (var i = 0; i < (FullTagScanAndUpload ? 16 : 5); i++)
                {
                    var blockNum = i * 4;

                    SendCmd("Load Key: ", reader, new byte[] { 0xFF, 0x82, 0x00, 0x00, 0x06 }.Concat(aKeys[i]).ToArray());
                    SendCmd("Authenticate: ", reader, [0xFF, 0x86, 0x00, 0x00, 0x05, 0x01, 0x00, (byte)blockNum, 0x60, 0x00]);

                    for (var ii = 0; ii < (FullTagScanAndUpload ? 4 : 3); ii++)
                    {
                        blockData[blockNum] = SendCmd("Read Binary: ", reader, [0xFF, 0xB0, 0x00, (byte)blockNum, 0x10]) ?? [16];
                        blockNum++;
                    }
                }

                #endregion

                #region Fill in keys

                if (FullTagScanAndUpload)
                {
                    var index = 0;

                    for (var i = 3; i < blockData.Length; i += 4)
                    {
                        blockData[i] = aKeys[index].Concat(blockData[i][6..10]).Concat(bKeys[index]).ToArray();
                        index++;
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

    #region NTAG (OpenSpool, TigerTag)

    /// <summary>NTAG pages 0x04 and up; an NTAG216 has 222 user pages, read 4 at a time.</summary>
    private const int MaxNtagUserBytes = 888;

    /// <summary>
    /// A PC/SC part 3 ATR naming a MIFARE Ultralight family card (card name bytes 00 03), which is how readers such as
    /// the ACR122U report NTAG21x.
    /// </summary>
    private static bool IsUltralight(byte[] atr) => atr is [0x3B, _, 0x80, 0x01, 0x80, 0x4F, ..] && atr.Length >= 15 && atr[13] == 0x00 && atr[14] == 0x03;

    /// <summary>
    /// Read an NTAG's user memory and hand a recognised spool tag (OpenSpool, TigerTag) to <see cref="OnSpoolFound"/>.
    /// Reads 16 bytes (4 pages) per command until the NDEF message, or TigerTag's 80 bytes, are covered.
    /// </summary>
    private void ReadNtag(ICardReader reader)
    {
        using (reader.Transaction(SCardReaderDisposition.Leave))
        {
            var uidData = SendCmd("Get card UID:", reader, [0xff, 0xCA, 0x00, 0x00, 0x00]);

            if (uidData == null)
            {
                OnLogMessage?.Invoke(LogLevel.Error, "Can't get UID");
                return;
            }

            var memory = new List<byte>();

            while (memory.Count < MaxNtagUserBytes)
            {
                var page = TigerTag.FirstPage + memory.Count / 4;
                var data = SendCmd("Read Binary: ", reader, [0xFF, 0xB0, 0x00, (byte)page, 0x10]);

                // Past the last page the tag answers with an error and no data.
                if (data is not { Length: 16 }) break;

                memory.AddRange(data);

                var bytes = memory.ToArray();

                if (TigerTag.IsTigerTag(bytes) && bytes.Length >= TigerTag.DataLength) break;
                if (NdefTlv.FindMessage(bytes) is { } message && message.Offset + message.Length <= bytes.Length) break;
            }

            var userMemory = memory.ToArray();
            var tagInfo = new TagInfo(uidData) { Records = NdefTlv.ParseRecords(userMemory), UserMemory = userMemory };

            if (SpoolTagReader.TryRead(tagInfo) is { } spoolTag)
            {
                OnLogMessage?.Invoke(LogLevel.Information, $"{spoolTag.Format.DisplayName()} tag with UID: {spoolTag.SerialNumber}");
                OnSpoolFound?.Invoke(spoolTag);
            }
            else OnLogMessage?.Invoke(LogLevel.Warning, $"NTAG {tagInfo.SerialNumber} holds no OpenSpool or TigerTag data");
        }
    }

    #endregion

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