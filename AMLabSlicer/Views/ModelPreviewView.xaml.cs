using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using System.Windows;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using AMLabSlicer.ViewModel;
using HxOrtho = HelixToolkit.Wpf.SharpDX.OrthographicCamera;
using HxPersp = HelixToolkit.Wpf.SharpDX.PerspectiveCamera;

namespace AMLabSlicer.Views
{
    public partial class ModelPreviewView : UserControl
    {
        private bool _isDragging = false;
        private bool _altDownOnMiddle = false;
        private Point _lastMousePos;
        private DragMode _currentDragMode = DragMode.None;
        private PreferencesViewModel? _prefs;
        /// <summary>注意旋转时的轨道轴心，平移时同步移动，避免平移后旋转弹回原点</summary>
        private Point3D _pivotPoint = new Point3D(0, 0, 0);

        private enum DragMode { None, Rotate, Pan }

        public ModelPreviewView()
        {
            InitializeComponent();
            MainViewport.EffectsManager = new DefaultEffectsManager();
            SetPerspectiveCamera(); // 默认透视
        }

        /// <summary>
        /// 由父视图在 DataContext 设置后调用，接入首选项监听
        /// </summary>
        public void SetPreferences(PreferencesViewModel prefs)
        {
            _prefs = prefs;
            _prefs.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(PreferencesViewModel.UseOrthographic))
                    ApplyCameraMode();
            };
        }

        private void ApplyCameraMode()
        {
            if (_prefs?.UseOrthographic == true)
                SetOrthographicCamera();
            else
                SetPerspectiveCamera();
        }

        private void SetPerspectiveCamera()
        {
            var cam = new HxPersp
            {
                Position      = new Point3D(300, -300, 250),
                LookDirection = new Vector3D(-300, 300, -250),
                UpDirection   = new Vector3D(0, 0, 1),
                NearPlaneDistance = 0.5,
                FarPlaneDistance  = 5000,
                FieldOfView = 45
            };
            MainViewport.Camera = cam;
        }

        private void SetOrthographicCamera()
        {
            var cam = new HxOrtho
            {
                Position      = new Point3D(300, -300, 250),
                LookDirection = new Vector3D(-300, 300, -250),
                UpDirection   = new Vector3D(0, 0, 1),
                NearPlaneDistance = 0.5,
                FarPlaneDistance  = 5000,
                Width = 400
            };
            MainViewport.Camera = cam;
        }

        // ── 鼠标事件处理 ──

        private void MainViewport_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Middle) return;

            var mods = Keyboard.Modifiers;
            _lastMousePos = e.GetPosition(MainViewport);
            _altDownOnMiddle = mods.HasFlag(ModifierKeys.Alt);

            if (_altDownOnMiddle)
            {
                e.Handled = true;
                return;
            }

            _isDragging = true;
            if (mods.HasFlag(ModifierKeys.Shift))
                _currentDragMode = DragMode.Pan;
            else
                _currentDragMode = DragMode.Rotate;

            MainViewport.CaptureMouse();
            e.Handled = true;
        }

        private void MainViewport_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;

            var pos = e.GetPosition(MainViewport);
            var dx = pos.X - _lastMousePos.X;
            var dy = pos.Y - _lastMousePos.Y;
            _lastMousePos = pos;

            switch (_currentDragMode)
            {
                case DragMode.Rotate: ApplyRotation(dx, dy); break;
                case DragMode.Pan:    ApplyPan(dx, dy);      break;
            }

            e.Handled = true;
        }

        private void MainViewport_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Middle) return;

            if (_altDownOnMiddle)
            {
                _altDownOnMiddle = false;
                SnapToNearestCardinalView();
            }
            else
            {
                _isDragging = false;
                _currentDragMode = DragMode.None;
                MainViewport.ReleaseMouseCapture();
            }

            e.Handled = true;
        }

        private void MainViewport_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // 清除可能残留的 Alt 状态
            _altDownOnMiddle = false;
            _isDragging = false;
            _currentDragMode = DragMode.None;

            // 透视模式：沿视线方向前后移动相机位置（线性缩放）
            // 正交模式：改变 Width
            var cam = MainViewport.Camera;
            if (cam is HxOrtho ortho)
            {
                double factor = e.Delta > 0 ? 0.9 : 1.1;
                ortho.Width = Math.Max(10, Math.Min(ortho.Width * factor, 5000));
            }
            else if (cam is HxPersp)
            {
                // 沿 LookDirection 平移相机，线性拉距
                var look = cam.LookDirection;
                look.Normalize();
                double step = e.Delta > 0 ? 15.0 : -15.0;
                cam.Position = cam.Position + look * step;
            }

            e.Handled = true;
        }

        // ── 相机操作实现 ──

        private void ApplyRotation(double dx, double dy)
        {
            var cam   = MainViewport.Camera;
            var look  = cam.LookDirection;
            var up    = cam.UpDirection;
            var right = Vector3D.CrossProduct(look, up); right.Normalize();

            var rotY = new AxisAngleRotation3D(up,    -dx * 0.2);
            var rotX = new AxisAngleRotation3D(right, -dy * 0.2);
            var matY = new RotateTransform3D(rotY).Value;
            var matX = new RotateTransform3D(rotX).Value;

            var newLook = matX.Transform(matY.Transform(look)); newLook.Normalize();
            var newUp   = matX.Transform(matY.Transform(up));   newUp.Normalize();

            // 围绕 _pivotPoint 轨道运动，而不是世界原点
            var offset    = cam.Position - _pivotPoint;
            var newOffset = matX.Transform(matY.Transform(offset));

            cam.LookDirection = newLook;
            cam.UpDirection   = newUp;
            cam.Position      = _pivotPoint + newOffset;
        }

        private void ApplyPan(double dx, double dy)
        {
            var cam   = MainViewport.Camera;
            var look  = cam.LookDirection; look.Normalize();
            var up    = cam.UpDirection;   up.Normalize();
            var right = Vector3D.CrossProduct(look, up); right.Normalize();

            double scale = cam is HxOrtho o
                ? o.Width / MainViewport.ActualWidth
                : 0.3;

            var delta    = right * (-dx * scale) + up * (dy * scale);
            cam.Position = cam.Position + delta;
            // 轨道轴心和相机一起平移，保持旋转通心不变
            _pivotPoint  = _pivotPoint + delta;
        }



        private void SnapToNearestCardinalView()
        {
            var cam  = MainViewport.Camera;
            // snap 时重置轴心到场景原点
            _pivotPoint = new Point3D(0, 0, 0);
            var look = cam.LookDirection;
            var len  = look.Length;
            if (len < 0.0001) return;

            var nx = look.X / len; var ny = look.Y / len; var nz = look.Z / len;
            double dist = 500;
            Vector3D newLook, newUp;
            double px, py, pz;

            if (Math.Abs(nx) >= Math.Abs(ny) && Math.Abs(nx) >= Math.Abs(nz))
            {
                if (nx > 0) { newLook = new Vector3D(-1, 0, 0); px = dist;  py = 0; pz = 0; }
                else        { newLook = new Vector3D( 1, 0, 0); px = -dist; py = 0; pz = 0; }
                newUp = new Vector3D(0, 0, 1);
            }
            else if (Math.Abs(ny) >= Math.Abs(nx) && Math.Abs(ny) >= Math.Abs(nz))
            {
                if (ny > 0) { newLook = new Vector3D(0, -1, 0); px = 0; py = dist;  pz = 0; }
                else        { newLook = new Vector3D(0,  1, 0); px = 0; py = -dist; pz = 0; }
                newUp = new Vector3D(0, 0, 1);
            }
            else
            {
                if (nz > 0) { newLook = new Vector3D(0, 0, -1); px = 0; py = 0; pz = dist;  }
                else        { newLook = new Vector3D(0, 0,  1); px = 0; py = 0; pz = -dist; }
                newUp = new Vector3D(0, 1, 0);
            }

            cam.Position      = new Point3D(px, py, pz);
            cam.LookDirection = newLook;
            cam.UpDirection   = newUp;
        }
    }
}