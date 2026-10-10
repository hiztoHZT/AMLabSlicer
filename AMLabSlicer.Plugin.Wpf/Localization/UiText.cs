using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace AMLabSlicer.Plugin.Wpf.Localization;

// Shared by the host and WPF plugins; localizes presentation without changing stored identifiers.
public sealed class UiText : INotifyPropertyChanged
{
    public static UiText Current { get; } = new();
    private readonly Dictionary<string, string> _english;
    private readonly (Regex Pattern, string English)[] _templates;
    private string _language = "zh-CN";
    public string Language
    {
        get => _language;
        set
        {
            value = value == "en" ? "en" : "zh-CN";
            if (_language == value) return;
            _language = value;
            PropertyChanged?.Invoke(this, new(nameof(Language)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public static void SetText(DependencyObject target, DependencyProperty property, string text) =>
        BindingOperations.SetBinding(target, property, TextExtension.Create(new Binding { Source = text }));
    private UiText()
    {
        using var stream = typeof(UiText).Assembly.GetManifestResourceStream("AMLabSlicer.Plugin.Wpf.Localization.en.json")!;
        _english = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
        _templates = _english.Where(p => p.Key.Contains("{0}")).Select(p =>
        {
            var pattern = Regex.Escape(p.Key);
            for (var i = 0; i < 10; i++) pattern = pattern.Replace(Regex.Escape("{" + i + "}"), "(?<v" + i + ">.*?)");
            return (new Regex("^" + pattern + "$", RegexOptions.Singleline, TimeSpan.FromMilliseconds(50)), p.Value);
        }).ToArray();
    }
    public string Translate(string? text)
    {
        if (text is null) return "";
        if (Language != "en") return text;
        if (_english.TryGetValue(text, out var translated)) return translated;
        foreach (var (pattern, english) in _templates)
        {
            var match = pattern.Match(text);
            if (!match.Success) continue;
            var arguments = Enumerable.Range(0, 10).Select(i => (object)match.Groups["v" + i].Value).ToArray();
            if (english.StartsWith("Parameter “", StringComparison.Ordinal)) arguments[0] = Translate((string)arguments[0]);
            return string.Format(CultureInfo.InvariantCulture, english, arguments);
        }
        return text; // Unknown external text stays intact.
    }
}

internal sealed class TranslationConverter : IMultiValueConverter
{
    public static TranslationConverter Instance { get; } = new();
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) => UiText.Current.Translate(values[0] as string);
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

[MarkupExtensionReturnType(typeof(string))]
public sealed class TextExtension : MarkupExtension
{
    public string Key { get; set; } = "";
    public override object ProvideValue(IServiceProvider serviceProvider) => Create(new Binding { Source = Key }).ProvideValue(serviceProvider);
    internal static MultiBinding Create(Binding text)
    {
        var result = new MultiBinding { Mode = BindingMode.OneWay, Converter = TranslationConverter.Instance };
        result.Bindings.Add(text);
        result.Bindings.Add(new Binding(nameof(UiText.Language)) { Source = UiText.Current });
        return result;
    }
}

[MarkupExtensionReturnType(typeof(string))]
public sealed class LocalizedBindingExtension : MarkupExtension
{
    public LocalizedBindingExtension() { }
    public LocalizedBindingExtension(string path) => Path = path;
    public string Path { get; set; } = ".";
    public string? ElementName { get; set; }
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding(Path) { Mode = BindingMode.OneWay };
        if (ElementName is not null) binding.ElementName = ElementName;
        return TextExtension.Create(binding).ProvideValue(serviceProvider);
    }
}
