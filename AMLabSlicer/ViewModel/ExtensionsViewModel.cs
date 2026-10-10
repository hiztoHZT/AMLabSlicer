using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using AMLabSlicer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
namespace AMLabSlicer.ViewModel;

public partial class ExtensionsViewModel : ObservableObject
{
    private readonly PluginManager _plugins;
    private readonly IUserDialogService _dialogs;
    public PreferencesViewModel Preferences { get; }
    public ObservableCollection<string> Sections { get; } = new() { "插件管理" };
    public string DirectoryPath => _plugins.DirectoryPath;
    public ObservableCollection<PluginPackage> Packages => _plugins.Packages;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsPluginSection), nameof(IsManagementSection))] private string _selectedSection = "插件管理";
    [ObservableProperty] private UserControl? _selectedSettingsPage;
    [ObservableProperty] private string _status = "插件配置集中在此窗口中修改";
    public bool IsManagementSection => SelectedSection == "插件管理";
    public bool IsPluginSection => !IsManagementSection;
    public ExtensionsViewModel(PreferencesViewModel preferences, PluginManager plugins, IUserDialogService dialogs)
    {
        Preferences = preferences; _plugins = plugins; _dialogs = dialogs;
    }
    partial void OnSelectedSectionChanged(string value)
    {
        SelectedSettingsPage = null;
        try
        {
            var package = Packages.FirstOrDefault(p => Label(p) == value && p.Instance is not null);
            if (package is not null) SelectedSettingsPage = _plugins.CreateSettingsPage(package.Id);
        }
        catch (Exception ex) { Status = "插件设置页面无法显示：" + ex.Message; SelectedSection = "插件管理"; }
    }
    public void SelectSection(string idOrName)
    {
        var package = Packages.FirstOrDefault(p => p.Instance is not null && (p.Id == idOrName || p.Name == idOrName));
        SelectedSection = package is null ? "插件管理" : Label(package);
        OnSelectedSectionChanged(SelectedSection);
    }
    [RelayCommand] private void Refresh()
    {
        try
        {
            _plugins.Discover();
            var selected = SelectedSection;
            Sections.Clear(); Sections.Add("插件管理");
            foreach (var package in Packages.Where(p => p.Instance is not null)) Sections.Add(Label(package));
            SelectedSection = Sections.Contains(selected) ? selected : "插件管理";
            Status = $"共 {Packages.Count} 个插件包，{_plugins.Panels.Count} 个已启用";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status = "读取插件目录失败：" + ex.Message; }
    }
    private string Label(PluginPackage package) => package.Name == "插件管理" || Packages.Count(p => p.Instance is not null && p.Name == package.Name) > 1
        ? $"{package.Name} ({package.Id})" : package.Name;
    [RelayCommand] private async Task InstallAsync()
    {
        var path = _dialogs.SelectPluginPackage(); if (path is null) return;
        try { await _plugins.InstallAsync(path); Refresh(); Status = "插件已安装，请在下方启用"; }
        catch (Exception ex) { Status = "安装失败：" + ex.Message; }
    }
    [RelayCommand] private async Task EnableAsync(PluginPackage package)
    {
        try { await _plugins.EnableAsync(package); Refresh(); Status = package.State; }
        catch (Exception ex) { Status = "启用失败：" + ex.Message; }
    }
    [RelayCommand] private async Task DisableAsync(PluginPackage package)
    {
        SelectedSettingsPage = null;
        try { await _plugins.DisableAsync(package); Refresh(); Status = package.State; }
        catch (Exception ex) { Status = "停用失败：" + ex.Message; }
    }
    [RelayCommand] private void OpenDirectory()
    {
        try { Directory.CreateDirectory(DirectoryPath); Process.Start(new ProcessStartInfo(DirectoryPath) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { Status = "打开目录失败：" + ex.Message; }
    }
    [RelayCommand] private async Task RemoveAsync(PluginPackage package)
    {
        SelectedSettingsPage = null;
        try { await _plugins.RemoveAsync(package); Refresh(); Status = package.State; }
        catch (Exception ex) { Status = "卸载失败：" + ex.Message; }
    }
}
