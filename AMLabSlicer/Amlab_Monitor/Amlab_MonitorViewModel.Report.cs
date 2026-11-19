using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media.Imaging;
using AMLabSlicer.Amlab_Monitor.Grpc;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AMLabSlicer.Amlab_Monitor;

public sealed partial class Amlab_MonitorDefectItem(DefectBox box) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label))]
    private DefectBox _box = box;

    [ObservableProperty] private bool _isVisible = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SoloLabel))]
    private bool _isSolo;

    public string Label => $"{Box.ClassName}  {Box.Confidence:P1}";
    public string SoloLabel => IsSolo ? "取消独显" : "仅此缺陷";
}

public sealed record Amlab_MonitorDefectLayer(string? ClassName, string Label);

public partial class Amlab_MonitorViewModel
{
    private static readonly Amlab_MonitorDefectLayer AllLayers = new(null, "全部图层");
    private readonly List<Amlab_MonitorDefectItem> _trackedDefects = [];
    private IReadOnlyList<Amlab_MonitorDefectItem> _currentDefects = [];
    private Amlab_MonitorDefectItem? _soloDefect;

    public ObservableCollection<Amlab_MonitorDefectLayer> Amlab_MonitorLayers { get; } = [AllLayers];
    public ObservableCollection<Amlab_MonitorDefectItem> Amlab_MonitorReport { get; } = [];

    [ObservableProperty] private Amlab_MonitorDefectLayer? _amlab_MonitorSelectedLayer = AllLayers;

    partial void OnAmlab_MonitorSelectedLayerChanged(Amlab_MonitorDefectLayer? value)
    {
        if (value?.ClassName is { } name && _soloDefect?.Box.ClassName != name)
            SetSoloDefect(null);
        RefreshReport();
    }

    [RelayCommand]
    private void Amlab_MonitorToggleDefect(Amlab_MonitorDefectItem? item)
    {
        if (item is null)
            return;
        item.IsVisible = !item.IsVisible;
        RefreshReport();
    }

    [RelayCommand]
    private void Amlab_MonitorSoloDefect(Amlab_MonitorDefectItem? item)
    {
        if (item is null)
            return;
        var next = ReferenceEquals(item, _soloDefect) ? null : item;
        if (next is not null)
            next.IsVisible = true;
        SetSoloDefect(next);
        RefreshReport();
    }

    private void SetSoloDefect(Amlab_MonitorDefectItem? item)
    {
        if (_soloDefect is not null)
            _soloDefect.IsSolo = false;
        _soloDefect = item;
        if (_soloDefect is not null)
            _soloDefect.IsSolo = true;
    }

    private void ResetReport()
    {
        SetSoloDefect(null);
        _trackedDefects.Clear();
        _currentDefects = [];
        Amlab_MonitorSelectedLayer = AllLayers;
        for (var i = Amlab_MonitorLayers.Count - 1; i > 0; i--)
            Amlab_MonitorLayers.RemoveAt(i);
    }

    private void UpdateReport(IEnumerable<DefectBox> boxes, bool sameFrame)
    {
        var unmatched = _trackedDefects.ToList();
        var current = new List<Amlab_MonitorDefectItem>();
        foreach (var box in boxes)
        {
            Amlab_MonitorDefectItem? match = null;
            var minimumOverlap = 0.5;
            foreach (var candidate in unmatched)
            {
                if (candidate.Box.ClassName != box.ClassName)
                    continue;
                var overlap = Overlap(candidate.Box, box);
                if (overlap >= minimumOverlap)
                {
                    match = candidate;
                    minimumOverlap = overlap;
                }
            }
            if (match is null)
            {
                match = new Amlab_MonitorDefectItem(box);
                _trackedDefects.Add(match);
            }
            else
            {
                unmatched.Remove(match);
                match.Box = box;
            }
            current.Add(match);

            if (!Amlab_MonitorLayers.Any(layer => layer.ClassName == box.ClassName))
                Amlab_MonitorLayers.Add(new Amlab_MonitorDefectLayer(box.ClassName, box.ClassName));
        }

        // A same-frame recheck can temporarily omit a box when the threshold increases.
        if (!sameFrame)
        {
            _trackedDefects.Clear();
            _trackedDefects.AddRange(current);
            if (_soloDefect is not null && !current.Contains(_soloDefect))
                SetSoloDefect(null);
        }

        _currentDefects = current;
        RefreshReport();
    }

    private void RefreshReport()
    {
        var model = Amlab_MonitorSelectedModel;
        var layerName = Amlab_MonitorSelectedLayer?.ClassName;
        var validThreshold = IsThreshold(Amlab_MonitorConfidence, out var confidence);
        var sameModel = model is not null && _detectedOptions?.ModelId == model.Id;
        var sameIou = !IsThreshold(Amlab_MonitorIou, out var iou) || _detectedOptions?.Iou == iou;
        var rows = sameModel && sameIou
            ? _currentDefects.Where(item => (!validThreshold || item.Box.Confidence >= confidence) &&
                (layerName is null || item.Box.ClassName == layerName)).ToArray()
            : [];

        for (var i = Amlab_MonitorReport.Count - 1; i >= 0; i--)
            if (!rows.Contains(Amlab_MonitorReport[i]))
                Amlab_MonitorReport.RemoveAt(i);
        for (var i = 0; i < rows.Length; i++)
        {
            if (i < Amlab_MonitorReport.Count && ReferenceEquals(Amlab_MonitorReport[i], rows[i]))
                continue;
            var existing = Amlab_MonitorReport.IndexOf(rows[i]);
            if (existing < 0)
                Amlab_MonitorReport.Insert(i, rows[i]);
            else
                Amlab_MonitorReport.Move(existing, i);
        }

        var visible = rows.Where(item => item.IsVisible && (_soloDefect is null || ReferenceEquals(item, _soloDefect)))
            .Select(item => item.Box).ToArray();
        Amlab_MonitorDefectOverlay = visible.Length > 0 && Amlab_MonitorCameraFrame is BitmapSource frame
            ? CreateOverlay(frame, visible)
            : null;
    }

    private static double Overlap(DefectBox first, DefectBox second)
    {
        var a = new Rect(new Point(first.XMin, first.YMin), new Point(first.XMax, first.YMax));
        var b = new Rect(new Point(second.XMin, second.YMin), new Point(second.XMax, second.YMax));
        var intersection = Rect.Intersect(a, b);
        if (intersection.IsEmpty)
            return 0;
        var area = intersection.Width * intersection.Height;
        var union = a.Width * a.Height + b.Width * b.Height - area;
        return union > 0 ? area / union : 0;
    }
}
