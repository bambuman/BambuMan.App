using Android.Text;
using Microsoft.Maui.Handlers;

namespace BambuMan;

/// <summary>
/// Custom Entry/Editor mapper that strips AndroidX EmojiTextWatcher from the underlying EditText.
/// Prevents IllegalArgumentException ("end should be &lt; than charSequence length") crash
/// in emoji2's SpannableBuilder during backspace on foldable devices after configuration changes.
/// See: https://github.com/dotnet/maui/issues/32004
/// </summary>
public static class NoEmojiEntryHandler
{
    public static void Register()
    {
        EntryHandler.Mapper.AppendToMapping("RemoveEmojiWatcher", (handler, _) =>
        {
            RemoveEmojiWatcher(handler.PlatformView);
        });

        EditorHandler.Mapper.AppendToMapping("RemoveEmojiWatcher", (handler, _) =>
        {
            RemoveEmojiWatcher(handler.PlatformView);
        });
    }

    private static void RemoveEmojiWatcher(Android.Widget.TextView platformView)
    {
        // EditText.EditableText exposes the spannable with attached watchers
        var spannable = platformView.EditableText;
        if (spannable == null) return;

        var watchers = spannable.GetSpans(0, spannable.Length(), Java.Lang.Class.FromType(typeof(ITextWatcher)));
        if (watchers == null) return;

        foreach (var watcher in watchers)
        {
            if (watcher?.GetType().FullName?.Contains("Emoji") == true)
                spannable.RemoveSpan(watcher);
        }
    }
}
