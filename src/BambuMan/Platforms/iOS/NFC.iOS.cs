using BambuMan.Shared.Nfc;
using CoreFoundation;
using CoreNFC;
using Foundation;
using System.Text;
using UIKit;
// ReSharper disable ConvertTypeCheckPatternToNullCheck
// ReSharper disable ParameterHidesMember
// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
// ReSharper disable InconsistentNaming
// ReSharper disable VariableHidesOuterVariable
#pragma warning disable CS8604 // Possible null reference argument.
#pragma warning disable CS8602 // Dereference of a possibly null reference.
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning disable CA1422
#pragma warning disable CS0067

namespace BambuMan
{
    /// <summary>
    /// iOS 13+ implementation of <see cref="INfc"/>
    /// </summary>
    public class NfcImplementation : NFCTagReaderSessionDelegate, INfc
    {
        public const string SessionTimeoutMessage = "session timeout";

        public event EventHandler? OnTagConnected;
        public event EventHandler? OnTagDisconnected;
        public event NdefMessageReceivedEventHandler? OnMessageReceived;
        public event NdefMessagePublishedEventHandler? OnMessagePublished;
        public event TagDiscoveredEventHandler? OnTagDiscovered;
        public event EventHandler? OnIOsReadingSessionCancelled;
        public event OnNfcStatusChangedEventHandler? OnNfcStatusChanged;
        public event TagListeningStatusChangedEventHandler? OnTagListeningStatusChanged;
        public event EventHandler? OnTagIntentReceived;

        private bool isWriting;
        private bool isFormatting;
        private bool customInvalidation;

        private INFCTag? tag;

        NFCTagReaderSession? NfcSession { get; set; }

        public bool FullTagScanAndUpload { get; set; }

        /// <summary>
        /// Checks if NFC Feature is available
        /// </summary>
        public bool IsAvailable => NFCReaderSession.ReadingAvailable;

        /// <summary>
        /// Checks if NFC Feature is enabled
        /// </summary>
        public bool IsEnabled => IsAvailable;

        /// <summary>
        /// Checks if writing mode is supported
        /// </summary>
        public bool IsWritingTagSupported => true;

        /// <summary>
        /// NFC configuration
        /// </summary>
        public NfcConfiguration Configuration { get; } = NfcConfiguration.GetDefaultConfiguration();

        /// <summary>
        /// Default constructor
        /// </summary>
        public NfcImplementation()
        {

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
            customInvalidation = false;
            isWriting = false;
            isFormatting = false;

            NfcSession = new NFCTagReaderSession(NFCPollingOption.Iso14443 | NFCPollingOption.Iso15693, this, DispatchQueue.CurrentQueue)
            {
                AlertMessage = Configuration.Messages.NFCDialogAlertMessage
            };
            NfcSession?.BeginSession();
            OnTagListeningStatusChanged?.Invoke(true);
        }

        /// <summary>
        /// Stops tags detection
        /// </summary>
        public void StopListening()
        {
            NfcSession?.InvalidateSession();
        }

        /// <summary>
        /// Starts tag publishing (writing or formatting)
        /// </summary>
        /// <param name="clearMessage">Format tag</param>
		public void StartPublishing(bool clearMessage = false)
        {
            if (!IsAvailable)
                throw new InvalidOperationException(Configuration.Messages.NFCWritingNotSupported);

            customInvalidation = false;
            isWriting = true;
            isFormatting = clearMessage;

            NfcSession = new NFCTagReaderSession(NFCPollingOption.Iso14443 | NFCPollingOption.Iso15693, this, DispatchQueue.CurrentQueue)
            {
                AlertMessage = Configuration.Messages.NFCDialogAlertMessage
            };
            NfcSession?.BeginSession();
            OnTagListeningStatusChanged?.Invoke(true);
        }

        /// <summary>
        /// Stops tag publishing
        /// </summary>
        public void StopPublishing()
        {
            isWriting = isFormatting = customInvalidation = false;
            tag = null;
            NfcSession?.InvalidateSession();
        }

        /// <summary>
        /// Publish or write a message on a tag
        /// </summary>
        /// <param name="tagInfo">see <see cref="ITagInfo"/></param>
        /// <param name="makeReadOnly">make tag read-only</param>
        public void PublishMessage(ITagInfo tagInfo, bool makeReadOnly = false) => WriteOrClearMessage(tag, tagInfo, false, makeReadOnly);

        /// <summary>
        /// Format tag
        /// </summary>
        /// <param name="tagInfo">see <see cref="ITagInfo"/></param>
        public void ClearMessage(ITagInfo tagInfo) => WriteOrClearMessage(tag, tagInfo, true);

        /// <summary>
        /// Event raised when NFC tags are detected
        /// </summary>
        /// <param name="session">iOS <see cref="NFCTagReaderSession"/></param>
        /// <param name="tags">Array of iOS <see cref="INFCTag"/></param>
        public override void DidDetectTags(NFCTagReaderSession session, INFCTag[] tags)
        {
            customInvalidation = false;
            tag = tags.First();

            string connectionError;

            session.ConnectTo(tag, (error) =>
            {
                if (error != null)
                {
                    connectionError = error.LocalizedDescription;
                    Invalidate(session, connectionError);
                    return;
                }

                var ndefTag = NfcNdefTagExtensions.GetNdefTag(tag);

                if (ndefTag == null)
                {
                    Invalidate(session, Configuration.Messages.NFCErrorNotCompliantTag);
                    return;
                }

                ndefTag.QueryNdefStatus((status, capacity, nsError) =>
                {
                    if (nsError != null)
                    {
                        Invalidate(session, nsError.LocalizedDescription);
                        return;
                    }

                    var isNdefSupported = status != NFCNdefStatus.NotSupported;

                    var identifier = NfcNdefTagExtensions.GetTagIdentifier(ndefTag);
                    var nTag = new TagInfo(identifier, isNdefSupported)
                    {
                        IsWritable = status == NFCNdefStatus.ReadWrite,
                        Capacity = Convert.ToInt32(capacity)
                    };

                    if (!isNdefSupported)
                    {
                        session.AlertMessage = Configuration.Messages.NFCErrorNotSupportedTag;

                        OnMessageReceived?.Invoke(nTag);
                        Invalidate(session);
                        return;
                    }

                    if (isWriting)
                    {
                        // Write mode
                        OnTagDiscovered?.Invoke(nTag, isFormatting);
                    }
                    else
                    {
                        // Read mode
                        ndefTag.ReadNdef((message, nsError) =>
                        {
                            // iOS Error: NFCReaderError.NdefReaderSessionErrorZeroLengthMessage (NDEF tag does not contain any NDEF message)
                            // NFCReaderError.NdefReaderSessionErrorZeroLengthMessage constant should be equals to 403 instead of 304
                            // see https://developer.apple.com/documentation/corenfc/nfcreadererror/code/ndefreadersessionerrorzerolengthmessage
                            if (nsError.Code != 403)
                            {
                                Invalidate(session, Configuration.Messages.NFCErrorRead);
                                return;
                            }

                            session.AlertMessage = Configuration.Messages.NFCSuccessRead;

                            nTag.Records = NFCNdefPayloadExtensions.GetRecords(message.Records);
                            OnMessageReceived?.Invoke(nTag);
                            Invalidate(session);
                        });
                    }
                });
            });
        }

        /// <summary>
        /// Event raised when an error happened during detection
        /// </summary>
        /// <param name="session">iOS <see cref="NFCTagReaderSession"/></param>
        /// <param name="error">iOS <see cref="NSError"/></param>
        public override void DidInvalidate(NFCTagReaderSession session, NSError error)
        {
            OnTagListeningStatusChanged?.Invoke(false);

            var readerError = (NFCReaderError)error.Code;

            if (readerError != NFCReaderError.ReaderSessionInvalidationErrorFirstNDEFTagRead && readerError != NFCReaderError.ReaderSessionInvalidationErrorUserCanceled)
            {
                var alertController = UIAlertController.Create(Configuration.Messages.NFCSessionInvalidated, error.LocalizedDescription.ToLower().Equals(SessionTimeoutMessage) ? Configuration.Messages.NFCSessionTimeout : error.LocalizedDescription, UIAlertControllerStyle.Alert);
                alertController.AddAction(UIAlertAction.Create(Configuration.Messages.NFCSessionInvalidatedButton, UIAlertActionStyle.Default, null));

                OnIOsReadingSessionCancelled.Invoke(null, EventArgs.Empty);

                DispatchQueue.MainQueue.DispatchAsync(() =>
                {
                    GetCurrentController().PresentViewController(alertController, true, null);
                });
            }
            else if (readerError == NFCReaderError.ReaderSessionInvalidationErrorUserCanceled && !customInvalidation)
                OnIOsReadingSessionCancelled?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>
        /// Write or Clear a NDEF message
        /// </summary>
        /// <param name="tagIn"><see cref="INFCTag"/></param>
        /// <param name="tagInfo"><see cref="ITagInfo"/></param>
        /// <param name="clearMessage">Clear Message</param>
        /// <param name="makeReadOnly">Make a tag read-only</param>
        internal void WriteOrClearMessage(INFCTag? tagIn, ITagInfo? tagInfo, bool clearMessage = false, bool makeReadOnly = false)
        {
            if (NfcSession == null) return;

            if (tagIn == null)
            {
                Invalidate(NfcSession, Configuration.Messages.NFCErrorMissingTag);
                return;
            }

            if (tagInfo == null || (!clearMessage && tagInfo.Records != null && tagInfo.Records.Any(record => record is { Payload: null })))
            {
                Invalidate(NfcSession, Configuration.Messages.NFCErrorMissingTagInfo);
                return;
            }

            var ndefTag = NfcNdefTagExtensions.GetNdefTag(tagIn);
            if (ndefTag == null)
            {
                Invalidate(NfcSession, Configuration.Messages.NFCErrorNotCompliantTag);
                return;
            }

            try
            {
                if (!ndefTag.Available)
                {
                    NfcSession.ConnectTo(tagIn, (error) =>
                    {
                        if (error != null)
                        {
                            Invalidate(NfcSession, error.LocalizedDescription);
                            return;
                        }

                        ExecuteWriteOrClear(NfcSession, ndefTag, tagInfo, clearMessage);
                    });
                }
                else
                {
                    ExecuteWriteOrClear(NfcSession, ndefTag, tagInfo, clearMessage);
                }

                if (!clearMessage && makeReadOnly)
                {
                    MakeTagReadOnly(NfcSession, tagIn, ndefTag);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.StackTrace);
            }
            finally
            {
                OnTagDisconnected?.Invoke(null, EventArgs.Empty);
            }
        }

        #region Private

        /// <summary>
        /// Returns the current iOS controller
        /// </summary>
        /// <returns>Object <see cref="UIViewController"/></returns>
        UIViewController GetCurrentController()
        {
            var window = UIApplication.SharedApplication.KeyWindow;

            var vc = window.RootViewController;

            while (vc.PresentedViewController != null) vc = vc.PresentedViewController;

            return vc;
        }

        /// <summary>
        /// Invalidate the session
        /// </summary>
        /// <param name="session"><see cref="NFCTagReaderSession"/></param>
        /// <param name="message">Message to show</param>
        void Invalidate(NFCTagReaderSession session, string? message = null)
        {
            customInvalidation = true;
            if (string.IsNullOrWhiteSpace(message)) session.InvalidateSession();
            else session.InvalidateSession(message);
        }

        /// <summary>
        /// Writes or clears a TAG
        /// </summary>
        /// <param name="session"><see cref="NFCTagReaderSession"/></param>
        /// <param name="tag"><see cref="INFCNdefTag"/></param>
        /// <param name="tagInfo"><see cref="ITagInfo"/></param>
        /// <param name="clearMessage">Clear message</param>
        void ExecuteWriteOrClear(NFCTagReaderSession session, INFCNdefTag tag, ITagInfo tagInfo, bool clearMessage = false)
        {
            tag.QueryNdefStatus((status, capacity, error) =>
            {
                if (error != null)
                {
                    Invalidate(session, error.LocalizedDescription);
                    return;
                }

                if (status == NFCNdefStatus.ReadOnly)
                {
                    Invalidate(session, Configuration.Messages.NFCErrorReadOnlyTag);
                    return;
                }

                if (Convert.ToInt32(capacity) < NfcUtils.GetSize(tagInfo.Records))
                {
                    Invalidate(session, Configuration.Messages.NFCErrorCapacityTag);
                    return;
                }

                NFCNdefMessage message = null;
                if (!clearMessage)
                {
                    session.AlertMessage = Configuration.Messages.NFCSuccessWrite;

                    var records = new List<NFCNdefPayload>();
                    foreach (var record in tagInfo.Records)
                    {
                        if (NFCNdefPayloadExtensions.GetiOSPayload(record, Configuration) is NFCNdefPayload ndefPayload)
                            records.Add(ndefPayload);
                    }

                    if (records.Any())
                        message = new NFCNdefMessage(records.ToArray());
                }
                else
                {
                    session.AlertMessage = Configuration.Messages.NFCSuccessClear;
                    message = NfcNdefMessageExtensions.EmptyNdefMessage;
                }

                if (message != null)
                {
                    tag.WriteNdef(message, (nsError) =>
                    {
                        if (nsError != null)
                        {
                            Invalidate(session, nsError.LocalizedDescription);
                            return;
                        }

                        tagInfo.Records = NFCNdefPayloadExtensions.GetRecords(message.Records);
                        OnMessagePublished?.Invoke(tagInfo);
                        if (NfcSession != null) Invalidate(NfcSession);
                    });
                }
                else
                    Invalidate(session, Configuration.Messages.NFCErrorWrite);
            });
        }

        /// <summary>
        /// Make a tag read-only
        /// WARNING: This operation is permanent
        /// </summary>
        /// <param name="session"><see cref="NFCTagReaderSession"/></param>
        /// <param name="tag"><see cref="ITagInfo"/></param>
        /// <param name="ndefTag"><see cref="INFCNdefTag"/></param>
        void MakeTagReadOnly(NFCTagReaderSession session, INFCTag tag, INFCNdefTag ndefTag)
        {
            session.ConnectTo(tag, (error) =>
            {
                if (error != null)
                {
                    Console.WriteLine(error.LocalizedDescription);
                    return;
                }

                ndefTag.WriteLock((nsError) =>
                {
                    if (nsError != null)
                        Console.WriteLine("Error when locking a tag on iOS: " + nsError.LocalizedDescription);
                    else
                        Console.WriteLine("Locking Successful!");
                });
            });
        }

        #endregion
    }

    /// <summary>
    /// Old iOS implementation of <see cref="INfc"/> (iOS less than 13)
    /// </summary>
    public class NfcImplementationBeforeIOs13 : NFCNdefReaderSessionDelegate, INfc
    {
        public const string SessionTimeoutMessage = "session timeout";

        private bool isWriting;
        private bool isFormatting;
        private bool customInvalidation;
        private INFCNdefTag? tag;

        public event EventHandler? OnTagConnected;
        public event EventHandler? OnTagDisconnected;
        public event NdefMessageReceivedEventHandler? OnMessageReceived;
        public event NdefMessagePublishedEventHandler? OnMessagePublished;
        public event TagDiscoveredEventHandler? OnTagDiscovered;
        public event EventHandler? OnIOsReadingSessionCancelled;
        public event OnNfcStatusChangedEventHandler? OnNfcStatusChanged;
        public event TagListeningStatusChangedEventHandler? OnTagListeningStatusChanged;
        public event EventHandler? OnTagIntentReceived;

        NFCNdefReaderSession? NfcSession { get; set; }

        public bool FullTagScanAndUpload { get; set; }

        /// <summary>
        /// Checks if NFC Feature is available
        /// </summary>
        public bool IsAvailable => NFCNdefReaderSession.ReadingAvailable;

        /// <summary>
        /// Checks if NFC Feature is enabled
        /// </summary>
        public bool IsEnabled => IsAvailable;

        /// <summary>
        /// Checks if writing mode is supported
        /// </summary>
        public bool IsWritingTagSupported => NfcUtils.IsWritingSupported();

        /// <summary>
        /// NFC configuration
        /// </summary>
        public NfcConfiguration Configuration { get; private set; }

        /// <summary>
        /// Default constructor
        /// </summary>
        public NfcImplementationBeforeIOs13()
        {
            Configuration = NfcConfiguration.GetDefaultConfiguration();
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
            customInvalidation = false;
            isWriting = false;
            isFormatting = false;

            NfcSession = new NFCNdefReaderSession(this, DispatchQueue.CurrentQueue, true)
            {
                AlertMessage = Configuration.Messages.NFCDialogAlertMessage
            };

            NfcSession?.BeginSession();
            OnTagListeningStatusChanged?.Invoke(true);
        }

        /// <summary>
        /// Stops tags detection
        /// </summary>
        public void StopListening()
        {
            NfcSession?.InvalidateSession();
        }

        /// <summary>
        /// Starts tag publishing (writing or formatting)
        /// </summary>
        /// <param name="clearMessage">Format tag</param>
        public void StartPublishing(bool clearMessage = false)
        {
            if (!IsAvailable)
            {
                throw new InvalidOperationException(Configuration.Messages.NFCWritingNotSupported);
            }

            customInvalidation = false;
            isWriting = true;
            isFormatting = clearMessage;

            NfcSession = new NFCNdefReaderSession(this, DispatchQueue.CurrentQueue, true)
            {
                AlertMessage = Configuration.Messages.NFCDialogAlertMessage
            };
            NfcSession?.BeginSession();
            OnTagListeningStatusChanged?.Invoke(true);
        }

        /// <summary>
        /// Stops tag publishing
        /// </summary>
        public void StopPublishing()
        {
            isWriting = isFormatting = customInvalidation = false;
            tag = null;
            NfcSession?.InvalidateSession();
        }

        /// <summary>
        /// Publish or write a message on a tag
        /// </summary>
        /// <param name="tagInfo">see <see cref="ITagInfo"/></param>
        /// <param name="makeReadOnly">Make a tag read-only</param>
        public void PublishMessage(ITagInfo tagInfo, bool makeReadOnly = false) => WriteOrClearMessage(tag, tagInfo, false, makeReadOnly);

        /// <summary>
        /// Format tag
        /// </summary>
        /// <param name="tagInfo">see <see cref="ITagInfo"/></param>
        public void ClearMessage(ITagInfo tagInfo) => WriteOrClearMessage(tag, tagInfo, true);

        /// <summary>
        /// Event raised when NDEF messages are detected
        /// </summary>
        /// <param name="session">iOS <see cref="NFCNdefReaderSession"/></param>
        /// <param name="messages">Array of iOS <see cref="NFCNdefMessage"/></param>
        public override void DidDetect(NFCNdefReaderSession session, NFCNdefMessage[] messages)
        {
            OnTagConnected?.Invoke(null, EventArgs.Empty);

            if (messages != null && messages.Length > 0)
            {
                var first = messages[0];
                var tagInfo = new TagInfo
                {
                    IsWritable = false,
                    Records = NFCNdefPayloadExtensions.GetRecords(first.Records)
                };
                OnMessageReceived?.Invoke(tagInfo);
            }

            OnTagDisconnected?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>
        /// Event raised when NFC tag detected
        /// </summary>
        /// <param name="session">iOS <see cref="NFCNdefReaderSession"/></param>
        /// <param name="tags">Array of iOS <see cref="INFCNdefTag"/></param>
        public override void DidDetectTags(NFCNdefReaderSession session, INFCNdefTag[] tags)
        {
            customInvalidation = false;
            tag = tags.First();

            session.ConnectToTag(tag, error =>
            {
                if (error != null)
                {
                    Invalidate(session, error.LocalizedDescription);
                    return;
                }

                if (tag == null)
                {
                    Invalidate(session, Configuration.Messages.NFCErrorNotCompliantTag);
                    return;
                }

                tag.QueryNdefStatus((status, capacity, ndefError) =>
                {
                    if (ndefError != null)
                    {
                        Invalidate(session, ndefError.LocalizedDescription);
                        return;
                    }

                    var isNdefSupported = status != NFCNdefStatus.NotSupported;

                    var identifier = NfcNdefTagExtensions.GetTagIdentifier(tag);
                    var nTag = new TagInfo(identifier, isNdefSupported)
                    {
                        IsWritable = status == NFCNdefStatus.ReadWrite,
                        Capacity = Convert.ToInt32(capacity)
                    };

                    if (!isNdefSupported)
                    {
                        session.AlertMessage = Configuration.Messages.NFCErrorNotSupportedTag;

                        OnMessageReceived?.Invoke(nTag);
                        Invalidate(session);
                        return;
                    }

                    if (isWriting)
                    {
                        // Write mode
                        OnTagDiscovered?.Invoke(nTag, isFormatting);
                    }
                    else
                    {
                        // Read mode
                        tag.ReadNdef((message, readError) =>
                        {
                            if (readError != null)
                            {
                                Invalidate(session, readError.Code == (int)NFCReaderError.NdefReaderSessionErrorZeroLengthMessage
                                    ? Configuration.Messages.NFCErrorEmptyTag
                                    : Configuration.Messages.NFCErrorRead);
                                return;
                            }

                            session.AlertMessage = Configuration.Messages.NFCSuccessRead;

                            nTag.Records = NFCNdefPayloadExtensions.GetRecords(message.Records);
                            OnMessageReceived?.Invoke(nTag);
                            Invalidate(session);
                        });
                    }
                });
            });
        }

        /// <summary>
        /// Event raised when an error happened during detection
        /// </summary>
        /// <param name="session">iOS <see cref="NFCTagReaderSession"/></param>
        /// <param name="error">iOS <see cref="NSError"/></param>
        public override void DidInvalidate(NFCNdefReaderSession session, NSError error)
        {
            OnTagListeningStatusChanged?.Invoke(false);

            var readerError = (NFCReaderError)(long)error.Code;
            if (readerError != NFCReaderError.ReaderSessionInvalidationErrorFirstNDEFTagRead && readerError != NFCReaderError.ReaderSessionInvalidationErrorUserCanceled)
            {
                var alertController = UIAlertController.Create(Configuration.Messages.NFCSessionInvalidated, error.LocalizedDescription.ToLower().Equals(SessionTimeoutMessage) ? Configuration.Messages.NFCSessionTimeout : error.LocalizedDescription, UIAlertControllerStyle.Alert);
                alertController.AddAction(UIAlertAction.Create(Configuration.Messages.NFCSessionInvalidatedButton, UIAlertActionStyle.Default, null));
                OnIOsReadingSessionCancelled?.Invoke(null, EventArgs.Empty);
                DispatchQueue.MainQueue.DispatchAsync(() =>
                {
                    GetCurrentController().PresentViewController(alertController, true, null);
                });
            }
            else if (readerError == NFCReaderError.ReaderSessionInvalidationErrorUserCanceled && !customInvalidation)
            {
                OnIOsReadingSessionCancelled?.Invoke(null, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Write or Clear a NDEF message
        /// </summary>
        /// <param name="tag"><see cref="INFCTag"/></param>
        /// <param name="tagInfo"><see cref="ITagInfo"/></param>
        /// <param name="clearMessage">Clear Message</param>
        /// <param name="makeReadOnly">Make a tag read-only</param>
        internal void WriteOrClearMessage(INFCNdefTag? tag, ITagInfo? tagInfo, bool clearMessage = false, bool makeReadOnly = false)
        {
            if (NfcSession == null)
            {
                return;
            }

            if (tag == null)
            {
                Invalidate(NfcSession, Configuration.Messages.NFCErrorMissingTag);
                return;
            }

            if (tagInfo == null || (!clearMessage && tagInfo.Records.Any(record => record.Payload == null)))
            {
                Invalidate(NfcSession, Configuration.Messages.NFCErrorMissingTagInfo);
                return;
            }

            if (this.tag == null)
            {
                Invalidate(NfcSession, Configuration.Messages.NFCErrorNotCompliantTag);
                return;
            }

            try
            {
                if (!this.tag.Available)
                {
                    NfcSession.ConnectToTag(this.tag, (error) =>
                    {
                        if (error != null)
                        {
                            Invalidate(NfcSession, error.LocalizedDescription);
                            return;
                        }

                        ExecuteWriteOrClear(NfcSession, this.tag, tagInfo, clearMessage);
                    });
                }
                else
                {
                    ExecuteWriteOrClear(NfcSession, this.tag, tagInfo, clearMessage);
                }

                if (!clearMessage && makeReadOnly)
                {
                    MakeTagReadOnly(NfcSession, tag);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.StackTrace);
            }
            finally
            {
                OnTagDisconnected?.Invoke(null, EventArgs.Empty);
            }
        }

        #region Private

        /// <summary>
        /// Returns the current iOS controller
        /// </summary>
        /// <returns>Object <see cref="UIViewController"/></returns>
        UIViewController GetCurrentController()
        {
            var window = UIApplication.SharedApplication.KeyWindow;
            var vc = window.RootViewController;
            while (vc.PresentedViewController != null)
                vc = vc.PresentedViewController;
            return vc;
        }

        /// <summary>
        /// Writes or clears a TAG
        /// </summary>
        /// <param name="session"><see cref="NFCTagReaderSession"/></param>
        /// <param name="tag"><see cref="INFCNdefTag"/></param>
        /// <param name="tagInfo"><see cref="ITagInfo"/></param>
        /// <param name="clearMessage">Clear message</param>
        private void ExecuteWriteOrClear(NFCNdefReaderSession session, INFCNdefTag tag, ITagInfo tagInfo, bool clearMessage = false)
        {
            tag.QueryNdefStatus((status, capacity, error) =>
            {
                if (error != null)
                {
                    Invalidate(session, error.LocalizedDescription);
                    return;
                }

                if (status == NFCNdefStatus.ReadOnly)
                {
                    Invalidate(session, Configuration.Messages.NFCErrorReadOnlyTag);
                    return;
                }

                if (Convert.ToInt32(capacity) < NfcUtils.GetSize(tagInfo.Records))
                {
                    Invalidate(session, Configuration.Messages.NFCErrorCapacityTag);
                    return;
                }

                NFCNdefMessage message = null;
                if (!clearMessage)
                {
                    session.AlertMessage = Configuration.Messages.NFCSuccessWrite;

                    var records = new List<NFCNdefPayload>();
                    for (var i = 0; i < tagInfo.Records.Length; i++)
                    {
                        var record = tagInfo.Records[i];
                        if (NFCNdefPayloadExtensions.GetiOSPayload(record, Configuration) is NFCNdefPayload ndefPayload)
                            records.Add(ndefPayload);
                    }

                    if (records.Any())
                        message = new NFCNdefMessage(records.ToArray());
                }
                else
                {
                    session.AlertMessage = Configuration.Messages.NFCSuccessClear;
                    message = NfcNdefMessageExtensions.EmptyNdefMessage;
                }

                if (message != null)
                {
                    tag.WriteNdef(message, (error) =>
                    {
                        if (error != null)
                        {
                            Invalidate(session, error.LocalizedDescription);
                            return;
                        }

                        tagInfo.Records = NFCNdefPayloadExtensions.GetRecords(message.Records);
                        OnMessagePublished?.Invoke(tagInfo);
                        Invalidate(NfcSession);
                    });
                }
                else
                    Invalidate(session, Configuration.Messages.NFCErrorWrite);
            });
        }

        /// <summary>
        /// Make a tag read-only
        /// WARNING: This operation is permanent
        /// </summary>
        /// <param name="session"><see cref="NFCTagReaderSession"/></param>
        /// <param name="tag"><see cref="ITagInfo"/></param>
        private void MakeTagReadOnly(NFCNdefReaderSession session, INFCNdefTag tag)
        {
            session.ConnectToTag(tag, (error) =>
            {
                if (error != null)
                {
                    Console.WriteLine(error.LocalizedDescription);
                    return;
                }

                tag.WriteLock((error) =>
                {
                    if (error != null)
                        Console.WriteLine("Error when locking a tag on iOS: " + error.LocalizedDescription);
                    else
                        Console.WriteLine("Locking Successful!");
                });
            });
        }

        /// <summary>
        /// Invalidate the session
        /// </summary>
        /// <param name="session"><see cref="NFCTagReaderSession"/></param>
        /// <param name="message">Message to show</param>
        void Invalidate(NFCNdefReaderSession session, string? message = null)
        {
            customInvalidation = true;
            if (string.IsNullOrWhiteSpace(message))
                session.InvalidateSession();
            else
                session.InvalidateSession(message);
        }

        #endregion
    }

    /// <summary>
    /// NFC Ndef Message extensions class
    /// </summary>
    internal static class NfcNdefMessageExtensions
    {
        /// <summary>
        /// Convert an iOS <see cref="NSData"/> into an array of bytes
        /// </summary>
        /// <param name="data">iOS <see cref="NSData"/></param>
        /// <returns>Array of bytes</returns>
        public static byte[] ToByteArray(this NSData data)
        {
            var bytes = new byte[(int)data.Length];
            if ((int)data.Length > 0) System.Runtime.InteropServices.Marshal.Copy(data.Bytes, bytes, 0, Convert.ToInt32(data.Length));
            return bytes;
        }

        /// <summary>
        /// Converte an iOS <see cref="NFCNdefMessage"/> into an array of bytes
        /// </summary>
        /// <param name="message">iOS <see cref="NFCNdefMessage"/></param>
        /// <returns>Array of bytes</returns>
        public static byte[] ToByteArray(this NFCNdefMessage message)
        {
            var records = message.Records;

            // Empty message: single empty record
            if (records == null || records.Length == 0) records = [];

            var m = new MemoryStream();
            for (var i = 0; i < records.Length; i++)
            {
                var record = records[i];
                var typeNameFormat = record?.TypeNameFormat ?? NFCTypeNameFormat.Empty;
                var payload = record?.Payload;
                var id = record?.Identifier;
                var type = record?.Type;

                var flags = (byte)typeNameFormat;

                // Message begin / end flags. If there is only one record in the message, both flags are set.
                if (i == 0)
                    flags |= 0x80;      // MB (message begin = first record in the message)
                if (i == records.Length - 1)
                    flags |= 0x40;      // ME (message end = last record in the message)

                // cf (chunked records) not supported yet

                // SR (Short Record)?
                if (payload == null || (int)payload.Length < 255)
                    flags |= 0x10;

                // ID present?
                if (id != null && (int)id.Length > 0)
                    flags |= 0x08;

                m.WriteByte(flags);

                // Type length
                if (type != null)
                    m.WriteByte((byte)type.Length);
                else
                    m.WriteByte(0);

                // Payload length 1 byte (SR) or 4 bytes
                if (payload == null)
                {
                    m.WriteByte(0);
                }
                else
                {
                    if ((flags & 0x10) != 0)
                    {
                        // SR
                        m.WriteByte((byte)payload.Length);
                    }
                    else
                    {
                        // No SR (Short Record)
                        var payloadLength = (uint)payload.Length;
                        m.WriteByte((byte)(payloadLength >> 24));
                        m.WriteByte((byte)(payloadLength >> 16));
                        m.WriteByte((byte)(payloadLength >> 8));
                        m.WriteByte((byte)(payloadLength & 0x000000ff));
                    }
                }

                // ID length
                if (id != null && (flags & 0x08) != 0)
                    m.WriteByte((byte)id.Length);

                // Type length
                if (type != null && (int)type.Length > 0)
                    m.Write(type.ToArray(), 0, (int)type.Length);

                // ID data
                if (id != null && (int)id.Length > 0)
                    m.Write(id.ToArray(), 0, (int)id.Length);

                // Payload data
                if (payload != null && (int)payload.Length > 0)
                    m.Write(payload.ToArray(), 0, (int)payload.Length);
            }

            return m.ToArray();
        }

        /// <summary>
        /// Returns an empty iOS <see cref="NFCNdefMessage"/>
        /// </summary>
        /// <returns>iOS <see cref="NFCNdefMessage"/></returns>
        internal static NFCNdefMessage EmptyNdefMessage
        {
            get
            {
                var records = new NFCNdefPayload[1];
                records[0] = NFCNdefPayloadExtensions.EmptyPayload;
                return new NFCNdefMessage(records);
            }
        }
    }

    /// <summary>
    /// NFC Ndef Payload Extensions Class
    /// </summary>
    internal static class NFCNdefPayloadExtensions
    {
        /// <summary>
        /// Returns ndef payload into MimeType
        /// </summary>
        /// <param name="payload"><see cref="NFCNdefPayload"/></param>
        /// <returns>string</returns>
        public static string? ToMimeType(this NFCNdefPayload payload)
        {
            switch (payload.TypeNameFormat)
            {
                case NFCTypeNameFormat.NFCWellKnown:
                    if (payload.Type.ToString() == "T")
                        return "text/plain";
                    break;
                case NFCTypeNameFormat.Media:
                    return payload.Type.ToString();
            }

            return null;
        }

        /// <summary>
        /// Returns Ndef payload into URI
        /// </summary>
        /// <param name="payload"><see cref="NFCNdefPayload"/></param>
        /// <returns><see cref="Uri"/></returns>
        public static Uri? ToUri(this NFCNdefPayload? payload)
        {
            if (payload == null) return null;

            switch (payload.TypeNameFormat)
            {
                case NFCTypeNameFormat.NFCWellKnown:
                    if (payload.Type.ToString() == "U")
                    {
                        var uri = payload.Payload.ParseWktUri();
                        return uri;
                    }
                    break;
                case NFCTypeNameFormat.AbsoluteUri:
                case NFCTypeNameFormat.Media:
                    var content = Encoding.UTF8.GetString(payload.Payload.ToByteArray());
                    if (Uri.TryCreate(content, UriKind.RelativeOrAbsolute, out var result))
                        return result;

                    break;
            }
            return null;
        }

        /// <summary>
        /// Returns complete URI of TNF_WELL_KNOWN, RTD_URI records.
        /// </summary>
        /// <returns><see cref="Uri"/></returns>
        private static Uri? ParseWktUri(this NSData data)
        {
            var payload = data.ToByteArray();

            if (payload.Length < 2) return null;

            var prefixIndex = payload[0] & 0xFF;
            if (prefixIndex < 0 || prefixIndex >= _uri_Prefixes_Map.Length) return null;

            var prefix = _uri_Prefixes_Map[prefixIndex];
            var suffix = Encoding.UTF8.GetString(CopyOfRange(payload, 1, payload.Length));

            if (Uri.TryCreate(prefix + suffix, UriKind.Absolute, out var result)) return result;

            return null;
        }

        /// <summary>
        /// Copy a range of an array into another array
        /// </summary>
        /// <param name="src">Array of <see cref="byte"/></param>
        /// <param name="start">Start</param>
        /// <param name="end">End</param>
        /// <returns>Array of <see cref="byte"/></returns>
        private static byte[] CopyOfRange(byte[] src, int start, int end)
        {
            var length = end - start;
            var dest = new byte[length];
            for (var i = 0; i < length; i++)
                dest[i] = src[start + i];
            return dest;
        }

        /// <summary>
        /// NFC Forum "URI Record Type Definition"
        /// This is a mapping of "URI Identifier Codes" to URI string prefixes,
        /// per section 3.2.2 of the NFC Forum URI Record Type Definition document.
        /// </summary>
        private static readonly string[] _uri_Prefixes_Map =
        [
            "", // 0x00
            "http://www.", // 0x01
            "https://www.", // 0x02
            "http://", // 0x03
            "https://", // 0x04
            "tel:", // 0x05
            "mailto:", // 0x06
            "ftp://anonymous:anonymous@", // 0x07
            "ftp://ftp.", // 0x08
            "ftps://", // 0x09
            "sftp://", // 0x0A
            "smb://", // 0x0B
            "nfs://", // 0x0C
            "ftp://", // 0x0D
            "dav://", // 0x0E
            "news:", // 0x0F
            "telnet://", // 0x10
            "imap:", // 0x11
            "rtsp://", // 0x12
            "urn:", // 0x13
            "pop:", // 0x14
            "sip:", // 0x15
            "sips:", // 0x16
            "tftp:", // 0x17
            "btspp://", // 0x18
            "btl2cap://", // 0x19
            "btgoep://", // 0x1A
            "tcpobex://", // 0x1B
            "irdaobex://", // 0x1C
            "file://", // 0x1D
            "urn:epc:id:", // 0x1E
            "urn:epc:tag:", // 0x1F
            "urn:epc:pat:", // 0x20
            "urn:epc:raw:", // 0x21
            "urn:epc:", // 0x22
            "urn:nfc:" // 0x23
        ];

        /// <summary>
        /// Returns an empty iOS <see cref="NFCNdefPayload"/>
        /// </summary>
        /// <returns>iOS <see cref="NFCNdefPayload"/></returns>
        internal static NFCNdefPayload EmptyPayload => new(NFCTypeNameFormat.Empty, new NSData(), new NSData(), new NSData());

        /// <summary>
        /// Transforms an array of <see cref="NFCNdefPayload"/> into an array of <see cref="NfcNdefRecord"/>
        /// </summary>
        /// <param name="records">Array of <see cref="NFCNdefPayload"/></param>
        /// <returns>Array of <see cref="NfcNdefRecord"/></returns>
        internal static NfcNdefRecord[] GetRecords(NFCNdefPayload[] records)
        {
            if (records == null) return [];

            var results = new NfcNdefRecord[records.Length];
            for (var i = 0; i < records.Length; i++)
            {
                var record = records[i];
                var ndefRecord = new NfcNdefRecord
                {
                    TypeFormat = (NfcNdefTypeFormat)record.TypeNameFormat,
                    Uri = records[i].ToUri()?.ToString(),
                    MimeType = records[i].ToMimeType() ?? "text/plain",
                    Payload = record.Payload?.ToByteArray()
                };
                results.SetValue(ndefRecord, i);
            }
            return results;
        }

        /// <summary>
        /// Returns NDEF payload
        /// </summary>
        /// <param name="record"><see cref="NfcNdefRecord"/></param>
        /// <param name="configuration"></param>
        /// <returns><see cref="NFCNdefPayload"/></returns>
        internal static NFCNdefPayload? GetiOSPayload(NfcNdefRecord? record, NfcConfiguration configuration)
        {
            if (record == null) return null;

            NFCNdefPayload? payload = null;

            switch (record.TypeFormat)
            {
                case NfcNdefTypeFormat.WellKnown:
                    var lang = record.LanguageCode;
                    if (string.IsNullOrWhiteSpace(lang)) lang = configuration.DefaultLanguageCode;
                    var langData = Encoding.ASCII.GetBytes(lang.Substring(0, 2));
                    var payloadData = new byte[] { 0x02 }.Concat(langData).Concat(record.Payload).ToArray();
                    payload = new NFCNdefPayload(NFCTypeNameFormat.NFCWellKnown, NSData.FromString("T"), new NSData(), NSData.FromString(Encoding.UTF8.GetString(payloadData), NSStringEncoding.UTF8));
                    break;
                case NfcNdefTypeFormat.Mime:
                    payload = new NFCNdefPayload(NFCTypeNameFormat.Media, record.MimeType, new NSData(), NSData.FromArray(record.Payload));
                    break;
                case NfcNdefTypeFormat.Uri:
                    payload = NFCNdefPayload.CreateWellKnownTypePayload(NSUrl.FromString(Encoding.UTF8.GetString(record.Payload)));
                    break;
                case NfcNdefTypeFormat.External:
                    payload = new NFCNdefPayload(NFCTypeNameFormat.NFCExternal, record.ExternalType, new NSData(), NSData.FromString(Encoding.UTF8.GetString(record.Payload), NSStringEncoding.UTF8));
                    break;
                case NfcNdefTypeFormat.Empty:
                    payload = EmptyPayload;
                    break;
                case NfcNdefTypeFormat.Unknown:
                case NfcNdefTypeFormat.Unchanged:
                case NfcNdefTypeFormat.Reserved:
                default:
                    break;
            }
            return payload;
        }
    }

    /// <summary>
    /// NFC Tag Extensions Class
    /// </summary>
    internal static class NfcNdefTagExtensions
    {
        /// <summary>
        /// Get Ndef tag
        /// </summary>
        /// <param name="tag"><see cref="INFCTag"/></param>
        /// <returns><see cref="INFCNdefTag"/></returns>
        internal static INFCNdefTag? GetNdefTag(INFCTag? tag)
        {
            if (tag == null || !tag.Available) return null;

            INFCNdefTag ndef;

#if NET6_0_OR_GREATER
            if (tag.Type == NFCTagType.MiFare)
                ndef = tag.AsNFCMiFareTag;
            else if (tag.Type == NFCTagType.Iso7816Compatible)
                ndef = tag.AsNFCIso7816Tag;
            else if (tag.Type == NFCTagType.Iso15693)
                ndef = tag.AsNFCIso15693Tag;
            else if (tag.Type == NFCTagType.FeliCa)
                ndef = tag.AsNFCFeliCaTag;
            else
                ndef = null;
#else
			if (tag.GetNFCMiFareTag() != null)
				ndef = tag.GetNFCMiFareTag();
			else if (tag.GetNFCIso7816Tag() != null)
				ndef = tag.GetNFCIso7816Tag();
			else if (tag.GetNFCIso15693Tag() != null)
				ndef = tag.GetNFCIso15693Tag();
			else if (tag.GetNFCFeliCaTag() != null)
				ndef = tag.GetNFCFeliCaTag();
			else
				ndef = null;
#endif

            return ndef;
        }

        /// <summary>
        /// Returns NFC Tag identifier
        /// </summary>
        /// <param name="tag"><see cref="INFCNdefTag"/></param>
        /// <returns>Tag identifier</returns>
        internal static byte[]? GetTagIdentifier(INFCNdefTag tag)
        {
            var identifier = tag switch
            {
                INFCMiFareTag mifareTag => mifareTag.Identifier.ToByteArray(),
                INFCFeliCaTag felicaTag => felicaTag.CurrentIdm.ToByteArray(),
                INFCIso15693Tag iso15693Tag => iso15693Tag.Identifier.ToByteArray().Reverse().ToArray(),
                INFCIso7816Tag iso7816Tag => iso7816Tag.Identifier.ToByteArray(),
                _ => null
            };

            return identifier;
        }
    }
}
