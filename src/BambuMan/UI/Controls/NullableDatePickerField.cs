using Plainer.Maui.Controls;
using UraniumUI.Material.Controls;
using UraniumUI.Pages;
using UraniumUI.Resources;
using UraniumUI.Views;
using Path = Microsoft.Maui.Controls.Shapes.Path;

namespace BambuMan.UI.Controls;

/// <summary>
/// A DatePickerField that properly supports nullable DateTime bindings.
/// Based on UraniumUI 2.15.0 DatePickerField with the following fixes:
///   - Date property is DateTime? (nullable) instead of DateTime
///   - Clearing sets Date to null instead of DateTime.Today
///   - Picking today's date correctly shows the field
///   - No NullReferenceException on clear
///
/// Remove this class once UraniumUI natively supports nullable dates.
/// </summary>
[ContentProperty(nameof(Validations))]
public class NullableDatePickerField : InputField
{
    public DatePickerView DatePickerView => (Content as DatePickerView)!;

    public override View Content { get; set; } = new DatePickerView
    {
        VerticalOptions = LayoutOptions.Center,
#if ANDROID
        Margin = new Thickness(16, 0),
#else
        Margin = new Thickness(10, 0),
#endif
        Opacity = 0,
    };

    protected StatefulContentView iconClear = new StatefulContentView
    {
        VerticalOptions = LayoutOptions.Center,
        HorizontalOptions = LayoutOptions.End,
        IsVisible = false,
        Padding = 10,
        Content = new Path
        {
            Data = UraniumShapes.X,
            Fill = ColorResource.GetColor("OnBackground", "OnBackgroundDark", Colors.DarkGray).WithAlpha(.5f),
        }
    };

    public override bool HasValue => Date != null;

    public NullableDatePickerField()
    {
        base.RegisterForEvents();
        iconClear.TappedCommand = new Command(OnClearTapped);

        // ReSharper disable once VirtualMemberCallInConstructor
        UpdateClearIconState();

        // Bind the inner DatePicker to follow IsEnabled, font settings.
        // Date is synced manually (not via binding) because DatePicker.Date is non-nullable.
        DatePickerView.SetBinding(DatePicker.IsEnabledProperty, new Binding(nameof(IsEnabled), source: this));
        DatePickerView.SetBinding(DatePicker.FontSizeProperty, new Binding(nameof(FontSize), source: this));
        DatePickerView.SetBinding(DatePicker.FontAutoScalingEnabledProperty, new Binding(nameof(FontAutoScalingEnabled), source: this));
        DatePickerView.SetBinding(DatePicker.FontFamilyProperty, new Binding(nameof(FontFamily), source: this));

        // When the user picks a date in the native dialog, push it to our nullable Date property.
        DatePickerView.DateSelected += OnDatePickerDateSelected;

        // On Android, DatePickerView fires PropertyChanged("IsOpen") when the native
        // DatePickerDialog opens/closes — not Focused/Unfocused. We track this to detect
        // when the user presses OK without changing the pre-selected date (DateSelected
        // doesn't fire in that case).
        DatePickerView.PropertyChanged += OnDatePickerPropertyChanged;
    }

    private bool dialogWasOpen;
    private bool suppressDateSelected;

    protected override object GetValueForValidator()
    {
        return Date!;
    }

    private void OnDatePickerDateSelected(object? sender, DateChangedEventArgs e)
    {
        if (suppressDateSelected) return;
        Date = e.NewDate;
    }

    /// <summary>
    /// Tracks the "IsOpen" property on DatePickerView. On Android the native DatePickerDialog
    /// fires PropertyChanged("IsOpen") instead of Focused/Unfocused. We use this to accept
    /// the picker's date when the dialog closes — needed when the user picks the pre-selected
    /// date (DateSelected doesn't fire in that case).
    /// </summary>
    private void OnDatePickerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != "IsOpen") return;

        var isOpen = (bool)DatePickerView.GetValue(GetIsOpenProperty());
        if (isOpen)
        {
            dialogWasOpen = true;
            return;
        }

        if (!dialogWasOpen) return;
        dialogWasOpen = false;

        Date = DatePickerView.Date;
    }

    private static BindableProperty? isOpenProperty;

    private static BindableProperty GetIsOpenProperty()
    {
        // Cache the reflection lookup
        return isOpenProperty ??= (BindableProperty)typeof(DatePicker)
            .GetField("IsOpenProperty", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.FlattenHierarchy)
            ?.GetValue(null)!;
    }

    protected virtual void OnClearTapped(object parameter)
    {
        if (!IsEnabled) return;

        Date = null;

        // Reset the inner picker to today so re-opening doesn't show the old date.
        // Suppress DateSelected to prevent it from re-setting Date.
        suppressDateSelected = true;
        DatePickerView.Date = DateTime.Today;
        suppressDateSelected = false;
    }

    protected virtual void OnDateChanged()
    {
        OnPropertyChanged(nameof(Date));
        CheckAndShowValidations();

        if (DatePickerView != null)
        {
            DatePickerView.Opacity = Date == null ? 0 : 1;

            // Keep the inner DatePicker in sync when Date is set programmatically.
            suppressDateSelected = true;
            DatePickerView.Date = Date ?? DateTime.Today;
            suppressDateSelected = false;
        }

        if (AllowClear)
            iconClear.IsVisible = Date != null;

        UpdateState();
    }

    protected override void OnIconChanged()
    {
        base.OnIconChanged();

        if (Icon == null)
            DatePickerView.Margin = new Thickness(10, 0);
        else
            DatePickerView.Margin = new Thickness(5, 1);
    }

    protected virtual void OnAllowClearChanged()
    {
        UpdateClearIconState();
    }

    protected virtual void UpdateClearIconState()
    {
        if (AllowClear)
        {
            if (!endIconsContainer.Contains(iconClear))
                endIconsContainer.Add(iconClear);
        }
        else
        {
            endIconsContainer.Remove(iconClear);
        }
    }

    public override void ResetValidation()
    {
        Date = null;
        base.ResetValidation();
    }

    #region Bindable Properties

    public DateTime? Date { get => (DateTime?)GetValue(DateProperty); set => SetValue(DateProperty, value); }

    public static readonly BindableProperty DateProperty = BindableProperty.Create(
        nameof(Date), typeof(DateTime?), typeof(NullableDatePickerField),
        defaultValue: null, defaultBindingMode: BindingMode.TwoWay,
        propertyChanged: (bindable, _, _) => (bindable as NullableDatePickerField)?.OnDateChanged());

    public DateTime MaximumDate { get => (DateTime)GetValue(MaximumDateProperty); set => SetValue(MaximumDateProperty, value); }

    public static readonly BindableProperty MaximumDateProperty = BindableProperty.Create(
        nameof(MaximumDate), typeof(DateTime), typeof(NullableDatePickerField),
        defaultValue: DatePicker.MaximumDateProperty.DefaultValue,
        propertyChanged: (bindable, _, newValue) =>
        {
            if (bindable is NullableDatePickerField field && field.DatePickerView != null)
                field.DatePickerView.MaximumDate = (DateTime)newValue;
        });

    public DateTime MinimumDate { get => (DateTime)GetValue(MinimumDateProperty); set => SetValue(MinimumDateProperty, value); }

    public static readonly BindableProperty MinimumDateProperty = BindableProperty.Create(
        nameof(MinimumDate), typeof(DateTime), typeof(NullableDatePickerField),
        defaultValue: DatePicker.MinimumDateProperty.DefaultValue,
        propertyChanged: (bindable, _, newValue) =>
        {
            if (bindable is NullableDatePickerField field && field.DatePickerView != null)
                field.DatePickerView.MinimumDate = (DateTime)newValue;
        });

    public string Format { get => (string)GetValue(FormatProperty); set => SetValue(FormatProperty, value); }

    public static readonly BindableProperty FormatProperty = BindableProperty.Create(
        nameof(Format), typeof(string), typeof(NullableDatePickerField), DatePicker.FormatProperty.DefaultValue,
        propertyChanged: (bindable, _, newValue) =>
        {
            if (bindable is NullableDatePickerField field && field.DatePickerView != null)
                field.DatePickerView.Format = (string)newValue;
        });

    public Color TextColor { get => (Color)GetValue(TextColorProperty); set => SetValue(TextColorProperty, value); }

    public static readonly BindableProperty TextColorProperty = BindableProperty.Create(
        nameof(TextColor), typeof(Color), typeof(NullableDatePickerField), DatePicker.TextColorProperty.DefaultValue,
        propertyChanged: (bindable, _, newValue) =>
        {
            if (bindable is NullableDatePickerField field && field.DatePickerView != null)
                field.DatePickerView.TextColor = (Color)newValue;
        });

    public double CharacterSpacing { get => (double)GetValue(CharacterSpacingProperty); set => SetValue(CharacterSpacingProperty, value); }

    public static readonly BindableProperty CharacterSpacingProperty = BindableProperty.Create(
        nameof(CharacterSpacing), typeof(double), typeof(NullableDatePickerField), DatePicker.CharacterSpacingProperty.DefaultValue,
        propertyChanged: (bindable, _, newValue) =>
        {
            if (bindable is NullableDatePickerField field && field.DatePickerView != null)
                field.DatePickerView.CharacterSpacing = (double)newValue;
        });

    public bool AllowClear { get => (bool)GetValue(AllowClearProperty); set => SetValue(AllowClearProperty, value); }

    public static readonly BindableProperty AllowClearProperty = BindableProperty.Create(
        nameof(AllowClear),
        typeof(bool), typeof(NullableDatePickerField),
        true,
        propertyChanged: (bindable, _, _) => (bindable as NullableDatePickerField)?.OnAllowClearChanged());

    #endregion
}
