using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.SharpDX;
using AMLabSlicer.ViewModel;

namespace AMLabSlicer.Views;

public partial class ModelPreviewView
{
    public static readonly DependencyProperty InsideHighlightGeometryProperty = DependencyProperty.Register(nameof(InsideHighlightGeometry), typeof(LineGeometry3D), typeof(ModelPreviewView));
    public static readonly DependencyProperty OutsideHighlightGeometryProperty = DependencyProperty.Register(nameof(OutsideHighlightGeometry), typeof(LineGeometry3D), typeof(ModelPreviewView));
    public LineGeometry3D? InsideHighlightGeometry { get => (LineGeometry3D?)GetValue(InsideHighlightGeometryProperty); set => SetValue(InsideHighlightGeometryProperty, value); }
    public LineGeometry3D? OutsideHighlightGeometry { get => (LineGeometry3D?)GetValue(OutsideHighlightGeometryProperty); set => SetValue(OutsideHighlightGeometryProperty, value); }
    private static void OnHighlightBoundsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((ModelPreviewView)d).UpdateBoundsWarning();
    private void BuildVolumeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PrepareWorkspaceViewModel.BuildVolumeProfile)) UpdateBoundsWarning();
    }
    private void UpdateBoundsWarning()
    {
        var inside = new LineBuilder();
        var outside = new LineBuilder();
        bool exceeded = false;
        var profile = GetVM()?.BuildVolumeProfile;
        double width = profile?.Number("machine_width") ?? 225;
        double depth = profile?.Number("machine_depth") ?? 225;
        double height = profile?.Number("machine_height") ?? 225;
        bool centered = profile?.Machine.GetValueOrDefault("machine_center_is_zero") == "true";
        bool ellipse = profile?.Machine.GetValueOrDefault("machine_shape") == "elliptic";
        double left = centered ? -width / 2 : 0, bottom = centered ? -depth / 2 : 0;
        double cx = left + width / 2, cy = bottom + depth / 2;
        bool Contains(Vector3 p) => p.Z >= -0.0001 && p.Z <= height + 0.0001 &&
            (ellipse ? Math.Pow((p.X - cx) / (width / 2), 2) + Math.Pow((p.Y - cy) / (depth / 2), 2) <= 1.000001
                     : p.X >= left - 0.0001 && p.X <= left + width + 0.0001 && p.Y >= bottom - 0.0001 && p.Y <= bottom + depth + 0.0001);
        Vector3 Transform(Vector3 p)
        {
            var q = ObjectHighlightTransform.Transform(new Point3D(p.X, p.Y, p.Z));
            return new Vector3((float)q.X, (float)q.Y, (float)q.Z);
        }
        // Split only the cached box edges, never traverse mesh vertices during dragging.
        if (ObjectHighlightGeometry is { Positions: not null, Indices: not null } geometry)
        for (int i = 0; i + 1 < geometry.Indices.Count; i += 2)
        {
            var a = Transform(geometry.Positions[geometry.Indices[i]]);
            var b = Transform(geometry.Positions[geometry.Indices[i + 1]]);
            var delta = b - a;
            var cuts = new List<double> { 0, 1 };
            void Add(double t) { if (double.IsFinite(t) && t > 0 && t < 1) cuts.Add(t); }
            void Plane(double value, double start, double direction) { if (Math.Abs(direction) > 1e-12) Add((value - start) / direction); }
            Plane(0, a.Z, delta.Z); Plane(height, a.Z, delta.Z);
            if (!ellipse)
            {
                Plane(left, a.X, delta.X); Plane(left + width, a.X, delta.X);
                Plane(bottom, a.Y, delta.Y); Plane(bottom + depth, a.Y, delta.Y);
            }
            else
            {
                double x = (a.X - cx) / (width / 2), y = (a.Y - cy) / (depth / 2);
                double dx = delta.X / (width / 2), dy = delta.Y / (depth / 2);
                double aa = dx * dx + dy * dy, bb = 2 * (x * dx + y * dy), cc = x * x + y * y - 1;
                double discriminant = bb * bb - 4 * aa * cc;
                if (aa > 1e-20 && discriminant >= -1e-12)
                {
                    Add((-bb - Math.Sqrt(Math.Max(0, discriminant))) / (2 * aa));
                    Add((-bb + Math.Sqrt(Math.Max(0, discriminant))) / (2 * aa));
                }
            }
            cuts.Sort();
            for (int k = 0; k + 1 < cuts.Count; k++)
            {
                if (cuts[k + 1] - cuts[k] < 1e-9) continue;
                bool valid = Contains(Vector3.Lerp(a, b, (float)((cuts[k] + cuts[k + 1]) / 2)));
                (valid ? inside : outside).AddLine(Vector3.Lerp(a, b, (float)cuts[k]), Vector3.Lerp(a, b, (float)cuts[k + 1]));
                exceeded |= !valid;
            }
        }
        InsideHighlightGeometry = inside.ToLineGeometry3D();
        OutsideHighlightGeometry = outside.ToLineGeometry3D();
        if (SelectionOutline != null) SelectionOutline.Color = exceeded ? Color.FromRgb(255, 64, 64) : Color.FromRgb(45, 155, 255);
    }
}
