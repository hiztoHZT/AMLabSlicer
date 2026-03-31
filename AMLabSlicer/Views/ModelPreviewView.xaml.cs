using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using System.Windows;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using AMLabSlicer.ViewModel;
using HxOrtho = HelixToolkit.Wpf.SharpDX.OrthographicCamera;
using HxPersp = HelixToolkit.Wpf.SharpDX.PerspectiveCamera;
using HelixToolkit.SharpDX.Model.Scene;
using AMLabSlicer.Commands;
using System.Numerics;
using System.Linq;
using System.Collections.Generic;
using AMLabSlicer.Core.Commands;
using AMLabSlicer.Core.Topology;
using AMLabSlicer.State;
using HxMesh = HelixToolkit.SharpDX.MeshGeometry3D;
using HxHit  = HelixToolkit.SharpDX.HitTestResult;

namespace AMLabSlicer.Views
{
    public partial class ModelPreviewView : UserControl
    {
        // ══════════════════════════════════════
        // 依赖属性
        // ══════════════════════════════════════
        public static readonly DependencyProperty SelectedNodeProperty =
            DependencyProperty.Register(nameof(SelectedNode), typeof(SceneNode),
                typeof(ModelPreviewView), new PropertyMetadata(null, OnSelectedNodeChanged));

        public SceneNode? SelectedNode
        {
            get => (SceneNode?)GetValue(SelectedNodeProperty);
            set => SetValue(SelectedNodeProperty, value);
        }

        public static readonly DependencyProperty IsManipulatorVisibleProperty =
            DependencyProperty.Register(nameof(IsManipulatorVisible), typeof(bool),
                typeof(ModelPreviewView), new PropertyMetadata(false));

        public bool IsManipulatorVisible
        {
            get => (bool)GetValue(IsManipulatorVisibleProperty);
            set => SetValue(IsManipulatorVisibleProperty, value);
        }

        /// <summary>选中节点变化时：更新工具栏/Manipulator 可见性。</summary>
        private static void OnSelectedNodeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var v = (ModelPreviewView)d;
            bool hasNode = e.NewValue != null;

            // 若正在变换，切换/取消选中时先静默还原（不记 Undo）
            if (!string.IsNullOrEmpty(v._activeTransformKey))
                v.DeactivateTransform(commit: false);

            v.IsManipulatorVisible = hasNode && v.GetVM()?.IsObjectMode == true;
            v.RefreshToolbarEnabled();

            // 让 gizmo 大小跟随物体尺寸（避免“球太大/太小”）
            if (hasNode && e.NewValue is SceneNode sn)
            {
                try
                {
                    if (TryComputeWorldAabb(sn, out var min, out var max))
                    {
                        float w = max.X - min.X;
                        float h = max.Y - min.Y;
                        float dZ = max.Z - min.Z;
                        float maxDim = MathF.Max(w, MathF.Max(h, dZ));

                        // 按 maxDim 做线性缩放；根据你的场景坐标大多在百毫米级别微调上下限
                        v.ObjectManipulator.SizeScale = (float)Math.Clamp(maxDim / 60f, 0.6f, 6f);
                    }
                }
                catch
                {
                    // bounds 暂时不可用：保持默认值
                }
            }
            else
            {
                v.ObjectManipulator.SizeScale = 2.5f;
            }

            // 选中新物体后，刷新输入栏到新物体的当前值（若输入栏当前不可见则忽略）
            if (hasNode && v.TransformInputBar.Visibility == Visibility.Visible)
                v.PopulateInputBar();
        }

        // ══════════════════════════════════════
        // 内部状态
        // ══════════════════════════════════════
        public AMLabSlicer.Core.Commands.CommandManager CommandDispatcher { get; } = new();

        private bool     _isDragging;
        private Point    _lastMousePos;
        private DragMode _currentDragMode = DragMode.None;
        private PreferencesViewModel? _prefs;
        private Point3D  _pivotPoint = new(0, 0, 0);

        // ── 变换状态 ──
        /// <summary>"G"/"R"/"S"，空字符串代表无活跃变换</summary>
        private string   _activeTransformKey = string.Empty;
        /// <summary>进入 ActivateTransform 时的节点矩阵快照（供 Escape 还原 + Undo 差分）</summary>
        private Matrix4x4 _snapMatrix = Matrix4x4.Identity;
        /// <summary>防止 TextChanged 回调与 PopulateInputBar 死循环</summary>
        private bool     _suppressInput;

        // ── 底面拾取 ──
        private bool _isFacePickMode;

        // ── 面模式 ──
        private HalfEdgeMesh?          _halfEdge;
        private MeshNode?              _editingMeshNode;
        private readonly HashSet<int>  _selectedFaces = new();
        private string                 _faceSelTool = "Q";
        private bool                   _isXRay;
        private OutlinerNodeViewModel? _editingFaceGroup;

        private enum DragMode { None, Rotate, Pan }

        // ══════════════════════════════════════
        // 初始化
        // ══════════════════════════════════════
        public ModelPreviewView()
        {
            InitializeComponent();
            MainViewport.EffectsManager = new DefaultEffectsManager();
            SetPerspectiveCamera();
            Focusable = true;
            PreviewKeyDown      += OnPreviewKeyDown;
            DataContextChanged  += OnDataContextChanged;
        }

        private PrepareWorkspaceViewModel? GetVM() => DataContext as PrepareWorkspaceViewModel;

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is PrepareWorkspaceViewModel vm)
            {
                _prefs = vm.AppPrefs;
                ApplyCameraMode();
                _prefs.PropertyChanged += (_, pe) =>
                {
                    if (pe.PropertyName == nameof(PreferencesViewModel.UseOrthographic)) ApplyCameraMode();
                    if (pe.PropertyName == nameof(PreferencesViewModel.UndoStackDepth))
                        CommandDispatcher.MaxDepth = _prefs.UndoStackDepth;
                };
                CommandDispatcher.MaxDepth = _prefs.UndoStackDepth;

                // Undo/Redo 后刷新输入栏（若有活跃变换）
                CommandDispatcher.CommandExecuted += (_, __) =>
                {
                    if (!string.IsNullOrEmpty(_activeTransformKey) &&
                        TransformInputBar.Visibility == Visibility.Visible)
                        PopulateInputBar();
                };

                vm.PropertyChanged += (_, pe) =>
                {
                    if (pe.PropertyName == nameof(PrepareWorkspaceViewModel.ViewportMode))
                        OnViewportModeChanged(vm.ViewportMode);
                };
            }
        }

        // ══════════════════════════════════════
        // 工具栏启用/激活态管理
        // ══════════════════════════════════════
        /// <summary>有选中物体时启用需要选中才能操作的按钮</summary>
        private void RefreshToolbarEnabled()
        {
            bool has = SelectedNode != null;
            foreach (var b in new[] { BtnMove, BtnRotate, BtnScale, BtnFace, BtnSplit, BtnDelete })
                b.IsEnabled = has;
        }

        /// <summary>设置物体工具栏中的"当前激活"按钮样式（Tag="active"），其余清空</summary>
        private void SetObjectToolActive(string? tag)
        {
            foreach (var btn in new[] { BtnMove, BtnRotate, BtnScale, BtnFace, BtnSplit, BtnDelete })
                btn.Tag = (btn.Tag as string ?? string.Empty) != string.Empty &&
                          btn.Content?.ToString()?.TrimStart().StartsWith(tag ?? "\0", StringComparison.OrdinalIgnoreCase) == true
                          ? "active" : null;
        }

        private void SetFaceToolActive(string? tag)
        {
            foreach (var b in new[] { FBtnClick, FBtnBox, FBtnBrush, FBtnLasso, FBtnAll, FBtnXRay, FBtnLinked })
                b.Tag = null;
            if (tag == null) return;
            var target = tag switch
            {
                "Q" => FBtnClick, "W" => FBtnBox, "E" => FBtnBrush, "R" => FBtnLasso,
                "T" => FBtnXRay,  "L" => FBtnLinked, _ => null
            };
            if (target != null) target.Tag = "active";
        }

        // ══════════════════════════════════════
        // 视口模式切换
        // ══════════════════════════════════════
        private void OnViewportModeChanged(ViewportMode mode)
        {
            if (mode == ViewportMode.ObjectMode)
            {
                _selectedFaces.Clear();
                _halfEdge = null;
                _editingMeshNode = null;
                IsManipulatorVisible = SelectedNode != null;
                Cursor = Cursors.Arrow;
                if (_isXRay) ToggleXRay(false);
                ClearFaceHighlight();
                RefreshToolbarEnabled();
            }
            else
            {
                IsManipulatorVisible = false;
                DeactivateTransform(commit: false);
                _selectedFaces.Clear();

                _editingMeshNode = SelectedNode switch
                {
                    MeshNode mn => mn,
                    // pivot 外层通常是 GroupNode，真正的 MeshNode 在其子树中，因此要遍历整个后代。
                    SceneNode sn => sn.Traverse().OfType<MeshNode>().FirstOrDefault(),
                    _ => null
                };

                if (_editingMeshNode?.Geometry is HxMesh)
                    BuildTopologyAsync(_editingMeshNode);

                SetFaceToolActive("Q");
            }
        }

        private async void BuildTopologyAsync(MeshNode mn)
        {
            if (mn.Geometry is not HxMesh geo) return;
            var idxList = geo.Indices?.ToList() ?? new List<int>();
            await Task.Run(() =>
            {
                _halfEdge = new HalfEdgeMesh();
                _halfEdge.Build(idxList);
            });
        }

        // ══════════════════════════════════════
        // 键盘路由
        // ══════════════════════════════════════
        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            // 防止 TextBox 内的按键被拦截（Tab、Enter 除外）
            // Ctrl+Z / Ctrl+Shift+Z
            if (e.Key == Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) CommandDispatcher.Redo();
                else CommandDispatcher.Undo();
                e.Handled = true; return;
            }

            // Tab 切换模式（不打断 TextBox 内部切焦点）
            if (e.Key == Key.Tab && e.OriginalSource is not TextBox)
            {
                GetVM()?.ToggleViewportModeCommand.Execute(null);
                e.Handled = true; return;
            }

            // Escape：退出当前状态
            if (e.Key == Key.Escape)
            {
                if (!string.IsNullOrEmpty(_activeTransformKey))
                    { DeactivateTransform(commit: false); e.Handled = true; return; }
                if (_isFacePickMode)
                    { _isFacePickMode = false; Cursor = Cursors.Arrow;
                      BtnFace.Tag = null; e.Handled = true; return; }
                if (SelectedNode != null)
                    { SelectedNode = null; e.Handled = true; return; }
            }

            var vm = GetVM();
            if (vm?.IsFaceMode == true)  { HandleFaceModeKey(e); return; }
            if (vm?.IsObjectMode == true) HandleObjectModeKey(e);
        }

        private void HandleObjectModeKey(KeyEventArgs e)
        {
            if (e.Key == Key.A && !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                { ConfirmAndAutoArrange(); e.Handled = true; return; }

            if (SelectedNode == null) return;

            switch (e.Key)
            {
                case Key.G:      ActivateTransform("G"); e.Handled = true; break;
                case Key.R:      ActivateTransform("R"); e.Handled = true; break;
                case Key.S:      ActivateTransform("S"); e.Handled = true; break;
                case Key.Delete:
                case Key.X:      ConfirmAndDelete();     e.Handled = true; break;
                case Key.F:      EnterFacePickMode();    e.Handled = true; break;
                case Key.B:      ConfirmAndSplit();      e.Handled = true; break;
            }
        }

        private void HandleFaceModeKey(KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Q: SetFaceTool("Q"); e.Handled = true; break;
                case Key.W: SetFaceTool("W"); e.Handled = true; break;
                case Key.E: SetFaceTool("E"); e.Handled = true; break;
                case Key.R: SetFaceTool("R"); e.Handled = true; break;
                case Key.A: SelectAllFaces(); e.Handled = true; break;
                case Key.T: ToggleXRay(!_isXRay); e.Handled = true; break;
                case Key.L: SelectLinkedFaces(); e.Handled = true; break;
                case Key.S: SaveFaceGroup(true);  e.Handled = true; break;
                case Key.D: SaveFaceGroup(false); e.Handled = true; break;
            }
        }

        // ══════════════════════════════════════
        // 工具栏点击：从 Content 字符串取 Tag
        // ══════════════════════════════════════
        private void ToolBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            // Content 格式：  "G  移动" / "Tab ▶ 面模式"
            string raw = btn.Content?.ToString() ?? string.Empty;
            string tag = raw.TrimStart().Split(' ')[0];

            switch (tag)
            {
                case "G": ActivateTransform("G"); break;
                case "R": ActivateTransform("R"); break;
                case "S": ActivateTransform("S"); break;
                case "F": EnterFacePickMode(); break;
                case "A": ConfirmAndAutoArrange(); break;
                case "B": ConfirmAndSplit(); break;
                case "X": ConfirmAndDelete(); break;
                case "Tab": GetVM()?.ToggleViewportModeCommand.Execute(null); break;
            }
        }

        private void FaceTool_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            string tag = btn.Content?.ToString()?.TrimStart().Split(' ')[0] ?? string.Empty;
            switch (tag)
            {
                case "Q": case "W": case "E": case "R": SetFaceTool(tag); break;
                case "A": SelectAllFaces(); break;
                case "T": ToggleXRay(!_isXRay); break;
                case "L": SelectLinkedFaces(); break;
                case "S": SaveFaceGroup(true); break;
                case "D": SaveFaceGroup(false); break;
                case "Tab": GetVM()?.ToggleViewportModeCommand.Execute(null); break;
            }
        }

        // ══════════════════════════════════════
        // ActivateTransform / 输入栏逻辑
        // ══════════════════════════════════════
        private void ActivateTransform(string mode)
        {
            if (SelectedNode == null) return;
            if (_activeTransformKey == mode) return; // 再次按相同键则关闭

            // 若已有激活状态，先提交
            if (!string.IsNullOrEmpty(_activeTransformKey))
                DeactivateTransform(commit: true);

            _activeTransformKey = mode;
            _snapMatrix = SelectedNode.ModelMatrix; // 快照，用于 Undo 差分 & Escape 还原

            // Manipulator 仅显示对应轴
            ObjectManipulator.EnableTranslation = mode == "G";
            ObjectManipulator.EnableRotation    = mode == "R";
            ObjectManipulator.EnableScaling     = mode == "S";

            // 修改标签文字
            TransformLabel.Text = mode switch
            {
                "G" => "移动 (mm)", "R" => "旋转 (°)", "S" => "缩放 (×)", _ => ""
            };

            // G 模式：只有 X/Y（贴地约束），隐藏 Z
            bool showZ = mode != "G";
            LblZ.Visibility = showZ ? Visibility.Visible : Visibility.Collapsed;
            TxfZ.Visibility = showZ ? Visibility.Visible : Visibility.Collapsed;

            PopulateInputBar();
            TransformInputBar.Visibility = Visibility.Visible;

            // 激活工具栏按钮
            SetActiveToolBtn(mode);
        }

        /// <summary>把当前 SelectedNode 的值填入输入框（不触发回调）</summary>
        private void PopulateInputBar()
        {
            if (SelectedNode == null || string.IsNullOrEmpty(_activeTransformKey)) return;
            _suppressInput = true;
            var m = SelectedNode.ModelMatrix;
            try
            {
                switch (_activeTransformKey)
                {
                    case "G":
                        TxfX.Text = m.Translation.X.ToString("F2");
                        TxfY.Text = m.Translation.Y.ToString("F2");
                        // Z 隐藏，不填
                        break;
                    case "R":
                        // 从旋转矩阵提取 Euler 角（ZYX 顺序）
                        ExtractEulerDeg(m, out float rx, out float ry, out float rz);
                        TxfX.Text = rx.ToString("F1");
                        TxfY.Text = ry.ToString("F1");
                        TxfZ.Text = rz.ToString("F1");
                        break;
                    case "S":
                        // 各轴缩放倍数 = 对应列向量长度
                        float sx = new Vector3(m.M11, m.M21, m.M31).Length();
                        float sy = new Vector3(m.M12, m.M22, m.M32).Length();
                        float sz = new Vector3(m.M13, m.M23, m.M33).Length();
                        TxfX.Text = sx.ToString("F3");
                        TxfY.Text = sy.ToString("F3");
                        TxfZ.Text = sz.ToString("F3");
                        break;
                }
            }
            finally { _suppressInput = false; }
        }

        private static void ExtractEulerDeg(Matrix4x4 m, out float rx, out float ry, out float rz)
        {
            // 归一化列（去除缩放干扰）
            float lenC0 = new Vector3(m.M11, m.M21, m.M31).Length();
            float lenC1 = new Vector3(m.M12, m.M22, m.M32).Length();
            float lenC2 = new Vector3(m.M13, m.M23, m.M33).Length();
            if (lenC0 < 1e-6f) lenC0 = 1; if (lenC1 < 1e-6f) lenC1 = 1; if (lenC2 < 1e-6f) lenC2 = 1;

            float r00 = m.M11/lenC0, r10 = m.M21/lenC0, r20 = m.M31/lenC0;
            float r01 = m.M12/lenC1, r11 = m.M22/lenC1;
            float r02 = m.M13/lenC2, r12 = m.M23/lenC2, r22 = m.M33/lenC2;

            ry = (float)(Math.Asin(Math.Clamp(-r20, -1.0, 1.0)) * 180.0 / Math.PI);
            float cosY = MathF.Cos(ry * MathF.PI / 180f);
            if (Math.Abs(cosY) > 1e-4f)
            {
                rx = (float)(Math.Atan2(r10, r00) * 180.0 / Math.PI);
                rz = (float)(Math.Atan2(r12, r22) * 180.0 / Math.PI);
            }
            else { rx = (float)(Math.Atan2(-r01, r11) * 180.0 / Math.PI); rz = 0; }
        }

        private void TransformBox_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_suppressInput || SelectedNode == null || string.IsNullOrEmpty(_activeTransformKey)) return;

            if (!float.TryParse(TxfX.Text, out float x)) return;
            if (!float.TryParse(TxfY.Text, out float y)) return;

            switch (_activeTransformKey)
            {
                case "G":
                {
                    // 只修改 X、Y 平移；Z 保持 snap 矩阵的翻译值（贴地由 Commit 时处理）
                    var newMat = _snapMatrix;
                    newMat.M41 = x;   // Translation.X
                    newMat.M42 = y;   // Translation.Y
                    // Z 不变: newMat.M43 = _snapMatrix.M43
                    SelectedNode.ModelMatrix = newMat;
                    break;
                }
                case "R":
                {
                    if (!float.TryParse(TxfZ.Text, out float z)) return;
                    // 从 snap 矩阵中提取平移和缩放，应用新 Euler 旋转
                    var newRot = Matrix4x4.CreateFromYawPitchRoll(
                        y * MathF.PI / 180f,
                        x * MathF.PI / 180f,
                        z * MathF.PI / 180f);

                    // 提取原缩放
                    float sx = new Vector3(_snapMatrix.M11, _snapMatrix.M21, _snapMatrix.M31).Length();
                    float sy = new Vector3(_snapMatrix.M12, _snapMatrix.M22, _snapMatrix.M32).Length();
                    float sz = new Vector3(_snapMatrix.M13, _snapMatrix.M23, _snapMatrix.M33).Length();
                    if (sx < 1e-6f) sx = 1; if (sy < 1e-6f) sy = 1; if (sz < 1e-6f) sz = 1;

                    var scaleM = Matrix4x4.CreateScale(sx, sy, sz);
                    var transM = Matrix4x4.CreateTranslation(_snapMatrix.Translation);

                    SelectedNode.ModelMatrix = scaleM * newRot * transM;
                    break;
                }
                case "S":
                {
                    if (!float.TryParse(TxfZ.Text, out float z)) return;
                    if (x < 0.001f || y < 0.001f || z < 0.001f) return; // 避免零缩放

                    // 提取原旋转（去掉旧缩放后重新应用新缩放）
                    float os1 = new Vector3(_snapMatrix.M11, _snapMatrix.M21, _snapMatrix.M31).Length();
                    float os2 = new Vector3(_snapMatrix.M12, _snapMatrix.M22, _snapMatrix.M32).Length();
                    float os3 = new Vector3(_snapMatrix.M13, _snapMatrix.M23, _snapMatrix.M33).Length();
                    if (os1 < 1e-6f) os1 = 1; if (os2 < 1e-6f) os2 = 1; if (os3 < 1e-6f) os3 = 1;

                    var rotM = _snapMatrix;
                    // 列向量归一化（得到纯旋转矩阵）
                    rotM.M11 /= os1; rotM.M21 /= os1; rotM.M31 /= os1;
                    rotM.M12 /= os2; rotM.M22 /= os2; rotM.M32 /= os2;
                    rotM.M13 /= os3; rotM.M23 /= os3; rotM.M33 /= os3;
                    rotM.M41 = 0; rotM.M42 = 0; rotM.M43 = 0; rotM.M44 = 1;

                    var newMat = Matrix4x4.CreateScale(x, y, z) * rotM *
                                 Matrix4x4.CreateTranslation(_snapMatrix.Translation);
                    SelectedNode.ModelMatrix = newMat;
                    break;
                }
            }
        }

        private void TransformBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)  { DeactivateTransform(commit: true);  e.Handled = true; }
            if (e.Key == Key.Escape) { DeactivateTransform(commit: false); e.Handled = true; }
        }

        private void ApplyTransform_Click(object sender, RoutedEventArgs e)
            => DeactivateTransform(commit: true);

        private void ResetTransform_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedNode == null) return;
            _suppressInput = true;
            switch (_activeTransformKey)
            {
                case "G": TxfX.Text = "0"; TxfY.Text = "0"; break;
                case "R": TxfX.Text = "0"; TxfY.Text = "0"; TxfZ.Text = "0"; break;
                case "S": TxfX.Text = "1"; TxfY.Text = "1"; TxfZ.Text = "1"; break;
            }
            _suppressInput = false;
            // 手动触发一次变换
            TransformBox_Changed(this, null!);
        }

        private void CloseInputBar_Click(object sender, RoutedEventArgs e)
            => DeactivateTransform(commit: false);

        /// <summary>
        /// commit=true：ApplyZFloor → 记录 Undo；commit=false：还原到 _snapMatrix
        /// </summary>
        private void DeactivateTransform(bool commit)
        {
            if (string.IsNullOrEmpty(_activeTransformKey) &&
                TransformInputBar.Visibility != Visibility.Visible) return;

            if (SelectedNode != null)
            {
                if (commit)
                {
                    // G 模式：贴地约束（Z 轴对齐地面）
                    if (_activeTransformKey == "G") ApplyZFloor(SelectedNode);
                    var after = SelectedNode.ModelMatrix;
                    if (after != _snapMatrix)
                        CommandDispatcher.Push(new TransformCommand(
                            SelectedNode, _snapMatrix, after, $"{LabelFor(_activeTransformKey)}"));
                }
                else
                {
                    // 还原
                    if (!string.IsNullOrEmpty(_activeTransformKey))
                        SelectedNode.ModelMatrix = _snapMatrix;
                }
            }

            _activeTransformKey = string.Empty;
            TransformInputBar.Visibility = Visibility.Collapsed;
            ObjectManipulator.EnableTranslation = true;
            ObjectManipulator.EnableRotation    = true;
            ObjectManipulator.EnableScaling     = true;
            SetActiveToolBtn(null);
        }

        private static string LabelFor(string key) => key switch
        { "G" => "移动", "R" => "旋转", "S" => "缩放", _ => "变换" };

        /// <summary>设置工具按钮的 active Tag</summary>
        private void SetActiveToolBtn(string? key)
        {
            BtnMove.Tag   = key == "G" ? "active" : null;
            BtnRotate.Tag = key == "R" ? "active" : null;
            BtnScale.Tag  = key == "S" ? "active" : null;
            BtnFace.Tag   = key == "F" ? "active" : null;
        }

        // ══════════════════════════════════════
        // Z 贴地约束
        // ══════════════════════════════════════
        /// <summary>
        /// 避免 BoundsWithTransform 在某些状态下返回非有限值（NaN/Infinity）导致推飞模型。
        /// 通过遍历网格顶点 + 自己计算 world AABB。
        /// </summary>
        private static bool TryComputeWorldAabb(SceneNode node, out Vector3 min, out Vector3 max)
        {
            min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            bool any = false;

            if (node == null) return false;

            try
            {
                foreach (var n in node.Traverse())
                {
                    if (n is MeshNode mn && mn.Geometry?.Positions != null)
                    {
                        // 只对该 MeshNode 计算一次 world matrix（后续顶点循环复用）
                        var worldM = GetWorldModelMatrix(mn);
                        foreach (var p in mn.Geometry.Positions)
                        {
                            var wp = Vector3.Transform(p, worldM);
                            if (wp.X < min.X) min.X = wp.X; if (wp.X > max.X) max.X = wp.X;
                            if (wp.Y < min.Y) min.Y = wp.Y; if (wp.Y > max.Y) max.Y = wp.Y;
                            if (wp.Z < min.Z) min.Z = wp.Z; if (wp.Z > max.Z) max.Z = wp.Z;
                        }
                        any = true;
                    }
                }
            }
            catch
            {
                return false;
            }

            if (!any) return false;

            bool finite =
                !(float.IsNaN(min.X) || float.IsInfinity(min.X)) &&
                !(float.IsNaN(min.Y) || float.IsInfinity(min.Y)) &&
                !(float.IsNaN(min.Z) || float.IsInfinity(min.Z)) &&
                !(float.IsNaN(max.X) || float.IsInfinity(max.X)) &&
                !(float.IsNaN(max.Y) || float.IsInfinity(max.Y)) &&
                !(float.IsNaN(max.Z) || float.IsInfinity(max.Z));

            return finite;
        }

        private static Matrix4x4 GetWorldModelMatrix(SceneNode node)
        {
            // node.ModelMatrix 是相对于 Parent 的局部矩阵；这里把父链逐层相乘得到 world。
            var stack = new Stack<SceneNode>();
            SceneNode? cur = node;
            while (cur != null)
            {
                stack.Push(cur);
                cur = cur.Parent;
            }

            var m = Matrix4x4.Identity;
            while (stack.Count > 0)
                m = m * stack.Pop().ModelMatrix;

            return m;
        }

        private static void ApplyZFloor(SceneNode node)
        {
            try
            {
                if (!TryComputeWorldAabb(node, out var min, out _)) return;

                // worldZ floor -> 把 node 的包围盒最低点压到 Z=0
                float dz = -min.Z;
                if (Math.Abs(dz) > 1e5f) return; // 保护阈值，避免数值异常推飞

                if (Math.Abs(dz) > 0.01f)
                    // worldZ floor -> 用“预乘”在世界坐标系推移，避免对象已旋转时沿局部 z 推移导致不贴地
                    node.ModelMatrix =
                        Matrix4x4.CreateTranslation(0, 0, dz) *
                        node.ModelMatrix;
            }
            catch { /* 边界未计算完成时忽略 */ }
        }

        // ══════════════════════════════════════
        // 确认弹窗
        // ══════════════════════════════════════
        private bool RequestConfirm(string message, string title, ref bool prefFlag)
        {
            if (!prefFlag) return true;
            var dlg = new Window
            {
                Title = title, Width = 420, Height = 190,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                ResizeMode = ResizeMode.NoResize
            };
            var sp = new StackPanel { Margin = new Thickness(20) };
            var txt = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0,0,0,12) };
            var chk = new CheckBox { Content = "不再显示此提示" };
            var btns = new StackPanel { Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,12,0,0) };
            bool ok = false;
            var yes = new Button { Content = "确定", Width = 72, Margin = new Thickness(0,0,8,0), IsDefault = true };
            var no  = new Button { Content = "取消", Width = 72, IsCancel = true };
            yes.Click += (_,__) => { ok = true; dlg.Close(); };
            no.Click  += (_,__) => dlg.Close();
            btns.Children.Add(yes); btns.Children.Add(no);
            sp.Children.Add(txt); sp.Children.Add(chk); sp.Children.Add(btns);
            dlg.Content = sp;
            dlg.ShowDialog();
            if (chk.IsChecked == true) prefFlag = false;
            return ok;
        }

        // ══════════════════════════════════════
        // 删除
        // ══════════════════════════════════════
        private void ConfirmAndDelete()
        {
            if (SelectedNode == null) return;
            bool flag = _prefs?.EnableDeleteConfirm ?? true;
            if (!RequestConfirm("确定删除选中的模型对象？", "删除模型", ref flag)) return;
            if (_prefs != null) _prefs.EnableDeleteConfirm = flag;

            var parent = SelectedNode.Parent as GroupNode;
            if (parent == null) return;
            parent.RemoveChildNode(SelectedNode);

            var vm = GetVM();
            var flat = vm?.OutlinerItems.SelectMany(FlattenOutliner).ToList();
            var ovm = flat?.FirstOrDefault(n => n.Node == SelectedNode);
            if (ovm != null)
            {
                var parentOvm = flat!.FirstOrDefault(n => n.Children.Contains(ovm));
                if (parentOvm != null) parentOvm.Children.Remove(ovm);
                else vm!.OutlinerItems.Remove(ovm);
            }
            SelectedNode = null;
        }

        // ══════════════════════════════════════
        // 选择底面 (F)
        // ══════════════════════════════════════
        private void EnterFacePickMode()
        {
            _isFacePickMode = true;
            Cursor = Cursors.Cross;
            BtnFace.Tag = "active";
        }

        private void ExecuteBottomFacePick(Point pos)
        {
            _isFacePickMode = false;
            Cursor = Cursors.Arrow;
            BtnFace.Tag = null;

            if (SelectedNode == null) return;
            var hits = MainViewport.FindHits(pos);
            if (hits == null || hits.Count == 0) return;

            var hit = hits.OfType<HxHit>().FirstOrDefault(h =>
                h.ModelHit == SelectedNode ||
                (h.ModelHit is SceneNode sn && sn.Parent == SelectedNode));
            if (hit == null) return;

            var n      = hit.NormalAtHit;
            var normal = new Vector3D(n.X, n.Y, n.Z);
            var target = new Vector3D(0, 0, -1);
            double angle = Vector3D.AngleBetween(normal, target);
            if (angle < 0.01) { ApplyZFloor(SelectedNode); return; }

            var axis = Vector3D.CrossProduct(normal, target);
            if (axis.Length < 1e-6) axis = new Vector3D(1, 0, 0);
            axis.Normalize();
            var rot   = new RotateTransform3D(new AxisAngleRotation3D(axis, angle)).Value;
            var rotM  = new Matrix4x4(
                (float)rot.M11,(float)rot.M12,(float)rot.M13,0,
                (float)rot.M21,(float)rot.M22,(float)rot.M23,0,
                (float)rot.M31,(float)rot.M32,(float)rot.M33,0, 0,0,0,1);
            var oldM = SelectedNode.ModelMatrix;
            SelectedNode.ModelMatrix = SelectedNode.ModelMatrix * rotM;
            ApplyZFloor(SelectedNode);
            CommandDispatcher.Push(new TransformCommand(SelectedNode, oldM, SelectedNode.ModelMatrix, "选择底面"));
        }

        // ══════════════════════════════════════
        // 拆分 (B)
        // ══════════════════════════════════════
        private void ConfirmAndSplit()
        {
            if (SelectedNode == null) return;
            bool flag = _prefs?.EnableSplitConfirm ?? true;
            if (!RequestConfirm(
                "将拆分模型为多个子对象。\n注意：拆分操作默认不可撤销。",
                "拆分模型", ref flag)) return;
            if (_prefs != null) _prefs.EnableSplitConfirm = flag;

            MeshNode? meshNode = SelectedNode as MeshNode
                ?? SelectedNode?.Items?.OfType<MeshNode>().FirstOrDefault();
            if (meshNode?.Geometry is not HxMesh) return;

            var topo = new HalfEdgeMesh();
            topo.Build(meshNode.Geometry.Indices?.ToList() ?? new());
            if (topo.GetAllConnectedComponents().Count <= 1)
            {
                MessageBox.Show("该模型只有一个连通分量，无需拆分。", "拆分", MessageBoxButton.OK);
                return;
            }

            var parentGroup = meshNode.Parent as GroupNode ?? SelectedNode?.Parent as GroupNode;
            if (parentGroup == null) return;

            int idx = 1;
            var childMeshes = meshNode.Items.OfType<MeshNode>().ToList();
            if (!childMeshes.Any()) childMeshes.Add(meshNode);
            foreach (var child in childMeshes)
            {
                var wrap = new GroupNode { Name = $"{meshNode.Name ?? "Part"}_{idx++}" };
                parentGroup.AddChildNode(wrap);
                wrap.AddChildNode(child);
            }
            parentGroup.RemoveChildNode(meshNode);
            SelectedNode = null;

            var vm = GetVM();
            var rootOvm = vm?.OutlinerItems.FirstOrDefault();
            if (rootOvm != null) rootOvm.Children.Clear();
            if (_prefs?.SplitUndoable != true) CommandDispatcher.Clear();
        }

        // ══════════════════════════════════════
        // 自动摆放 (A)
        // ══════════════════════════════════════
        private void ConfirmAndAutoArrange()
        {
            bool flag = _prefs?.EnableArrangeConfirm ?? true;
            if (!RequestConfirm("自动重排场景中所有对象。", "自动摆放", ref flag)) return;
            if (_prefs != null) _prefs.EnableArrangeConfirm = flag;

            var vm = GetVM();
            if (vm?.LoadedModel is not SceneNodeGroupModel3D gm) return;

            var children = gm.GroupNode.Items
                .Where(n => n is MeshNode || n is GroupNode).ToList();
            // 用可靠的 world AABB 替代 BoundsWithTransform，避免在某些状态下返回 Infinity/NaN
            var bounds = new List<(SceneNode node, Vector3 min, Vector3 max, float area)>();
            foreach (var child in children)
            {
                if (TryComputeWorldAabb(child, out var min, out var max))
                {
                    float w = max.X - min.X;
                    float h = max.Y - min.Y;
                    float area = w * h;
                    bounds.Add((child, min, max, area));
                }
                else
                {
                    // 兜底：尽可能避免推飞（若依旧不可信则该节点会被跳过）
                    var b = child.BoundsWithTransform;
                    bool finite =
                        !(float.IsNaN(b.Minimum.X) || float.IsInfinity(b.Minimum.X)) &&
                        !(float.IsNaN(b.Minimum.Y) || float.IsInfinity(b.Minimum.Y)) &&
                        !(float.IsNaN(b.Minimum.Z) || float.IsInfinity(b.Minimum.Z)) &&
                        !(float.IsNaN(b.Maximum.X) || float.IsInfinity(b.Maximum.X)) &&
                        !(float.IsNaN(b.Maximum.Y) || float.IsInfinity(b.Maximum.Y)) &&
                        !(float.IsNaN(b.Maximum.Z) || float.IsInfinity(b.Maximum.Z));
                    if (!finite) continue;

                    float w = b.Maximum.X - b.Minimum.X;
                    float h = b.Maximum.Y - b.Minimum.Y;
                    bounds.Add((child, b.Minimum, b.Maximum, w * h));
                }
            }

            bounds.Sort((a, b) => b.area.CompareTo(a.area));

            float curX = -112.5f, curY = -112.5f, rowH = 0;
            const float pad = 5f;
            var cmds = new List<ICommandAction>();
            foreach (var it in bounds)
            {
                var child = it.node;
                float w = it.max.X - it.min.X, d = it.max.Y - it.min.Y;
                if (curX + w > 112.5f && curX > -112.5f)
                    { curX = -112.5f; curY += rowH + pad; rowH = 0; }

                float dz = -it.min.Z;
                if (Math.Abs(dz) > 1e5f) continue; // bounds 异常兜底
                // 世界坐标推移，避免对象带旋转时沿局部轴推移导致摆放错误
                var newMat =
                    Matrix4x4.CreateTranslation(curX - it.min.X, curY - it.min.Y, dz) *
                    child.ModelMatrix;
                cmds.Add(new TransformCommand(child, child.ModelMatrix, newMat, "摆放"));
                curX += w + pad; rowH = Math.Max(rowH, d);
            }
            CommandDispatcher.ExecuteCommand(new BatchCommand(cmds, "自动摆放"));
        }

        // ══════════════════════════════════════
        // 面模式操作
        // ══════════════════════════════════════
        private void SetFaceTool(string tool) { _faceSelTool = tool; SetFaceToolActive(tool); }
        private void SelectAllFaces()
        {
            if (_halfEdge == null) return;
            for (int i = 0; i < _halfEdge.FaceCount; i++) _selectedFaces.Add(i);
            RefreshFaceHighlight();
        }
        private void SelectLinkedFaces()
        {
            var pos = Mouse.GetPosition(MainViewport);
            var hits = MainViewport.FindHits(pos);
            if (hits == null || hits.Count == 0 || _halfEdge == null) return;
            int fi = hits[0] is HxHit h ? h.IndiceStartLocation / 3 : 0;
            var comp = _halfEdge.GetConnectedComponent(fi);
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) _selectedFaces.ExceptWith(comp);
            else _selectedFaces.UnionWith(comp);
            RefreshFaceHighlight();
        }
        private void ToggleXRay(bool on)
        {
            _isXRay = on;
            FBtnXRay.Tag = on ? "active" : null;
            if (_editingMeshNode?.Material is HelixToolkit.SharpDX.Model.PhongMaterialCore mc)
            {
                var c = mc.DiffuseColor;
                mc.DiffuseColor = new HelixToolkit.Maths.Color4(c.Red, c.Green, c.Blue, on ? 0.3f : 1f);
            }
        }
        private void RefreshFaceHighlight() { }
        private void ClearFaceHighlight()   { _selectedFaces.Clear(); }
        private void SaveFaceGroup(bool over)
        {
            if (_selectedFaces.Count == 0) { MessageBox.Show("请先选择面片。"); return; }
            var vm = GetVM();
            if (vm == null || _editingMeshNode == null) return;
            if (over && _editingFaceGroup != null)
            {
                _editingFaceGroup.FaceIndices!.Clear();
                _editingFaceGroup.FaceIndices.AddRange(_selectedFaces);
            }
            else
            {
                var p = vm.OutlinerItems.SelectMany(FlattenOutliner)
                    .FirstOrDefault(n => n.Node == _editingMeshNode);
                if (p == null) return;
                int gi = p.Children.Count(c => c.IsFaceGroup) + 1;
                var fv = new OutlinerNodeViewModel(_editingMeshNode, $"面片组 {gi}")
                    { FaceIndices = new List<int>(_selectedFaces) };
                p.Children.Add(fv);
                _editingFaceGroup = fv;
            }
        }

        // ══════════════════════════════════════
        // 鼠标交互
        // ══════════════════════════════════════
        private void MainViewport_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            this.Focus();

            if (e.ChangedButton == MouseButton.Left)
            {
                var pos = e.GetPosition(MainViewport);

                if (_isFacePickMode) { ExecuteBottomFacePick(pos); e.Handled = true; return; }

                // 判断是否点在 Manipulator 上（VisualTree 向上查找）
                if (e.OriginalSource is DependencyObject src && IsDescendantOf(src, ObjectManipulator))
                {
                    // Manipulator 处理拖拽，记录变换前快照
                    if (SelectedNode != null) _snapMatrix = SelectedNode.ModelMatrix;
                    return;
                }

                var vm = GetVM();
                if (vm?.IsFaceMode == true) { HandleFaceClick(pos, e); return; }

                // 物体模式：点选
                var hits = MainViewport.FindHits(pos);
                SceneNode? newSel = null;
                if (hits != null && hits.Count > 0)
                {
                    foreach (var hit in hits)
                    {
                        if (hit.ModelHit is SceneNode sn && (sn is MeshNode || sn is GroupNode))
                        {
                            newSel = FindRootNode(sn, vm?.LoadedModel as SceneNodeGroupModel3D);
                            if (newSel != null) break;
                        }
                    }
                }

                if (SelectedNode != newSel)
                    SelectedNode = newSel;
                return;
            }

            // 中键：相机控制
            if (e.ChangedButton == MouseButton.Middle)
            {
                _lastMousePos    = e.GetPosition(MainViewport);
                _currentDragMode = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
                    ? DragMode.Pan : DragMode.Rotate;
                _isDragging = true;
                MainViewport.CaptureMouse();
                e.Handled = true;
            }
        }

        private void MainViewport_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            // Manipulator 拖拽结束后记录 Undo
            if (e.LeftButton == MouseButtonState.Released &&
                !string.IsNullOrEmpty(_activeTransformKey) == false &&
                SelectedNode != null)
            {
                // 当 Manipulator 拖拽结束时（鼠标已松开），flush undo
                var cur = SelectedNode.ModelMatrix;
                if (cur != _snapMatrix && _activeTransformKey == string.Empty)
                {
                    CommandDispatcher.Push(new TransformCommand(SelectedNode, _snapMatrix, cur, "Gizmo 拖拽"));
                    _snapMatrix = cur;
                    PopulateInputBar();
                }
            }

            if (!_isDragging) return;
            var pos = e.GetPosition(MainViewport);
            double dx = pos.X - _lastMousePos.X, dy = pos.Y - _lastMousePos.Y;
            _lastMousePos = pos;
            if (_currentDragMode == DragMode.Rotate) ApplyRotation(dx, dy);
            else                                      ApplyPan(dx, dy);
            e.Handled = true;
        }

        private void MainViewport_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                _isDragging      = false;
                _currentDragMode = DragMode.None;
                MainViewport.ReleaseMouseCapture();

                // Alt + Middle = 对齐到最近标准视角
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
                    SnapToNearestCardinalView();
                e.Handled = true;
            }

            // Manipulator 放手后记录 Undo（无活跃文字输入栏时）
            if (e.ChangedButton == MouseButton.Left && SelectedNode != null &&
                string.IsNullOrEmpty(_activeTransformKey))
            {
                var cur = SelectedNode.ModelMatrix;
                if (cur != _snapMatrix)
                {
                    ApplyZFloor(SelectedNode);
                    CommandDispatcher.Push(new TransformCommand(SelectedNode, _snapMatrix, SelectedNode.ModelMatrix, "Gizmo 拖拽"));
                    _snapMatrix = SelectedNode.ModelMatrix;
                }
            }
        }

        private void MainViewport_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var cam = MainViewport.Camera;
            if (cam is HxOrtho ortho)
                ortho.Width = Math.Max(10, ortho.Width * (e.Delta > 0 ? 0.85 : 1.15));
            else if (cam is HxPersp)
            {
                var look = cam.LookDirection; look.Normalize();
                cam.Position = cam.Position + look * (e.Delta > 0 ? 20.0 : -20.0);
            }
            e.Handled = true;
        }

        // ══════════════════════════════════════
        // 面模式点选
        // ══════════════════════════════════════
        private void HandleFaceClick(Point pos, MouseButtonEventArgs e)
        {
            var hits = MainViewport.FindHits(pos);
            if (hits == null || hits.Count == 0 || _halfEdge == null) return;
            bool sub = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            bool alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);

            if (alt)
            {
                int fi = hits[0] is HxHit ha ? ha.IndiceStartLocation / 3 : 0;
                var loop = _halfEdge.GetFaceLoop(fi);
                if (sub) foreach (var f in loop) _selectedFaces.Remove(f);
                else     foreach (var f in loop) _selectedFaces.Add(f);
            }
            else if (!_isXRay)
            {
                int fi = hits[0] is HxHit h1 ? h1.IndiceStartLocation / 3 : 0;
                if (sub) _selectedFaces.Remove(fi); else _selectedFaces.Add(fi);
            }
            else
            {
                foreach (var hit in hits.OfType<HxHit>())
                {
                    int fi = hit.IndiceStartLocation / 3;
                    if (sub) _selectedFaces.Remove(fi); else _selectedFaces.Add(fi);
                }
            }
            RefreshFaceHighlight();
        }

        // ══════════════════════════════════════
        // 相机
        // ══════════════════════════════════════
        public void SetPreferences(PreferencesViewModel prefs)
        {
            _prefs = prefs;
            _prefs.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(PreferencesViewModel.UseOrthographic)) ApplyCameraMode();
            };
        }

        private void ApplyCameraMode()
        {
            if (_prefs?.UseOrthographic == true) SetOrthographicCamera();
            else SetPerspectiveCamera();
        }

        private void SetPerspectiveCamera()
        {
            MainViewport.Camera = new HxPersp
            {
                Position = new Point3D(300, -300, 250), LookDirection = new Vector3D(-300, 300, -250),
                UpDirection = new Vector3D(0, 0, 1), NearPlaneDistance = 0.5,
                FarPlaneDistance = 5000, FieldOfView = 45
            };
            _pivotPoint = new Point3D(0, 0, 0);
        }

        private void SetOrthographicCamera()
        {
            MainViewport.Camera = new HxOrtho
            {
                Position = new Point3D(300, -300, 250), LookDirection = new Vector3D(-300, 300, -250),
                UpDirection = new Vector3D(0, 0, 1), NearPlaneDistance = 0.5,
                FarPlaneDistance = 5000, Width = 400
            };
            _pivotPoint = new Point3D(0, 0, 0);
        }

        private void ApplyRotation(double dx, double dy)
        {
            var cam   = MainViewport.Camera;
            var look  = cam.LookDirection; look.Normalize();
            var up    = cam.UpDirection;   up.Normalize();
            var right = Vector3D.CrossProduct(look, up); right.Normalize();
            var matY  = new RotateTransform3D(new AxisAngleRotation3D(up,    -dx * 0.3)).Value;
            var matX  = new RotateTransform3D(new AxisAngleRotation3D(right, -dy * 0.3)).Value;
            cam.LookDirection = matX.Transform(matY.Transform(look));
            cam.UpDirection   = matX.Transform(matY.Transform(up));
            var off = cam.Position - _pivotPoint;
            cam.Position = _pivotPoint + matX.Transform(matY.Transform(off));
        }

        private void ApplyPan(double dx, double dy)
        {
            var cam   = MainViewport.Camera;
            var look  = cam.LookDirection; look.Normalize();
            var up    = cam.UpDirection;   up.Normalize();
            var right = Vector3D.CrossProduct(look, up); right.Normalize();
            double sc = cam is HxOrtho o ? o.Width / MainViewport.ActualWidth : 0.4;
            var delta = right * (-dx * sc) + up * (dy * sc);
            cam.Position = cam.Position + delta;
            _pivotPoint  = _pivotPoint  + delta;
        }

        private void SnapToNearestCardinalView()
        {
            var cam  = MainViewport.Camera;
            _pivotPoint = new Point3D(0, 0, 0);
            var look = cam.LookDirection;
            double len = look.Length;
            if (len < 1e-6) return;
            double nx = look.X/len, ny = look.Y/len, nz = look.Z/len, dist = 500;
            double px = 0, py = 0, pz = 0;
            Vector3D nl, nu;
            if (Math.Abs(nx) >= Math.Abs(ny) && Math.Abs(nx) >= Math.Abs(nz))
                { nl = nx>0 ? new(-1,0,0) : new(1,0,0); px = nx>0?dist:-dist; nu = new(0,0,1); }
            else if (Math.Abs(ny) >= Math.Abs(nx) && Math.Abs(ny) >= Math.Abs(nz))
                { nl = ny>0 ? new(0,-1,0) : new(0,1,0); py = ny>0?dist:-dist; nu = new(0,0,1); }
            else
                { nl = nz>0 ? new(0,0,-1) : new(0,0,1); pz = nz>0?dist:-dist; nu = new(0,1,0); }
            cam.Position = new Point3D(px, py, pz);
            cam.LookDirection = nl; cam.UpDirection = nu;
        }

        // ══════════════════════════════════════
        // 工具函数
        // ══════════════════════════════════════
        private static SceneNode? FindRootNode(SceneNode node, SceneNodeGroupModel3D? model)
        {
            if (model == null) return null;
            GroupNode? root;
            try { root = model.GroupNode; } catch { return null; }
            if (root == null) return null;
            var cur = node;
            while (cur != null)
            {
                if (cur.Parent == root) return cur;
                cur = cur.Parent;
            }
            return null;
        }

        private static bool IsDescendantOf(DependencyObject child, DependencyObject ancestor)
        {
            var cur = child;
            while (cur != null)
            {
                if (ReferenceEquals(cur, ancestor)) return true;
                cur = System.Windows.Media.VisualTreeHelper.GetParent(cur);
            }
            return false;
        }

        private static IEnumerable<OutlinerNodeViewModel> FlattenOutliner(OutlinerNodeViewModel root)
        {
            yield return root;
            foreach (var c in root.Children)
                foreach (var d in FlattenOutliner(c)) yield return d;
        }
    }
}