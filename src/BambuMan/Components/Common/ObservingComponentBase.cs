using Microsoft.AspNetCore.Components;
using System.Collections.Specialized;
using System.ComponentModel;

namespace BambuMan.Components.Common
{
    /// <summary>
    /// Base for pages that render a view model: re-renders whenever an observed <see cref="INotifyPropertyChanged"/>
    /// or collection changes, from whichever thread raised it, and unsubscribes when the page goes away.
    /// </summary>
    public abstract class ObservingComponentBase : ComponentBase, IDisposable
    {
        private readonly List<Action> unsubscribers = [];

        protected void Observe(INotifyPropertyChanged source)
        {
            source.PropertyChanged += OnSourceChanged;
            unsubscribers.Add(() => source.PropertyChanged -= OnSourceChanged);
        }

        protected void ObserveCollection(INotifyCollectionChanged source)
        {
            source.CollectionChanged += OnSourceChanged;
            unsubscribers.Add(() => source.CollectionChanged -= OnSourceChanged);
        }

        private void OnSourceChanged(object? sender, EventArgs e) => _ = InvokeAsync(StateHasChanged);

        public virtual void Dispose()
        {
            foreach (var unsubscribe in unsubscribers) unsubscribe();
            unsubscribers.Clear();

            GC.SuppressFinalize(this);
        }
    }
}
