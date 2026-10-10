using System.Windows;
using System.Windows.Media;
using AMLabSlicer.Models;

namespace AMLabSlicer.Services;

public interface IAppearanceService
{
    IReadOnlyList<string> FontFamilies { get; }
    void Apply(UserPreferences preferences);
}

public sealed class AppearanceService : IAppearanceService
{
    public IReadOnlyList<string> FontFamilies { get; } = Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(n => n).ToArray();

    public void Apply(UserPreferences preferences)
    {
        var app = Application.Current;
        if (app == null) return;
        if (!app.Dispatcher.CheckAccess()) { app.Dispatcher.Invoke(() => Apply(preferences)); return; }
        AMLabSlicer.Plugin.Wpf.Localization.UiText.Current.Language = preferences.Language;
        var light = preferences.Theme == "浅色";
        var palette = new Dictionary<string, string>
        {
            ["WindowBackgroundBrush"] = light ? "#F3F4F6" : "#1E1E1E",
            ["TitleBarBackgroundBrush"] = light ? "#E9EBEF" : "#252525",
            ["ToolbarBackgroundBrush"] = light ? "#E9EBEF" : "#252525",
            ["SurfaceBrush"] = light ? "#F3F4F6" : "#1E1E1E",
            ["SurfaceElevatedBrush"] = light ? "#FFFFFF" : "#2B2B2B",
            ["SurfaceBorderBrush"] = light ? "#CDD2DA" : "#424242",
            ["ControlBackgroundBrush"] = light ? "#FFFFFF" : "#323232",
            ["ControlBorderBrush"] = light ? "#BEC5CE" : "#424242",
            ["ControlHoverBorderBrush"] = light ? "#8A96A6" : "#666666",
            ["PrimaryTextBrush"] = light ? "#20252B" : "#E6E6E6",
            ["SecondaryTextBrush"] = light ? "#606975" : "#A6A6A6",
            ["ButtonHoverBrush"] = light ? "#E3E7ED" : "#383838",
            ["MenuPopupBackgroundBrush"] = light ? "#FFFFFF" : "#2B2B2B",
            ["MenuPopupBorderBrush"] = light ? "#CDD2DA" : "#424242",
            ["MenuItemHoverBrush"] = light ? "#E3E7ED" : "#383838",
            ["MenuItemBackgroundBrush"] = light ? "#FFFFFF" : "#2B2B2B",
            ["MenuItemTextBrush"] = light ? "#20252B" : "#E6E6E6",
            ["MenuSeparatorBrush"] = light ? "#CDD2DA" : "#424242",
            ["GridSplitterBrush"] = light ? "#E9EBEF" : "#252525",
            ["SplitterTrackBrush"] = light ? "#E9EBEF" : "#252525",
            ["SplitterHandleBrush"] = light ? "#8A96A6" : "#666666",
            ["MutedAccentBrush"] = light ? "#DEECFA" : "#203B56",
            ["SelectionHighlightBrush"] = light ? "#DEECFA" : "#203B56",
            ["AccentBrush"] = light ? "#0067B8" : "#2D9BFF",
            ["AccentHoverBrush"] = light ? "#005A9E" : "#47A9FF",
            ["ControlFocusBorderBrush"] = light ? "#0067B8" : "#2D9BFF",
            ["ExpanderHeaderBrush"] = light ? "#FFFFFF" : "#2B2B2B",
            ["OverlayPanelBrush"] = light ? "#EEFFFFFF" : "#D01A1A1A",
            ["ViewportBackgroundBrush"] = light ? "#E4E7EC" : "#333333",
            ["ViewCubeTextBrush"] = light ? "#505660" : "#C1C7D0",
            ["ViewCubeStrokeBrush"] = light ? "#6B7280" : "#A6AFBD",
            ["WarningBrush"] = light ? "#9B6400" : "#E0A642",
            ["ScrollThumbBrush"] = light ? "#9098A3" : "#666666",
            ["ScrollThumbHoverBrush"] = light ? "#6B7280" : "#888888",
            ["ScrollThumbDraggingBrush"] = light ? "#505660" : "#AAAAAA",
            ["ParameterSliderTrackBrush"] = light ? "#CDD2DA" : "#151515",
            ["ParameterSliderFillBrush"] = light ? "#8A96A6" : "#666666",
            ["ParameterSliderThumbBrush"] = light ? "#606975" : "#D9D9D9"
        };
        foreach (var (name, hex) in palette)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            app.Resources[name] = brush;
        }
        app.Resources["PrimaryTextColor"] = ((SolidColorBrush)app.Resources["PrimaryTextBrush"]).Color;
        app.Resources["BuildGridMinorColor"] = (Color)ColorConverter.ConvertFromString(light ? "#BFC5CE" : "#4D4D4D");
        app.Resources["BuildGridMajorColor"] = (Color)ColorConverter.ConvertFromString(light ? "#A0A8B4" : "#666666");
        app.Resources["UiFontFamily"] = new FontFamily(preferences.FontFamilyName);
        app.Resources["UiFontSize"] = preferences.FontSize;
        app.Resources["UiTitleFontSize"] = preferences.FontSize + 1;
        app.Resources["UiSmallFontSize"] = Math.Max(10, preferences.FontSize - 1);
        app.Resources["UiInputHeight"] = Math.Max(26, preferences.FontSize + 14);
        app.Resources["UiInlineInputHeight"] = Math.Max(24, preferences.FontSize + 12);
        app.Resources["UiParameterRowHeight"] = Math.Max(32, preferences.FontSize + 18);
        app.Resources["UiCardHeaderHeight"] = Math.Max(30, preferences.FontSize + 16);
        app.Resources["UiActionHeight"] = Math.Max(30, preferences.FontSize + 16);
        app.Resources["UiNavigationHeight"] = Math.Max(30, preferences.FontSize + 16);
        app.Resources["UiCategoryItemHeight"] = Math.Max(28, preferences.FontSize + 14);
        app.Resources["UiConfigRowHeight"] = Math.Max(36, preferences.FontSize + 24);
        app.Resources["UiConfigRowGridHeight"] = new GridLength(Math.Max(36, preferences.FontSize + 24));
    }
}
