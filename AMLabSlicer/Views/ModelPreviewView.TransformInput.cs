using System.Numerics;
using System.Windows.Controls;

namespace AMLabSlicer.Views;

public partial class ModelPreviewView
{
    // System.Numerics uses row vectors: S * R stores each local axis scale in a row.
    private static Vector3 InputScale(Matrix4x4 m) => new(
        new Vector3(m.M11, m.M12, m.M13).Length(),
        new Vector3(m.M21, m.M22, m.M23).Length(),
        new Vector3(m.M31, m.M32, m.M33).Length());

    private static Matrix4x4 InputRotation(Matrix4x4 m)
    {
        var s = InputScale(m);
        if (s.X < 1e-6f || s.Y < 1e-6f || s.Z < 1e-6f) return Matrix4x4.Identity;
        m.M11 /= s.X; m.M12 /= s.X; m.M13 /= s.X;
        m.M21 /= s.Y; m.M22 /= s.Y; m.M23 /= s.Y;
        m.M31 /= s.Z; m.M32 /= s.Z; m.M33 /= s.Z;
        m.Translation = Vector3.Zero;
        return m;
    }

    // Euler angles matching Rx * Ry * Rz (row-vector convention).
    private static void ExtractEulerDeg(Matrix4x4 matrix, out float rx, out float ry, out float rz)
    {
        var m = InputRotation(matrix);
        float y = MathF.Asin(Math.Clamp(-m.M13, -1f, 1f));
        float x, z;
        if (Math.Abs(MathF.Cos(y)) > 1e-4f)
        { x = MathF.Atan2(m.M23, m.M33); z = MathF.Atan2(m.M12, m.M11); }
        else { x = MathF.Atan2(-m.M32, m.M22); z = 0; }
        rx = x * 180 / MathF.PI; ry = y * 180 / MathF.PI; rz = z * 180 / MathF.PI;
    }

    private Vector3 InputValues(Matrix4x4 matrix)
    {
        if (_activeTransformKey == "G") return matrix.Translation;
        if (_activeTransformKey == "S") return InputScale(matrix);
        ExtractEulerDeg(matrix, out float x, out float y, out float z);
        return new(x, y, z);
    }

    private static string InputText(float value) => value.ToString("0.#####");
    private static bool ReadInput(TextBox box, out float value) =>
        float.TryParse(box.Text, out value) && float.IsFinite(value);

    private void PopulateInputBar()
    {
        if (SelectedNode == null || string.IsNullOrEmpty(_activeTransformKey)) return;
        _suppressInput = true;
        try
        {
            var values = InputValues(SelectedNode.ModelMatrix);
            TxfX.Text = InputText(values.X); TxfY.Text = InputText(values.Y); TxfZ.Text = InputText(values.Z);
            SetRelativeInputs(values);
        }
        finally { _suppressInput = false; }
    }

    // Call only while callbacks are suppressed. Both rows refer to the same snapshot.
    private void SetRelativeInputs(Vector3 absolute)
    {
        if (_activeTransformKey == "S") return;
        var delta = absolute - InputValues(_snapMatrix);
        DeltaX.Text = InputText(delta.X); DeltaY.Text = InputText(delta.Y); DeltaZ.Text = InputText(delta.Z);
    }

    private void RelativeTransformBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppressInput || SelectedNode == null || _activeTransformKey is not ("G" or "R")) return;
        if (!ReadInput(DeltaX, out float dx) || !ReadInput(DeltaY, out float dy)) return;
        float dz = 0;
        if (_activeTransformKey == "R" && !ReadInput(DeltaZ, out dz)) return;
        var values = InputValues(_snapMatrix) + new Vector3(dx, dy, dz);
        if (!float.IsFinite(values.X) || !float.IsFinite(values.Y) || !float.IsFinite(values.Z)) return;
        _suppressInput = true;
        try { TxfX.Text = InputText(values.X); TxfY.Text = InputText(values.Y); TxfZ.Text = InputText(values.Z); }
        finally { _suppressInput = false; }
        TransformBox_Changed(this, e);
    }

    private void TransformBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppressInput || SelectedNode == null || string.IsNullOrEmpty(_activeTransformKey)) return;
        if (!ReadInput(TxfX, out float x) || !ReadInput(TxfY, out float y)) return;
        float z = _snapMatrix.Translation.Z;
        if (_activeTransformKey != "G" && !ReadInput(TxfZ, out z)) return;
        var values = new Vector3(x, y, z);
        Matrix4x4 next;
        switch (_activeTransformKey)
        {
            case "G":
                next = _snapMatrix;
                next.Translation = values;
                break;
            case "R":
                var radians = values * (MathF.PI / 180f);
                next = Matrix4x4.CreateScale(InputScale(_snapMatrix)) *
                    Matrix4x4.CreateRotationX(radians.X) * Matrix4x4.CreateRotationY(radians.Y) *
                    Matrix4x4.CreateRotationZ(radians.Z) * Matrix4x4.CreateTranslation(_snapMatrix.Translation);
                break;
            case "S":
                if (ScaleLinkToggle.IsChecked == true && sender is TextBox edited)
                {
                    var current = InputScale(SelectedNode.ModelMatrix);
                    float old = edited == TxfX ? current.X : edited == TxfY ? current.Y : current.Z;
                    float requested = edited == TxfX ? x : edited == TxfY ? y : z;
                    if (old < 1e-6f) return;
                    values = current * (requested / old);
                }
                if (!float.IsFinite(values.X) || !float.IsFinite(values.Y) || !float.IsFinite(values.Z) ||
                    values.X < .001f || values.Y < .001f || values.Z < .001f) return;
                next = Matrix4x4.CreateScale(values) * InputRotation(_snapMatrix) * Matrix4x4.CreateTranslation(_snapMatrix.Translation);
                break;
            default: return;
        }
        SelectedNode.ModelMatrix = next;
        UpdateSelectionTransform();
        UpdateObjectHighlightTransform(_snapMatrix, next, SelectedNode);
        _suppressInput = true;
        try
        {
            if (_activeTransformKey == "S" && ScaleLinkToggle.IsChecked == true)
            {
                // Preserve the field being typed, so intermediate decimals remain editable.
                if (sender != TxfX) TxfX.Text = InputText(values.X);
                if (sender != TxfY) TxfY.Text = InputText(values.Y);
                if (sender != TxfZ) TxfZ.Text = InputText(values.Z);
            }
            else if (sender != this) SetRelativeInputs(values);
        }
        finally { _suppressInput = false; }
    }
}
