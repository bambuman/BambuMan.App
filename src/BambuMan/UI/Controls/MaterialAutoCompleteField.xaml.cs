using System.Collections.ObjectModel;
using HorusStudio.Maui.MaterialDesignControls;

namespace BambuMan.UI.Controls;

/// <summary>
/// A Material text field with a type-ahead suggestion list — replaces UraniumUI's
/// AutoCompleteTextField (HorusStudio has no autocomplete control yet).
/// Free-text capable: <see cref="Text"/> is just the field value, so the user can enter a
/// brand-new value not present in <see cref="ItemsSource"/>. The suggestion list shows
/// substring matches and hides once the text exactly matches an item (preset or after a pick).
/// </summary>
public partial class MaterialAutoCompleteField : ContentView
{
    private readonly ObservableCollection<string> suggestions = [];

    public MaterialAutoCompleteField()
    {
        InitializeComponent();

        SuggestionsView.ItemsSource = suggestions;

        Field.SetBinding(MaterialTextField.TextProperty, new Binding(nameof(Text), BindingMode.TwoWay, source: this));
        Field.SetBinding(MaterialTextField.LabelProperty, new Binding(nameof(Label), source: this));
        Field.SetBinding(MaterialTextField.PlaceholderProperty, new Binding(nameof(Placeholder), source: this));
        Field.SetBinding(MaterialTextField.LeadingIconProperty, new Binding(nameof(LeadingIcon), source: this));
    }

    #region Bindable Properties

    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text), typeof(string), typeof(MaterialAutoCompleteField), default(string),
        BindingMode.TwoWay, propertyChanged: (b, _, _) => ((MaterialAutoCompleteField)b).UpdateSuggestions());

    public string? Text { get => (string?)GetValue(TextProperty); set => SetValue(TextProperty, value); }

    public static readonly BindableProperty ItemsSourceProperty = BindableProperty.Create(
        nameof(ItemsSource), typeof(IEnumerable<string>), typeof(MaterialAutoCompleteField), default(IEnumerable<string>),
        propertyChanged: (b, _, _) => ((MaterialAutoCompleteField)b).UpdateSuggestions());

    public IEnumerable<string>? ItemsSource { get => (IEnumerable<string>?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }

    public static readonly BindableProperty LabelProperty = BindableProperty.Create(
        nameof(Label), typeof(string), typeof(MaterialAutoCompleteField));

    public string? Label { get => (string?)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    public static readonly BindableProperty PlaceholderProperty = BindableProperty.Create(
        nameof(Placeholder), typeof(string), typeof(MaterialAutoCompleteField));

    public string? Placeholder { get => (string?)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }

    public static readonly BindableProperty LeadingIconProperty = BindableProperty.Create(
        nameof(LeadingIcon), typeof(ImageSource), typeof(MaterialAutoCompleteField));

    public ImageSource? LeadingIcon { get => (ImageSource?)GetValue(LeadingIconProperty); set => SetValue(LeadingIconProperty, value); }

    #endregion

    private void UpdateSuggestions()
    {
        var text = Text?.Trim() ?? string.Empty;

        suggestions.Clear();

        if (ItemsSource != null && text.Length > 0)
        {
            foreach (var item in ItemsSource)
            {
                if (string.IsNullOrWhiteSpace(item)) continue;
                if (item.Equals(text, StringComparison.OrdinalIgnoreCase)) continue;
                if (!item.Contains(text, StringComparison.OrdinalIgnoreCase)) continue;

                suggestions.Add(item);
                if (suggestions.Count >= 6) break;
            }
        }

        SuggestionsView.HeightRequest = Math.Min(suggestions.Count, 6) * 44;
        SuggestionsBorder.IsVisible = suggestions.Count > 0;
    }

    private void OnSuggestionSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not string selected) return;

        Text = selected;
        SuggestionsView.SelectedItem = null;
        SuggestionsBorder.IsVisible = false;
    }
}
