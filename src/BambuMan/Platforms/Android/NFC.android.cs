using Android;
using Android.App;
using Android.Content;
using Android.Nfc;
using Android.Nfc.Tech;
using BambuMan.Shared;
using BambuMan.Shared.Nfc;
using Newtonsoft.Json;
using System.Security.Cryptography;
using System.Text;
using Activity = Android.App.Activity;
using Debug = System.Diagnostics.Debug;

#pragma warning disable CS0067

namespace BambuMan
{
    /// <summary>
    /// Android implementation of <see cref="INfc"/>
    /// </summary>
    public class NfcImplementation : INfc
    {
        public event EventHandler? OnTagConnected;
        public event EventHandler? OnTagDisconnected;
        public event NdefMessageReceivedEventHandler? OnMessageReceived;
        public event NdefMessagePublishedEventHandler? OnMessagePublished;
        public event TagDiscoveredEventHandler? OnTagDiscovered;
        public event EventHandler? OnIOsReadingSessionCancelled;
        public event EventHandler? OnTagIntentReceived;
        public event TagListeningStatusChangedEventHandler? OnTagListeningStatusChanged;

        private readonly NfcAdapter? nfcAdapter;

        private bool isListening;
        private bool isWriting;
        private bool isFormatting;
        private Tag? currentTag;

        /// <summary>
        /// Current Android <see cref="Context"/>
        /// </summary>
        Context CurrentContext => CrossNfc.AppContext;

        /// <summary>
        /// Current Android <see cref="Activity"/>
        /// </summary>
        Activity? CurrentActivity => CrossNfc.GetCurrentActivity(true);

        public bool FullTagScanAndUpload { get; set; }

        /// <summary>
        /// Checks if NFC Feature is available
        /// </summary>
        public bool IsAvailable
        {
            get
            {
                if (CurrentContext.CheckCallingOrSelfPermission(Manifest.Permission.Nfc) != Android.Content.PM.Permission.Granted) return false;
                return nfcAdapter != null;
            }
        }

        /// <summary>
        /// Checks if NFC Feature is enabled
        /// </summary>
        public bool IsEnabled => IsAvailable && (nfcAdapter?.IsEnabled ?? false);

        /// <summary>
        /// Checks if writing mode is supported
        /// </summary>
        public bool IsWritingTagSupported => NfcUtils.IsWritingSupported();

        /// <summary>
        /// NFC configuration
        /// </summary>
        public NfcConfiguration Configuration { get; } = NfcConfiguration.GetDefaultConfiguration();

        /// <summary>
        /// Default constructor
        /// </summary>
        public NfcImplementation()
        {
            nfcAdapter = NfcAdapter.GetDefaultAdapter(CurrentContext);
        }

        /// <summary>
        /// Update NFC configuration
        /// </summary>
        /// <param name="configuration"><see cref="NfcConfiguration"/></param>
        public void SetConfiguration(NfcConfiguration configuration) => Configuration.Update(configuration);

        /// <summary>
        /// Starts tags detection
        /// </summary>
        public void StartListening()
        {
            if (nfcAdapter == null) return;

            if (CurrentActivity == null) return;

            var intent = new Intent(CurrentActivity, CurrentActivity.GetType()).AddFlags(ActivityFlags.SingleTop);

            // We don't use MonoAndroid12.0 as target framework for easier backward compatibility:
            // MonoAndroid12.0 needs JDK 11.
            PendingIntentFlags pendingIntentFlags = 0;

#if NET6_0_OR_GREATER
            if (OperatingSystem.IsAndroidVersionAtLeast(31)) pendingIntentFlags = PendingIntentFlags.Mutable;
#else
            if (OperatingSystem.IsAndroidVersionAtLeast(31))
				pendingIntentFlags = (PendingIntentFlags)33554432;
#endif

            var pendingIntent = PendingIntent.GetActivity(CurrentActivity, 0, intent, pendingIntentFlags);

            var ndefFilter = new IntentFilter(NfcAdapter.ActionNdefDiscovered);
            ndefFilter.AddDataType("*/*");

            var tagFilter = new IntentFilter(NfcAdapter.ActionTagDiscovered);
            tagFilter.AddCategory(Intent.CategoryDefault);

            var filters = new[] { ndefFilter, tagFilter };

            nfcAdapter.EnableForegroundDispatch(CurrentActivity, pendingIntent, filters, null);

            isListening = true;
            OnTagListeningStatusChanged?.Invoke(isListening);
        }

        /// <summary>
        /// Stops tags detection
        /// </summary>
        public void StopListening()
        {
            DisablePublishing();
            if (nfcAdapter != null)
                nfcAdapter.DisableForegroundDispatch(CurrentActivity);

            isListening = false;
            OnTagListeningStatusChanged?.Invoke(isListening);
        }

        /// <summary>
        /// Starts tag publishing (writing or formatting)
        /// </summary>
        /// <param name="clearMessage">Format tag</param>
        public void StartPublishing(bool clearMessage = false)
        {
            if (!IsWritingTagSupported)
                return;

            isWriting = true;
            isFormatting = clearMessage;
        }

        /// <summary>
        /// Stops tag publishing
        /// </summary>
        public void StopPublishing() => DisablePublishing();

        /// <summary>
        /// Publish or write a message on a tag
        /// </summary>
        /// <param name="tagInfo">see <see cref="ITagInfo"/></param>
        /// <param name="makeReadOnly">make tag read-only</param>
        public void PublishMessage(ITagInfo tagInfo, bool makeReadOnly = false) => WriteOrClearMessage(tagInfo, false, makeReadOnly);

        /// <summary>
        /// Format tag
        /// </summary>
        /// <param name="tagInfo">see <see cref="ITagInfo"/></param>
        public void ClearMessage(ITagInfo tagInfo) => WriteOrClearMessage(tagInfo, true);

        /// <summary>
        /// Write or Clear a NDEF message
        /// </summary>
        /// <param name="tagInfo"><see cref="ITagInfo"/></param>
        /// <param name="clearMessage">Clear Message</param>
        /// <param name="makeReadOnly">Make tag read-only</param>
        internal void WriteOrClearMessage(ITagInfo tagInfo, bool clearMessage = false, bool makeReadOnly = false)
        {
            try
            {
                if (currentTag == null)
                    throw new Exception(Configuration.Messages.NFCErrorMissingTag);

                if (tagInfo == null)
                    throw new Exception(Configuration.Messages.NFCErrorMissingTagInfo);

                var ndef = Ndef.Get(currentTag);
                if (ndef != null)
                {
                    try
                    {
                        if (!ndef.IsWritable)
                            throw new Exception(Configuration.Messages.NFCErrorReadOnlyTag);

                        if (ndef.MaxSize < NfcUtils.GetSize(tagInfo.Records))
                            throw new Exception(Configuration.Messages.NFCErrorCapacityTag);

                        ndef.Connect();
                        OnTagConnected?.Invoke(null, EventArgs.Empty);

                        NdefMessage? message = null;

                        if (clearMessage)
                        {
                            message = GetEmptyNdefMessage();
                        }
                        else
                        {
                            var records = new List<NdefRecord>();

                            if (tagInfo.Records != null)
                            {
                                foreach (var record in tagInfo.Records)
                                {
                                    if (record == null) continue;
                                    if (GetAndroidNdefRecord(record) is { } ndefRecord) records.Add(ndefRecord);
                                }
                            }


                            if (records.Any())
                                message = new NdefMessage(records.ToArray());
                        }

                        if (message != null)
                        {
                            ndef.WriteNdefMessage(message);

                            if (!clearMessage && makeReadOnly)
                            {
                                if (!MakeReadOnly(ndef))
                                    Console.WriteLine("Cannot lock tag");
                            }

                            var nTag = GetTagInfo(currentTag, ndef.NdefMessage);
                            if (nTag != null) OnMessagePublished?.Invoke(nTag);
                        }
                        else
                            throw new Exception(Configuration.Messages.NFCErrorWrite);
                    }
                    catch (TagLostException tlex)
                    {
                        throw new Exception("Tag Lost Error: " + tlex.Message);
                    }
                    catch (Java.IO.IOException ioex)
                    {
                        throw new Exception("Tag IO Error: " + ioex.Message);
                    }
                    catch (Android.Nfc.FormatException fe)
                    {
                        throw new Exception("Tag Format Error: " + fe.Message);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception("Tag Error:" + ex.Message);
                    }
                    finally
                    {
                        if (ndef.IsConnected)
                            ndef.Close();

                        currentTag = null;
                        OnTagDisconnected?.Invoke(null, EventArgs.Empty);
                    }
                }
                else throw new Exception(Configuration.Messages.NFCErrorNotCompliantTag);
            }
            catch (Exception ex)
            {
                StopPublishingAndThrowError(ex.Message);
            }
        }

        /// <summary>
        /// Handle Android OnNewIntent
        /// </summary>
        /// <param name="intent">Android <see cref="Intent"/></param>
        internal void HandleNewIntent(Intent? intent)
        {
            if (intent == null) return;

            if (intent.Action == NfcAdapter.ActionTagDiscovered || intent.Action == NfcAdapter.ActionNdefDiscovered)
            {
                if (OperatingSystem.IsAndroidVersionAtLeast(33))
                    currentTag = (Tag?)intent.GetParcelableExtra(NfcAdapter.ExtraTag, Java.Lang.Class.FromType(typeof(Tag)));
                else
                    currentTag = (Tag?)intent.GetParcelableExtra(NfcAdapter.ExtraTag);

                if (currentTag != null)
                {
                    OnTagIntentReceived?.Invoke(this, EventArgs.Empty);

                    var nTag = GetTagInfo(currentTag);
                    if (nTag == null) return;

                    if (isWriting)
                    {
                        // Write mode
                        OnTagDiscovered?.Invoke(nTag, isFormatting);
                    }
                    else
                    {
                        // Read mode
                        OnMessageReceived?.Invoke(nTag);
                    }
                }
            }
        }

        /// <summary>
        /// Handle Android OnResume
        /// </summary>
        internal void HandleOnResume()
        {
            // Android 10 fix:
            // If listening mode is already enable, we restart listening when activity is resumed
            if (isListening)
                StartListening();
        }

        #region Private

        /// <summary>
        /// Stops publishing and throws error
        /// </summary>
        /// <param name="message">message</param>
        void StopPublishingAndThrowError(string message)
        {
            StopPublishing();
            throw new Exception(message);
        }

        /// <summary>
        /// Deactivate publishing
        /// </summary>
        void DisablePublishing()
        {
            isWriting = false;
            isFormatting = false;
        }

        /// <summary>
        /// Transforms an array of <see cref="NdefRecord"/> into an array of <see cref="NfcNdefRecord"/>
        /// </summary>
        /// <param name="records">Array of <see cref="NdefRecord"/></param>
        /// <returns>Array of <see cref="NfcNdefRecord"/></returns>
        NfcNdefRecord[] GetRecords(NdefRecord?[]? records)
        {
            if (records == null) return [];

            var results = new NfcNdefRecord[records.Length];

            for (var i = 0; i < records.Length; i++)
            {
                if (records[i] == null) continue;

                var ndefRecord = new NfcNdefRecord
                {
                    TypeFormat = (NfcNdefTypeFormat)records[i]!.Tnf,
                    Uri = records[i]!.ToUri()?.ToString(),
                    MimeType = records[i]!.ToMimeType() ?? "text/plain",
                    Payload = records[i]!.GetPayload()
                };

                results.SetValue(ndefRecord, i);
            }
            return results;
        }

        /// <summary>
        /// Returns information contains in NFC Tag
        /// </summary>
        /// <param name="tag">Android <see cref="Tag"/></param>
        /// <param name="ndefMessage">Android <see cref="NdefMessage"/></param>
        /// <returns><see cref="ITagInfo"/></returns>
        ITagInfo? GetTagInfo(Tag? tag, NdefMessage? ndefMessage = null)
        {
            if (tag == null) return null;

            #region Read Bambu Lab Fillament Info

            var mfc = MifareClassic.Get(tag);

            if (mfc != null)
            {
                var showLogs = true;

                try
                {
                    mfc.Connect();

                    var uidData = tag.GetId();
                    if (uidData == null) return null;

                    var uid = BitConverter.ToString(uidData).Replace("-", "");

                    var bambuTagInfo = new BambuFillamentInfo(uidData);

                    Debug.WriteLine($"NFC UID: {uid}");

                    #region Generate Keys

                    var master = new byte[] { 0x9a, 0x75, 0x9c, 0xf2, 0xc4, 0xf7, 0xca, 0xff, 0x22, 0x2c, 0xb9, 0x76, 0x9b, 0x41, 0xbc, 0x96 };
                    var context = "RFID-A\0"u8.ToArray();

                    var primary = HKDF.Extract(HashAlgorithmName.SHA256, uidData, salt: master);
                    var dest = HKDF.Expand(HashAlgorithmName.SHA256, primary, 6 * 16, context);

                    var keys = Enumerable.Range(0, 16).Select(x => dest[new Range(x * 6, x * 6 + 6)]).ToArray();

                    if (showLogs)
                    {
                        foreach (var key in keys)
                        {
                            Debug.WriteLine(BitConverter.ToString(key).Replace("-", "").ToLower());
                        }
                    }

                    #endregion

                    #region Read Blocks

                    var tagReadStart = DateTime.Now;

                    var blockData = FullTagScanAndUpload ? new byte[64][] : new byte[20][];

                    for (var i = 0; i < (FullTagScanAndUpload ? 16 : 5); i++)
                    {
                        var blockNum = i * 4;

                        var authA = mfc.AuthenticateSectorWithKeyA(i, keys[i]);
                        if (!authA) continue;

                        for (var ii = 0; ii < (FullTagScanAndUpload ? 4 : 3); ii++)
                        {
                            try
                            {
                                blockData[blockNum] = mfc.ReadBlock(blockNum) ?? [16];
                            }
                            catch (Exception e)
                            {
                                Debug.WriteLine(e.ToString());
                            }

                            blockNum++;
                        }
                    }

                    bambuTagInfo.ReadTime = (DateTime.Now - tagReadStart).TotalMilliseconds;
                    bambuTagInfo.ParseData(blockData, keys, fullRead: FullTagScanAndUpload);

                    #endregion

                    Debug.WriteLine($"Nfc read time: {bambuTagInfo.ReadTime:0.###}ms");

#if DEBUG
                    var json = JsonConvert.SerializeObject(bambuTagInfo, Formatting.Indented);
                    Debug.WriteLine(json);
#endif

                    return bambuTagInfo;
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e.ToString());
                }
                finally
                {
                    try
                    {
                        if (mfc.IsConnected) mfc.Close();
                    }
                    catch (Exception)
                    {
                        //ignore
                    }
                }
            }

            #endregion

            var ndef = Ndef.Get(tag);
            var nTag = new TagInfo(tag.GetId() ?? [], ndef != null);

            if (ndef != null)
            {
                nTag.Capacity = ndef.MaxSize;
                nTag.IsWritable = ndef.IsWritable;

                ndefMessage ??= ndef.CachedNdefMessage;

                if (ndefMessage != null)
                {
                    var records = ndefMessage.GetRecords();
                    nTag.Records = GetRecords(records);
                }
            }

            return nTag;
        }

        /// <summary>
        /// Transforms a <see cref="NfcNdefRecord"/> into an Android <see cref="NdefRecord"/>
        /// </summary>
        /// <param name="record">Object <see cref="NfcNdefRecord"/></param>
        /// <returns>Android <see cref="NdefRecord"/></returns>
        NdefRecord? GetAndroidNdefRecord(NfcNdefRecord? record)
        {
            if (record == null) return null;

            NdefRecord? ndefRecord = null;
            switch (record.TypeFormat)
            {
                case NfcNdefTypeFormat.WellKnown:
                    var languageCode = record.LanguageCode;
                    if (string.IsNullOrWhiteSpace(languageCode)) languageCode = Configuration.DefaultLanguageCode;
                    if (languageCode != null && record.Payload != null) ndefRecord = NdefRecord.CreateTextRecord(languageCode.Substring(0, 2), Encoding.UTF8.GetString(record.Payload));
                    break;
                case NfcNdefTypeFormat.Mime:
                    ndefRecord = NdefRecord.CreateMime(record.MimeType, record.Payload);
                    break;
                case NfcNdefTypeFormat.Uri:
                    if (record.Payload != null) ndefRecord = NdefRecord.CreateUri(Encoding.UTF8.GetString(record.Payload));
                    break;
                case NfcNdefTypeFormat.External:
                    ndefRecord = NdefRecord.CreateExternal(record.ExternalDomain, record.ExternalType, record.Payload);
                    break;
                case NfcNdefTypeFormat.Empty:
                    ndefRecord = GetEmptyNdefRecord();
                    break;
                case NfcNdefTypeFormat.Unknown:
                case NfcNdefTypeFormat.Unchanged:
                case NfcNdefTypeFormat.Reserved:
                default:
                    break;

            }
            return ndefRecord;
        }

        /// <summary>
        /// Returns an empty Android <see cref="NdefRecord"/>
        /// </summary>
        /// <returns>Android <see cref="NdefRecord"/></returns>
        NdefRecord GetEmptyNdefRecord()
        {
            var empty = Array.Empty<byte>();
            return new NdefRecord(NdefRecord.TnfEmpty, empty, empty, empty);
        }

        /// <summary>
        /// Returns an empty Android <see cref="NdefMessage"/>
        /// </summary>
        /// <returns>Android <see cref="NdefMessage"/></returns>
        NdefMessage GetEmptyNdefMessage()
        {
            var records = new NdefRecord[1];
            records[0] = GetEmptyNdefRecord();
            return new NdefMessage(records);
        }

        /// <summary>
        /// Make a tag read-only
        /// WARNING: This operation is permanent
        /// </summary>
        /// <param name="ndef"><see cref="Ndef"/></param>
        /// <returns>boolean</returns>
        bool MakeReadOnly(Ndef? ndef)
        {
            if (ndef == null) return false;

            var result = false;
            var newConnection = false;

            if (!ndef.IsConnected)
            {
                newConnection = true;
                ndef.Connect();
            }

            if (ndef.CanMakeReadOnly())
                result = ndef.MakeReadOnly();

            if (newConnection && ndef.IsConnected)
                ndef.Close();

            return result;
        }

        #endregion

        #region NFC Status Event Listener

        private NfcBroadcastReceiver? nfcBroadcastReceiver;

        private event OnNfcStatusChangedEventHandler? OnOnNfcStatusChangedInternal;

        public event OnNfcStatusChangedEventHandler? OnNfcStatusChanged
        {
            add
            {
                var wasRunning = OnOnNfcStatusChangedInternal != null;
                OnOnNfcStatusChangedInternal += value;
                if (!wasRunning && OnOnNfcStatusChangedInternal != null) RegisterListener();
            }
            remove
            {
                var wasRunning = OnOnNfcStatusChangedInternal != null;
                OnOnNfcStatusChangedInternal -= value;
                if (wasRunning && OnOnNfcStatusChangedInternal == null) UnRegisterListener();
            }
        }

        /// <summary>
        /// Register NFC Broadcast Receiver
        /// </summary>
        void RegisterListener()
        {
            nfcBroadcastReceiver = new NfcBroadcastReceiver(OnNfcStatusChange);
            CurrentContext.RegisterReceiver(nfcBroadcastReceiver, new IntentFilter(NfcAdapter.ActionAdapterStateChanged));
        }

        /// <summary>
        /// Unregister NFC Broadcast Receiver
        /// </summary>
        void UnRegisterListener()
        {
            if (nfcBroadcastReceiver == null)
                return;

            try
            {
                CurrentContext.UnregisterReceiver(nfcBroadcastReceiver);
            }
            catch (Java.Lang.IllegalArgumentException ex)
            {
                throw new Exception("NFC Broadcast Receiver Error: " + ex.Message);
            }

            nfcBroadcastReceiver.Dispose();
            nfcBroadcastReceiver = null;
        }

        /// <summary>
        /// Called when NFC status has changed
        /// </summary>
        void OnNfcStatusChange() => OnOnNfcStatusChangedInternal?.Invoke(IsEnabled);

        /// <summary>
        /// Broadcast Receiver to check NFC feature availability
        /// </summary>
        [BroadcastReceiver(Enabled = true, Exported = false, Label = "NFC Status Broadcast Receiver")]
        private class NfcBroadcastReceiver : BroadcastReceiver
        {
            private readonly Action? onChanged;

            // ReSharper disable once UnusedMember.Local
            public NfcBroadcastReceiver() { }

            /// <summary>
            /// Broadcast Receiver to check NFC feature availability
            /// </summary>
            public NfcBroadcastReceiver(Action onChanged)
            {
                this.onChanged = onChanged;
            }

            // ReSharper disable once AsyncVoidMethod
            public override async void OnReceive(Context? context, Intent? intent)
            {
                if (intent?.Action != NfcAdapter.ActionAdapterStateChanged) return;

                var state = intent.GetIntExtra(NfcAdapter.ExtraAdapterState, 0);

                if (state != NfcAdapter.StateOff && state != NfcAdapter.StateOn) return;

                // await 1500ms to ensure that the status updates
                await Task.Delay(1500);
                onChanged?.Invoke();
            }
        }

        #endregion

    }
}
