using System.Windows.Controls;
using AMLabSlicer.Plugin.Abstractions;
namespace AMLabSlicer.Plugin.Wpf;

// Only embedded UserControls are accepted. No Window, Show or owner API is exposed.
public interface IPluginSettingsPage
{
    UserControl? CreateSettingsPage();
}
public interface IPanelPlugin : IPlugin, IPluginSettingsPage
{
    UserControl CreatePanel();
}
