using System.IO;
using System.Text.Json;
using AMLabSlicer.Core.Printers;

namespace AMLabSlicer.Services;

public sealed class PrinterLibrary
{
    public int SchemaVersion { get; set; } = 1;
    public string ActivePrinterId { get; set; } = "";
    public List<PrinterProfile> Printers { get; set; } = new();
}

public sealed class PrinterProfileStore : IPrinterProfileProvider
{
    private readonly object sync = new();
    private PrinterLibrary library;
    public string Location { get; }
    public string LoadWarning { get; private set; } = "";
    public event EventHandler? Changed;
    public PrinterProfileStore(string? path = null)
    {
        Location = path ?? Path.Combine(AppStoragePaths.DataDirectory, "printers.json");
        library = new PrinterLibrary();
        if (!File.Exists(Location))
        {
            var printer = PrinterProfile.Create(); library.Printers.Add(printer); library.ActivePrinterId = printer.Id;
        }
        else
        {
            try
            {
                library = JsonSerializer.Deserialize<PrinterLibrary>(File.ReadAllText(Location)) ?? throw new JsonException("打印机库为空。");
                ValidateLibrary(library);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException or NullReferenceException)
            {
                // Preserve corrupt or newer files. Never silently overwrite them with defaults.
                library = new(); LoadWarning = "打印机库无法读取；原文件保留，保存已禁用：" + ex.Message;
            }
        }
    }
    public PrinterLibrary Snapshot()
    {
        lock (sync) return JsonSerializer.Deserialize<PrinterLibrary>(JsonSerializer.Serialize(library))!;
    }
    public PrinterProfile GetActiveSnapshot()
    {
        lock (sync)
        {
            if (LoadWarning.Length > 0) throw new InvalidOperationException(LoadWarning);
            return library.Printers.SingleOrDefault(p => p.Id == library.ActivePrinterId)?.Clone()
                ?? throw new InvalidOperationException("没有选择打印机，请在“设置 → 打印机”中新建或选择。");
        }
    }
    public void Save(PrinterLibrary edited)
    {
        lock (sync)
        {
            if (LoadWarning.Length > 0) throw new InvalidOperationException(LoadWarning);
            var candidate = JsonSerializer.Deserialize<PrinterLibrary>(JsonSerializer.Serialize(edited))!;
            foreach (var printer in candidate.Printers) printer.SynchronizeDerivedValues();
            ValidateLibrary(candidate);
            LocalJsonFile.Save(Location, candidate);
            library = candidate;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void Select(string id)
    {
        var snapshot = Snapshot(); snapshot.ActivePrinterId = id; Save(snapshot);
    }
    private static void ValidateLibrary(PrinterLibrary library)
    {
        if (library.SchemaVersion != 1) throw new InvalidOperationException("不支持的打印机库版本。");
        if (library.Printers.Select(p => p.Id).Distinct().Count() != library.Printers.Count) throw new InvalidOperationException("打印机标识重复。");
        if (library.Printers.Count == 0 ? library.ActivePrinterId != "" : !library.Printers.Any(p => p.Id == library.ActivePrinterId)) throw new InvalidOperationException("当前打印机标识无效。");
        foreach (var profile in library.Printers) PrinterValidation.ThrowIfInvalid(profile);
    }
}
