namespace BambuMan
{
    public enum ToastKind
    {
        Info,
        Success,
        Error
    }

    public record ToastMessage(Guid Id, ToastKind Kind, string Text);

    /// <summary>Short notices shown bottom-right by the layout's toast host.</summary>
    public class ToastService
    {
        private readonly List<ToastMessage> toasts = [];

        public event Action? Changed;

        public IReadOnlyList<ToastMessage> Toasts
        {
            get
            {
                lock (toasts) return toasts.ToList();
            }
        }

        public void Show(string text, ToastKind kind = ToastKind.Info, bool isLong = false)
        {
            var toast = new ToastMessage(Guid.NewGuid(), kind, text);

            lock (toasts) toasts.Add(toast);
            Changed?.Invoke();

            _ = DismissLaterAsync(toast, TimeSpan.FromSeconds(isLong ? 4 : 2));
        }

        public void Dismiss(Guid id)
        {
            lock (toasts) toasts.RemoveAll(x => x.Id == id);
            Changed?.Invoke();
        }

        private async Task DismissLaterAsync(ToastMessage toast, TimeSpan delay)
        {
            await Task.Delay(delay);
            Dismiss(toast.Id);
        }
    }
}
