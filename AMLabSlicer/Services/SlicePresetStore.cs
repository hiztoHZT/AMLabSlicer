using System.Globalization;
using System.IO;
using System.Text.Json;

namespace AMLabSlicer.Services;

public sealed class SlicePreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "默认配置";
    public Dictionary<string, string> Values { get; set; } = new();
}
public sealed class SlicePresetGroup
{
    public string SelectedId { get; set; } = "";
    public List<SlicePreset> Presets { get; set; } = new();
}
internal sealed class SlicePresetStore
{
    private readonly string path;
    private readonly string legacyPath;
    public SlicePresetStore(string? directory = null)
    {
        directory ??= AppStoragePaths.DataDirectory;
        path = Path.Combine(directory, "named-slice-presets.json");
        legacyPath = Path.Combine(directory, "slice-presets.json");
    }
    private Dictionary<string, SlicePresetGroup> Load() => File.Exists(path)
        ? JsonSerializer.Deserialize<Dictionary<string, SlicePresetGroup>>(File.ReadAllText(path))
          ?? throw new InvalidDataException("参数配置文件为空。") : new();
    private SlicePresetGroup Ensure(Dictionary<string, SlicePresetGroup> all, string context)
    {
        if (all.TryGetValue(context, out var group) && group.Presets.Count > 0) return group;
        var preset = new SlicePreset();
        if (File.Exists(legacyPath))
        {
            var legacy = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string,string>>>(File.ReadAllText(legacyPath));
            if (legacy?.TryGetValue(context, out var values) == true) preset.Values = values;
        }
        preset.Values.Remove("atom.fixed_pose_confirmed");
        all[context] = group = new() { SelectedId = preset.Id, Presets = new() { preset } };
        LocalJsonFile.Save(path, all);
        return group;
    }
    public SlicePresetGroup Group(string context) { var all = Load(); return Ensure(all, context); }
    public Dictionary<string,string> Read(string context)
    {
        var parts=context.Split('#',2); var group=Group(parts[0]);
        return new(group.Presets.Single(p=>p.Id==(parts.Length==2 ? parts[1] : group.SelectedId)).Values);
    }
    public void Save(string context, string key, object? value)
    {
        if (key == "atom.fixed_pose_confirmed") return;
        var parts=context.Split('#',2); var all=Load(); var group=Ensure(all,parts[0]);
        var preset=group.Presets.Single(p=>p.Id==(parts.Length==2 ? parts[1] : group.SelectedId));
        preset.Values[key]=Convert.ToString(value,CultureInfo.InvariantCulture) ?? "";
        LocalJsonFile.Save(path,all);
    }
    public void Select(string context, string id)
    {
        var all=Load(); var group=Ensure(all,context);
        if(!group.Presets.Any(p=>p.Id==id)) throw new ArgumentException("参数配置不存在。");
        group.SelectedId=id; LocalJsonFile.Save(path,all);
    }
    public void Create(string context, string name, Dictionary<string,string> values)
    {
        var all=Load();var group=Ensure(all,context);ValidateName(group,name);
        var preset=new SlicePreset {Name=name.Trim(),Values=new(values)};
        preset.Values.Remove("atom.fixed_pose_confirmed");
        group.Presets.Add(preset);group.SelectedId=preset.Id;LocalJsonFile.Save(path,all);
    }
    public void Rename(string context,string id,string name)
    {
        var all=Load();var group=Ensure(all,context);ValidateName(group,name,id);
        group.Presets.Single(p=>p.Id==id).Name=name.Trim();LocalJsonFile.Save(path,all);
    }
    public void Delete(string context,string id)
    {
        var all=Load();var group=Ensure(all,context);
        if(group.Presets.Count<=1) throw new InvalidOperationException("至少保留一个参数配置。");
        group.Presets.RemoveAll(p=>p.Id==id);
        if(group.SelectedId==id)group.SelectedId=group.Presets[0].Id;
        LocalJsonFile.Save(path,all);
    }
    private static void ValidateName(SlicePresetGroup group,string name,string? except=null)
    {
        if(string.IsNullOrWhiteSpace(name) || name.Trim().Length>80)throw new ArgumentException("配置名称需要 1–80 个字符。");
        if(group.Presets.Any(p=>p.Id!=except && string.Equals(p.Name,name.Trim(),StringComparison.OrdinalIgnoreCase)))throw new ArgumentException("同一切片方法下已存在此配置名称。");
    }
}
