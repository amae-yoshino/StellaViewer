using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Documents;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

/// <summary>Scale viewer chrome only. The image viewport never receives a layout transform.</summary>
internal static class UiSizing
{
    private sealed class Scope : INotifyPropertyChanged
    {
        internal UiSizeSettings Settings = new();
        public double FontFactor => Settings.FontSize / 14d;
        public event PropertyChangedEventHandler? PropertyChanged;
        internal void Update(UiSizeSettings settings) { Settings = settings; PropertyChanged?.Invoke(this, new(nameof(FontFactor))); }
    }
    private sealed class FontConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => (double)value * (double)parameter;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
    private sealed record Metrics(double MinHeight, double Height);
    private sealed record Bounds(double Width, double Height, double MinWidth, double MinHeight);
    private sealed record WidthMetrics(double Width, double MinWidth);
    private static readonly DependencyProperty ScopeProperty = DependencyProperty.RegisterAttached("Scope", typeof(Scope), typeof(UiSizing), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));
    private static readonly ConditionalWeakTable<FrameworkElement, Metrics> metrics = new();
    private static readonly ConditionalWeakTable<Window, Bounds> bounds = new();
    private static readonly ConditionalWeakTable<FrameworkElement, WidthMetrics> widths = new();
    private static readonly FontConverter converter = new();
    private static bool initialized;
    internal static void Initialize()
    {
        if (initialized) return; initialized = true;
        EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) => {
            if (sender is FrameworkElement element && (element.GetValue(ScopeProperty) as Scope ?? Window.GetWindow(element)?.GetValue(ScopeProperty) as Scope) is Scope scope) ApplyElement(element, scope);
        }));
    }
    private static void ApplyElement(FrameworkElement element, Scope scope)
    {
        var property = TextElement.FontSizeProperty;
        if (property is not null) {
            if (BindingOperations.GetBinding(element, property) is { Source: Scope previous, Converter: FontConverter, ConverterParameter: double original } && !ReferenceEquals(previous, scope))
                BindingOperations.SetBinding(element, property, new Binding(nameof(Scope.FontFactor)) { Source = scope, Converter = converter, ConverterParameter = original, Mode = BindingMode.OneWay });
            var source = DependencyPropertyHelper.GetValueSource(element, property);
            if (!source.IsExpression && source.BaseValueSource is BaseValueSource.Local or BaseValueSource.Style && element.GetValue(property) is double size)
                BindingOperations.SetBinding(element, property, new Binding(nameof(Scope.FontFactor)) { Source = scope, Converter = converter, ConverterParameter = size, Mode = BindingMode.OneWay });
        }
        // Fixed editor/button heights and tile captions must grow with larger text.
        if (element is Control or TextBlock && element.ReadLocalValue(FrameworkElement.HeightProperty) is double height && height is > 0 and <= 80) {
            var original = metrics.GetValue(element, item => new(item.MinHeight, height));
            element.MinHeight = Math.Max(original.MinHeight, original.Height * Math.Max(1, scope.FontFactor));
        }
        if (element is TabItem && double.IsFinite(element.Width)) {
            var original = widths.GetValue(element, item => new(item.Width, item.MinWidth));
            element.Width = original.Width * Math.Max(1, scope.FontFactor);
        }
        else if (element is TextBlock && element.ReadLocalValue(FrameworkElement.WidthProperty) is double width && width is > 0 and <= 80) {
            var original = widths.GetValue(element, item => new(width, item.MinWidth));
            element.MinWidth = Math.Max(original.MinWidth, original.Width * Math.Max(1, scope.FontFactor));
        }
    }
    private static void Walk(DependencyObject root, Scope scope)
    {
        if (root is FrameworkElement scoped && scoped.ReadLocalValue(ScopeProperty) is Scope local) scope = local;
        if (root is FrameworkElement element) ApplyElement(element, scope);
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) Walk(VisualTreeHelper.GetChild(root, i), scope);
    }
    internal static void ApplyList(FrameworkElement list, int fontSize)
    {
        var scope = list.ReadLocalValue(ScopeProperty) as Scope ?? new Scope();
        list.SetValue(ScopeProperty, scope); scope.Update(new(100, fontSize));
        list.SetValue(TextElement.FontSizeProperty, 14d);
        Walk(list, scope);
    }
    internal static void Apply(Window window, UiSizeSettings value)
    {
        Initialize();
        var scope = window.ReadLocalValue(ScopeProperty) as Scope;
        if (scope is null) {
            scope = new Scope(); var active = scope;
            window.Loaded += (_, _) => Walk(window, active);
        }
        window.SetValue(ScopeProperty, scope); scope.Update(value.Normalize()); Walk(window, scope);
    }
    internal static void ConfigureDialog(Window window, UiSizeSettings value)
    {
        Apply(window, value);
        var original = bounds.GetValue(window, item => new(item.Width, item.Height, item.MinWidth, item.MinHeight));
        var scale = value.Normalize().ScalePercent / 100d;
        if (window.Content is FrameworkElement content) content.LayoutTransform = new ScaleTransform(scale, scale);
        var area = SystemParameters.WorkArea;
        window.MinWidth = Math.Min(original.MinWidth * scale, Math.Max(320, area.Width - 32));
        window.MinHeight = Math.Min(original.MinHeight * scale, Math.Max(240, area.Height - 32));
        if (double.IsFinite(original.Width)) window.Width = Math.Max(window.MinWidth, Math.Min(original.Width * scale, area.Width - 32));
        if (double.IsFinite(original.Height)) window.Height = Math.Max(window.MinHeight, Math.Min(original.Height * scale, area.Height - 32));
    }
    internal static void ConfigurePopup(ContextMenu menu, Window owner)
    {
        if (owner.GetValue(ScopeProperty) is not Scope scope) return;
        menu.SetValue(ScopeProperty, scope);
        menu.FontSize = 14;
        Walk(menu, scope);
    }
}
