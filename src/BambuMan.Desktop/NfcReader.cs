﻿// ReSharper disable LocalizableElement

using PCSC.Monitoring;
using PCSC;
using PCSC.Exceptions;
using PCSC.Iso7816;
using System.Security.Cryptography;
using Newtonsoft.Json;
using BambuMan.Shared;
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

            if (atr == "3B-8F-80-01-80-4F-0C-A0-00-00-03-06-03-00-01-00-00-00-00-6A" || atr == "3B-8B-80-01-00-12-23-3F-53-65-49-44-0F-90-00-A0")
            {
                using (reader.Transaction(SCardReaderDisposition.Leave))
                {
                    var start = DateTime.Now;

                    var uidData = SendCmd("Get card UID:", reader, [0xff, 0xCA, 0x00, 0x00, 0x00]);

                    if (uidData != null)
                    {
                        var fillamentInfo = new BambuFillamentInfo(uidData);

                        OnLogMessage?.Invoke(LogLevel.Information, $"NFC with UID: {fillamentInfo.SerialNumber}");

                        #region Generate Keys

                        var master = new byte[] { 0x9a, 0x75, 0x9c, 0xf2, 0xc4, 0xf7, 0xca, 0xff, 0x22, 0x2c, 0xb9, 0x76, 0x9b, 0x41, 0xbc, 0x96 };
                        var context = "RFID-A\0"u8.ToArray();

                        var primary = HKDF.Extract(HashAlgorithmName.SHA256, uidData, salt: master);
                        var dest = HKDF.Expand(HashAlgorithmName.SHA256, primary, 6 * 16, context);

                        var keys = Enumerable.Range(0, 16).Select(x => dest[new Range(x * 6, x * 6 + 6)]).ToArray();

                        if (ShowLogs) OnLogMessage?.Invoke(LogLevel.Debug, $"Mifare nfc keys: {string.Join(", ", keys.Select(key => BitConverter.ToString(key).Replace("-", "").ToLower()))}");

                        #endregion

                        #region Read Blocks

                        var blockData = new byte[20][];

                        var blockNum = 0;

                        for (var i = 0; i < 5; i++)
                        {
                            SendCmd("Load Key: ", reader, new byte[] { 0xFF, 0x82, 0x00, 0x00, 0x06 }.Concat(keys[i]).ToArray());
                            SendCmd("Authenticate: ", reader, [0xFF, 0x86, 0x00, 0x00, 0x05, 0x01, 0x00, (byte)blockNum, 0x60, 0x00]);

                            for (var ii = 0; ii < 3; ii++)
                            {
                                blockData[blockNum] = SendCmd("Read Binary: ", reader, [0xFF, 0xB0, 0x00, (byte)blockNum, 0x10]) ?? [16];
                                blockNum++;
                            }

                            blockNum++;
                        }

                        #endregion

                        #region Parse tag data

                        fillamentInfo.ParseData(blockData);

                        #endregion

                        if (WriteJsonFiles)
                        {
                            if (!Directory.Exists("bambu_nfc_jsons")) Directory.CreateDirectory("bambu_nfc_jsons");
                            File.WriteAllText(Path.Combine("bambu_nfc_jsons", $"{DateTime.Now:yyyy-MM-dd_HHmmss}_{fillamentInfo.TrayUid}.json"), JsonConvert.SerializeObject(fillamentInfo, Formatting.Indented));
                        }

                        OnSpoolFound?.Invoke(fillamentInfo);
                    }
                    else
                    {
                        OnLogMessage?.Invoke(LogLevel.Error, "Can't get UID");
                    }

                    if (ShowLogs) OnLogMessage?.Invoke(LogLevel.Debug, $"Time taken: {(DateTime.Now - start).TotalMilliseconds:0.###}ms");
                }
            }
            else
            {
                OnLogMessage?.Invoke(LogLevel.Warning, $"Non mifare nfc! ATR: {atr} ");
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