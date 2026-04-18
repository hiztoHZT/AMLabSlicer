using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using System.Windows.Media;
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
using HxLine = HelixToolkit.SharpDX.LineGeometry3D;

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

        public static readonly DependencyProperty ObjectHighlightTransformProperty =
            DependencyProperty.Register(nameof(ObjectHighlightTransform), typeof(System.Windows.Media.Media3D.Transform3D),
                typeof(ModelPreviewView), new PropertyMetadata(System.Windows.Media.Media3D.Transform3D.Identity));

        public System.Windows.Media.Media3D.Transform3D ObjectHighlightTransform
        {
            get => (System.Windows.Media.Media3D.Transform3D)GetValue(ObjectHighlightTransformProperty);
            set => SetValue(ObjectHighlightTransformProperty, value);
        }

        public static readonly DependencyProperty ObjectHighlightGeometryProperty =
            DependencyProperty.Register(nameof(ObjectHighlightGeometry), typeof(HxLine),
                typeof(ModelPreviewView), new PropertyMetadata(null));

        public HxLine? ObjectHighlightGeometry
        {
            get => (HxLine?)GetValue(ObjectHighlightGeometryProperty);
            set => SetValue(ObjectHighlightGeometryProperty, value);
        }

        public static readonly DependencyProperty IsObjectHighlightVisibleProperty =
            DependencyProperty.Register(nameof(IsObjectHighlightVisible), typeof(bool),
                typeof(ModelPreviewView), new PropertyMetadata(false));

        public bool IsObjectHighlightVisible
        {
            get => (bool)GetValue(IsObjectHighlightVisibleProperty);
            set => SetValue(IsObjectHighlightVisibleProperty, value);
        }

        public static readonly DependencyProperty FaceWireframeGeometryProperty =
            DependencyProperty.Register(nameof(FaceWireframeGeometry), typeof(HxLine),
                typeof(ModelPreviewView), new PropertyMetadata(null));

        public HxLine? FaceWireframeGeometry
        {
            get => (HxLine?)GetValue(FaceWireframeGeometryProperty);
            set => SetValue(FaceWireframeGeometryProperty, value);
        }

        public static readonly DependencyProperty FaceCenterDotGeometryProperty =
            DependencyProperty.Register(nameof(FaceCenterDotGeometry), typeof(HxLine),
                typeof(ModelPreviewView), new PropertyMetadata(null));

        public HxLine? FaceCenterDotGeometry
        {
            get => (HxLine?)GetValue(FaceCenterDotGeometryProperty);
            set => SetValue(FaceCenterDotGeometryProperty, value);
        }

        public static readonly DependencyProperty IsFaceCenterDotVisibleProperty =
            DependencyProperty.Register(nameof(IsFaceCenterDotVisible), typeof(bool),
                typeof(ModelPreviewView), new PropertyMetadata(false));

        public bool IsFaceCenterDotVisible
        {
            get => (bool)GetValue(IsFaceCenterDotVisibleProperty);
            set => SetValue(IsFaceCenterDotVisibleProperty, value);
        }

        public static readonly DependencyProperty FaceSelectionEdgeGeometryProperty =
            DependencyProperty.Register(nameof(FaceSelectionEdgeGeometry), typeof(HxLine),
                typeof(ModelPreviewView), new PropertyMetadata(null));

        public HxLine? FaceSelectionEdgeGeometry
        {
            get => (HxLine?)GetValue(FaceSelectionEdgeGeometryProperty);
            set => SetValue(FaceSelectionEdgeGeometryProperty, value);
        }

        public static readonly DependencyProperty FaceSelectionGeometryProperty =
            DependencyProperty.Register(nameof(FaceSelectionGeometry), typeof(HxMesh),
                typeof(ModelPreviewView), new PropertyMetadata(null));

        public HxMesh? FaceSelectionGeometry
        {
            get => (HxMesh?)GetValue(FaceSelectionGeometryProperty);
            set => SetValue(FaceSelectionGeometryProperty, value);
        }

        public static readonly DependencyProperty IsFaceOverlayVisibleProperty =
            DependencyProperty.Register(nameof(IsFaceOverlayVisible), typeof(bool),
                typeof(ModelPreviewView), new PropertyMetadata(false));

        public bool IsFaceOverlayVisible
        {
            get => (bool)GetValue(IsFaceOverlayVisibleProperty);
            set => SetValue(IsFaceOverlayVisibleProperty, value);
        }

        public static readonly DependencyProperty IsFaceSelectionVisibleProperty =
            DependencyProperty.Register(nameof(IsFaceSelectionVisible), typeof(bool),
                typeof(ModelPreviewView), new PropertyMetadata(false));

        public bool IsFaceSelectionVisible
        {
            get => (bool)GetValue(IsFaceSelectionVisibleProperty);
            set => SetValue(IsFaceSelectionVisibleProperty, value);
        }

        public static readonly DependencyProperty ModalConstraintGeometryProperty =
            DependencyProperty.Register(nameof(ModalConstraintGeometry), typeof(HxLine),
                typeof(ModelPreviewView), new PropertyMetadata(null));

        public HxLine? ModalConstraintGeometry
        {
            get => (HxLine?)GetValue(ModalConstraintGeometryProperty);
            set => SetValue(ModalConstraintGeometryProperty, value);
        }

        public static readonly DependencyProperty ModalConstraintColorProperty =
            DependencyProperty.Register(nameof(ModalConstraintColor), typeof(System.Windows.Media.Color),
                typeof(ModelPreviewView), new PropertyMetadata(System.Windows.Media.Colors.Transparent));

        public System.Windows.Media.Color ModalConstraintColor
        {
            get => (System.Windows.Media.Color)GetValue(ModalConstraintColorProperty);
            set => SetValue(ModalConstraintColorProperty, value);
        }

        public static readonly DependencyProperty ModeInfoTextProperty =
            DependencyProperty.Register(nameof(ModeInfoText), typeof(string),
                typeof(ModelPreviewView), new PropertyMetadata("模式：物体模式"));

        public string ModeInfoText
        {
            get => (string)GetValue(ModeInfoTextProperty);
            set => SetValue(ModeInfoTextProperty, value);
        }

        public static readonly DependencyProperty SelectionInfoTextProperty =
            DependencyProperty.Register(nameof(SelectionInfoText), typeof(string),
                typeof(ModelPreviewView), new PropertyMetadata(string.Empty));

        public string SelectionInfoText
        {
            get => (string)GetValue(SelectionInfoTextProperty);
            set => SetValue(SelectionInfoTextProperty, value);
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


            // 选中新物体后，刷新输入栏到新物体的当前值（若输入栏当前不可见则忽略）
            if (hasNode && v.TransformInputBar.Visibility == Visibility.Visible)
                v.PopulateInputBar();

            v.RefreshObjectHighlight();
            v.UpdateStatusInfo();
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

        // ── Gizmo 位置跟踪 ──────────────────────────
        private bool      _gizmoUpdating;              // 防止 SceneNode 矩阵赋值触发反向回调
        private Matrix4x4 _gizmoLastMatrix = Matrix4x4.Identity; // 上次 GizmoAnchor SceneNode 矩阵
        private bool      _inGizmoDrag;                // 是否正在拖拽 Gizmo


        // ── Blender-style 模态状态 ──
        private bool      _isModalTransformActive = false;
        private string    _modalMode = "";           // "G", "R", "S"
        private string    _modalAxis = "";           // "", "X", "Y", "Z"
        private string    _modalInputBuffer = "";    // 用户敲击的数字缓冲区 "10", "-5.5"
        private Point     _modalInitialMousePos;     // 进入模态时的鼠标位置
        private Matrix4x4 _modalInitialMatrix;       // 进入模态前的物体初始矩阵快照
        private Vector3   _modalPivotCenter;         // 进入模态前的固定几何中心（包围盒中心）
        private Vector3   _modalInitialMouseWorld;   // 进入模态时鼠标在中心平面的世界投影

        // ── UI 工具栏原变换状态 ──
        /// <summary>"G"/"R"/"S"，空字符串代表无活跃变换</summary>
        private string   _activeTransformKey = string.Empty;
        /// <summary>进入 ActivateTransform 时的节点矩阵快照</summary>
        private Matrix4x4 _snapMatrix = Matrix4x4.Identity;
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
        private bool                   _faceGroupDirty;
        private bool                   _isFaceBrushing;
        private bool                   _isFaceRangeSelecting;
        private bool                   _isLassoSelecting;
        private Point                  _lastFaceBrushPos;
        private Point                  _rangeStartPos;
        private Point                  _rangeCurrentPos;
        private readonly List<Point>   _lassoPoints = new();
        private double                 _brushRadiusPx = 14.0;
        private bool                   _linkedSelectObjectMode;
        private const bool             StrictCenterPickInXRay = true;
        private const float            CenterPickRatio = 0.14f;

        private enum DragMode { None, Rotate, Pan }

        // ══════════════════════════════════════
        // 初始化
        // ══════════════════════════════════════
        public ModelPreviewView()
        {
            InitializeComponent();
            MainViewport.EffectsManager = new DefaultEffectsManager();
            SetPerspectiveCamera();
            UpdateStatusInfo();
            Focusable = true;
            PreviewKeyDown     += OnPreviewKeyDown;
            DataContextChanged += OnDataContextChanged;

            InitializeViewCube2D();
        }

        /// <summary>
        /// 将 GizmoAnchor.SceneNode 的当前矩阵与 _gizmoLastMatrix 的增量 delta 应用到 SelectedNode。
        /// 用于 Gizmo 拖拽过程中的实时同步（每次 MouseMove 增量调用）。
        /// </summary>
        private void SyncGizmoToSelectedNode()
        {
            if (_gizmoUpdating) return;
            if (SelectedNode == null) return;
            if (GizmoAnchor.SceneNode is not { } anchorNode) return;

            Matrix4x4 current = anchorNode.ModelMatrix;
            if (!Matrix4x4.Invert(_gizmoLastMatrix, out var lastInv)) return;
            Matrix4x4 delta = lastInv * current;
            if (delta == Matrix4x4.Identity) return;

            _gizmoUpdating = true;
            SelectedNode.ModelMatrix = delta * SelectedNode.ModelMatrix;
            _gizmoLastMatrix = current; // 增量更新，下次只计算新增的 delta
            _gizmoUpdating = false;
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
                RefreshToolbarEnabled();
                UpdateStatusInfo();
            }
        }

        // ══════════════════════════════════════
        // 工具栏启用/激活态管理
        // ══════════════════════════════════════
        /// <summary>有选中物体时启用需要选中才能操作的按钮</summary>
        private void RefreshToolbarEnabled()
        {
            bool has = SelectedNode != null;
            foreach (var b in new[] { BtnMove, BtnRotate, BtnScale, BtnFace, BtnSplit, BtnDelete, BtnMode })
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
                if (_isXRay) ToggleXRay(false);
                _selectedFaces.Clear();
                _halfEdge = null;
                _editingMeshNode = null;
                _editingFaceGroup = null;
                _faceGroupDirty = false;
                _isFaceBrushing = false;
                _isFaceRangeSelecting = false;
                _isLassoSelecting = false;
                IsManipulatorVisible = SelectedNode != null;
                Cursor = Cursors.Arrow;
                HideSelectionOverlays();
                ClearFaceHighlight();
                ClearFaceOverlay();
                RefreshToolbarEnabled();
                RefreshObjectHighlight();
            }
            else
            {
                IsManipulatorVisible = false;
                DeactivateTransform(commit: false);
                _selectedFaces.Clear();
                _editingFaceGroup = null;
                _faceGroupDirty = false;
                _isFaceBrushing = false;
                _isFaceRangeSelecting = false;
                _isLassoSelecting = false;
                HideSelectionOverlays();

                _editingMeshNode = SelectedNode switch
                {
                    MeshNode mn => mn,
                    // pivot 外层通常是 GroupNode，真正的 MeshNode 在其子树中，因此要遍历整个后代。
                    SceneNode sn => sn.Traverse().OfType<MeshNode>().FirstOrDefault(),
                    _ => null
                };

                if (_editingMeshNode?.Geometry is HxMesh)
                {
                    BuildTopologyAsync(_editingMeshNode);
                    RefreshFaceOverlay();
                }
                else
                {
                    var vm = GetVM();
                    if (vm != null) vm.ViewportMode = ViewportMode.ObjectMode;
                    return;
                }

                SetFaceTool("Q");
            }

            UpdateStatusInfo();
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

            // ── Blender 模态交互接管体系 ──
            if (_isModalTransformActive)
            {
                HandleModalTransformKey(e);
                if (!e.Handled) e.Handled = true; // 吞噬该状态下的所有残余按键
                return;  
            }

            // Tab 切换模式（不打断 TextBox 内部切焦点）
            if (e.Key == Key.Tab && e.OriginalSource is not TextBox)
            {
                var vmTab = GetVM();
                if (vmTab != null && (vmTab.IsFaceMode || SelectedNode != null))
                    vmTab.ToggleViewportModeCommand.Execute(null);
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
                case Key.G:      EnterModalTransform("G"); e.Handled = true; break;
                case Key.R:      EnterModalTransform("R"); e.Handled = true; break;
                case Key.S:      EnterModalTransform("S"); e.Handled = true; break;
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
                case "Tab":
                {
                    var vm = GetVM();
                    if (vm != null && (vm.IsFaceMode || SelectedNode != null))
                        vm.ToggleViewportModeCommand.Execute(null);
                    break;
                }
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

        private void BrushSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _brushRadiusPx = Math.Clamp(e.NewValue, 4.0, 60.0);
            if (BrushSizeText != null) BrushSizeText.Text = ((int)Math.Round(_brushRadiusPx)).ToString();
            if (_isFaceBrushing || _faceSelTool == "E")
                UpdateBrushPreview(_lastFaceBrushPos);
        }

        private void LinkedModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _linkedSelectObjectMode = LinkedModeCombo.SelectedIndex == 1;
        }

        // ══════════════════════════════════════
        // Blender 风格模态变换逻辑 (G/R/S)
        // ══════════════════════════════════════
        private void EnterModalTransform(string mode)
        {
            if (SelectedNode == null) return;
            // 进入模态前，若已有普通 UI 激活状态，先退出
            if (!string.IsNullOrEmpty(_activeTransformKey))
                DeactivateTransform(commit: true);

            _isModalTransformActive = true;
            _modalMode = mode;
            _modalAxis = "";
            _modalInputBuffer = "";
            _modalInitialMatrix = SelectedNode.ModelMatrix;
            
            _modalPivotCenter = new Vector3(0, 0, 0);
            if (TryComputeWorldAabb(SelectedNode, out var cmin, out var cmax))
                _modalPivotCenter = (cmin + cmax) * 0.5f;
                
            _modalInitialMousePos = Mouse.GetPosition(MainViewport);
            _modalInitialMouseWorld = GetMouseUnprojectedZPlane(_modalInitialMousePos, _modalPivotCenter.Z);
            
            // 改为十字星标
            Mouse.OverrideCursor = Cursors.Cross;
            MainViewport.CaptureMouse();
            
            UpdateModalStatusText();
            UpdateOverlayCanvas();
        }

        private void HandleModalTransformKey(KeyEventArgs e)
        {
            // Escape: 取消
            if (e.Key == Key.Escape)
            {
                ExitModalTransform(commit: false);
                e.Handled = true;
                return;
            }
            
            // Enter 或 Space: 确认
            if (e.Key == Key.Enter || e.Key == Key.Space)
            {
                ExitModalTransform(commit: true);
                e.Handled = true;
                return;
            }
            
            // 轴约束 X/Y/Z
            if (e.Key == Key.X || e.Key == Key.Y || e.Key == Key.Z)
            {
                if (_modalMode == "G" && e.Key == Key.Z) return; // 规则 1: 禁止 GZ 操作
                
                string axis = e.Key.ToString();
                // 连按两次切换约束/取消约束？这里：如果相同则是取消约束
                _modalAxis = _modalAxis == axis ? "" : axis;
                UpdateModalTransform();
                UpdateModalStatusText();
                UpdateOverlayCanvas();
                e.Handled = true;
                return;
            }

            // 处理数字和符号
            HandleModalBufferInput(e);
        }

        private void HandleModalBufferInput(KeyEventArgs e)
        {
            bool changed = false;
            // 屏蔽字母干扰
            if (e.Key >= Key.A && e.Key <= Key.Z) return; 

            if (e.Key == Key.Back || e.Key == Key.Delete)
            {
                if (_modalInputBuffer.Length > 0)
                {
                    _modalInputBuffer = _modalInputBuffer.Substring(0, _modalInputBuffer.Length - 1);
                    changed = true;
                }
            }
            else if (e.Key == Key.OemMinus || e.Key == Key.Subtract)
            {
                if (_modalInputBuffer.StartsWith("-")) _modalInputBuffer = _modalInputBuffer.Substring(1);
                else _modalInputBuffer = "-" + _modalInputBuffer;
                changed = true;
            }
            else if (e.Key == Key.OemPeriod || e.Key == Key.Decimal)
            {
                if (!_modalInputBuffer.Contains('.')) { _modalInputBuffer += "."; changed = true; }
            }
            else
            {
                // D0-D9
                if (e.Key >= Key.D0 && e.Key <= Key.D9)
                {
                    _modalInputBuffer += (e.Key - Key.D0).ToString();
                    changed = true;
                }
                // NumPad0-NumPad9
                else if (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9)
                {
                    _modalInputBuffer += (e.Key - Key.NumPad0).ToString();
                    changed = true;
                }
            }

            if (changed)
            {
                UpdateModalTransform();
                UpdateModalStatusText();
                e.Handled = true;
            }
        }
        
        private Vector3 GetMouseUnprojectedZPlane(Point mousePos, float targetZ)
        {
            var ray = MainViewport.UnProject(mousePos);
            if (Math.Abs(ray.Direction.Z) < 1e-6f) return Vector3.Zero;
            float t = (targetZ - ray.Position.Z) / ray.Direction.Z;
            return ray.Position + t * ray.Direction;
        }

        private void UpdateModalTransform()
        {
            if (SelectedNode == null) return;
            
            double value = 0;
            bool hasValidInput = false;
            
            if (!string.IsNullOrEmpty(_modalInputBuffer) && _modalInputBuffer != "-")
            {
                if (double.TryParse(_modalInputBuffer, out double parsed))
                {
                    value = parsed;
                    hasValidInput = true;
                }
            }
            
            // 如果没有有效的数字输入，则使用鼠标相对位移！
            Vector3 worldDelta = Vector3.Zero;
            var curPos = Mouse.GetPosition(MainViewport);

            if (!hasValidInput)
            {
                var curWorld = GetMouseUnprojectedZPlane(curPos, _modalPivotCenter.Z);
                worldDelta = curWorld - _modalInitialMouseWorld;
                
                double dx = curPos.X - _modalInitialMousePos.X;
                double dy = curPos.Y - _modalInitialMousePos.Y;
                
                if (_modalMode == "G")
                {
                    if (_modalAxis == "X") value = worldDelta.X;
                    else if (_modalAxis == "Y") value = worldDelta.Y;
                    else if (_modalAxis == "Z") value = 0; // 禁止 Z 使用
                }
                else if (_modalMode == "R")
                {
                    value = (dx - dy) * 0.5; // deg per pixel (用于带约束的旋转)
                }
                else if (_modalMode == "S")
                {
                    value = 1.0 + (dx - dy) * 0.005; 
                }
            }

            // ============ 应用变换！============
            Matrix4x4 newMat = _modalInitialMatrix;
            Vector3 center = _modalPivotCenter;
            
            if (_modalMode == "G")
            {
                float mx = 0, my = 0, mz = 0;
                if (_modalAxis == "X") mx = (float)value;
                else if (_modalAxis == "Y") my = (float)value;
                else if (_modalAxis == "Z") mz = (float)value;
                else 
                {
                    // 无约束时：
                    if (hasValidInput) { mx = (float)value; my = (float)value; }
                    else { mx = worldDelta.X; my = worldDelta.Y; } // 映射到真实 XYZ 平面的平移
                }
                
                newMat.Translation = _modalInitialMatrix.Translation + new Vector3(mx, my, mz);
            }
            else if (_modalMode == "R")
            {
                 float angleRad = (float)(value * Math.PI / 180.0);
                 Matrix4x4 rot = Matrix4x4.Identity;
                 
                 if (_modalAxis == "X") rot = Matrix4x4.CreateRotationX(angleRad);
                 else if (_modalAxis == "Y") rot = Matrix4x4.CreateRotationY(angleRad);
                 else rot = Matrix4x4.CreateRotationZ(angleRad); // 默认数值或默认角
                 
                 newMat = _modalInitialMatrix * Matrix4x4.CreateTranslation(-center) * rot * Matrix4x4.CreateTranslation(center);
            }
            else if (_modalMode == "S")
            {
                 // 缩放
                 float sval = (float)value;
                 if (sval < 0.001f && sval > -0.001f) sval = 0.001f; // 防止缩放为 0 导致逆矩阵失效
                 
                 float sx = 1, sy = 1, sz = 1;
                 if (_modalAxis == "X") sx = sval;
                 else if (_modalAxis == "Y") sy = sval;
                 else if (_modalAxis == "Z") sz = sval;
                 else { sx = sy = sz = sval; }

                 newMat = _modalInitialMatrix * Matrix4x4.CreateTranslation(-center) * Matrix4x4.CreateScale(sx, sy, sz) * Matrix4x4.CreateTranslation(center);
            }

            SelectedNode.ModelMatrix = newMat;
            
            UpdateObjectHighlightTransform(_modalInitialMatrix, newMat, SelectedNode);
        }
        
        private void UpdateModalStatusText()
        {
            // 在原输入栏上显示
            TransformInputBar.Visibility = Visibility.Visible;
            string axisStr = string.IsNullOrEmpty(_modalAxis) ? "(自由)" : _modalAxis;
            string dispStr = $"{_modalMode} {axisStr}: {_modalInputBuffer}";
            TransformLabel.Text = dispStr;
            // 隐藏实际的输入框，仅保留标签
            LblX.Visibility = Visibility.Collapsed; TxfX.Visibility = Visibility.Collapsed;
            LblY.Visibility = Visibility.Collapsed; TxfY.Visibility = Visibility.Collapsed;
            LblZ.Visibility = Visibility.Collapsed; TxfZ.Visibility = Visibility.Collapsed;
        }
        
        private void ExitModalTransform(bool commit)
        {
            _isModalTransformActive = false;
            Mouse.OverrideCursor = null;
            MainViewport.ReleaseMouseCapture();
            
            if (!commit)
            {
                SelectedNode.ModelMatrix = _modalInitialMatrix;
            }
            else
            {
                if (_modalInitialMatrix != SelectedNode.ModelMatrix)
                {
                    if (_modalMode == "G" || _modalMode == "S" || _modalMode == "R")
                    {
                        // 约束移动后检查贴地
                        ApplyZFloor(SelectedNode); 
                    }
                    CommandDispatcher.Push(new TransformCommand(SelectedNode, _modalInitialMatrix, SelectedNode.ModelMatrix, $"模态 {_modalMode} 变换"));
                }
            }
            
            // 清理状态
            ModalConstraintGeometry = null;
            TransformInputBar.Visibility = Visibility.Collapsed;
            // 恢复 UI 输入栏可见状态为正常
            LblX.Visibility = Visibility.Visible; TxfX.Visibility = Visibility.Visible;
            LblY.Visibility = Visibility.Visible; TxfY.Visibility = Visibility.Visible;
            // Z 轴可见性在打开 ActivateTransform(...) 时重置
            RefreshObjectHighlight();
        }

        private void UpdateOverlayCanvas()
        {
            if (!_isModalTransformActive || string.IsNullOrEmpty(_modalAxis) || SelectedNode == null) 
            {
                ModalConstraintGeometry = null;
                return;
            }

            // 绘制横贯物体的 3D 辅助线，使用固定的进入模态时的几何中心
            Vector3 center = _modalPivotCenter;

            // 构造长线端点（±2000mm）
            float length = 2000f;
            Vector3 p1 = center, p2 = center;
            System.Windows.Media.Color color = System.Windows.Media.Colors.White;

            if (_modalAxis == "X") 
            { 
               p1.X -= length; p2.X += length; 
               color = System.Windows.Media.Colors.Red; 
            }
            else if (_modalAxis == "Y") 
            { 
               p1.Y -= length; p2.Y += length; 
               color = System.Windows.Media.Colors.LimeGreen; 
            }
            else if (_modalAxis == "Z") 
            { 
               p1.Z -= length; p2.Z += length; 
               color = System.Windows.Media.Colors.DodgerBlue; 
            }

            var builder = new LineBuilder();
            builder.AddLine(p1, p2);
            
            ModalConstraintGeometry = builder.ToLineGeometry3D();
            ModalConstraintColor = color;
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

        // <summary>把当前 SelectedNode 的值填入输入框（不触发回调）</summary>
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
            UpdateObjectHighlightTransform(_snapMatrix, SelectedNode.ModelMatrix, SelectedNode);
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
                    // 无论什么模式，修改完成确认后均对齐地面
                    ApplyZFloor(SelectedNode);
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
            RefreshObjectHighlight();
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

        /// <summary>
        /// 仅更新蓝色包围盒线框，不重置 GizmoAnchor.Transform。
        /// 在 Gizmo 拖拽过程中的 MouseMove 里调用，避免复位 GizmoAnchor 干扰正在进行的拖拽。
        /// </summary>
        private void RefreshBoundingBoxOnly()
        {
            if (SelectedNode == null) return;
            if (!TryComputeWorldAabb(SelectedNode, out var min, out var max, fast: true))
            {
                ObjectHighlightGeometry = null;
                ObjectHighlightTransform = System.Windows.Media.Media3D.Transform3D.Identity;
                IsObjectHighlightVisible = false;
                return;
            }

            ObjectHighlightTransform = System.Windows.Media.Media3D.Transform3D.Identity;

            var b = new LineBuilder();
            var p000 = new Vector3(min.X, min.Y, min.Z); var p001 = new Vector3(min.X, min.Y, max.Z);
            var p010 = new Vector3(min.X, max.Y, min.Z); var p011 = new Vector3(min.X, max.Y, max.Z);
            var p100 = new Vector3(max.X, min.Y, min.Z); var p101 = new Vector3(max.X, min.Y, max.Z);
            var p110 = new Vector3(max.X, max.Y, min.Z); var p111 = new Vector3(max.X, max.Y, max.Z);

            b.AddLine(p000, p001); b.AddLine(p001, p011); b.AddLine(p011, p010); b.AddLine(p010, p000);
            b.AddLine(p100, p101); b.AddLine(p101, p111); b.AddLine(p111, p110); b.AddLine(p110, p100);
            b.AddLine(p000, p100); b.AddLine(p001, p101); b.AddLine(p010, p110); b.AddLine(p011, p111);

            ObjectHighlightGeometry = b.ToLineGeometry3D();
            IsObjectHighlightVisible = true;
        }

        private void UpdateObjectHighlightTransform(Matrix4x4 initialMatrix, Matrix4x4 newMatrix, SceneNode node)
        {
            var parentWorld = node.Parent != null ? GetWorldModelMatrix(node.Parent) : Matrix4x4.Identity;
            Matrix4x4.Invert(parentWorld, out var invParent);
            Matrix4x4.Invert(initialMatrix, out var invInitial);

            var deltaM = invParent * invInitial * newMatrix * parentWorld;
            ObjectHighlightTransform = new System.Windows.Media.Media3D.MatrixTransform3D(
                new System.Windows.Media.Media3D.Matrix3D(
                    deltaM.M11, deltaM.M12, deltaM.M13, deltaM.M14,
                    deltaM.M21, deltaM.M22, deltaM.M23, deltaM.M24,
                    deltaM.M31, deltaM.M32, deltaM.M33, deltaM.M34,
                    deltaM.M41, deltaM.M42, deltaM.M43, deltaM.M44));
        }

        private void RefreshObjectHighlight(bool fast = false)
        {
            var vm = GetVM();
            if (SelectedNode == null || vm?.IsObjectMode != true)
            {
                ObjectHighlightGeometry = null;
                ObjectHighlightTransform = System.Windows.Media.Media3D.Transform3D.Identity;
                IsObjectHighlightVisible = false;
                IsManipulatorVisible = false;
                _gizmoLastMatrix = Matrix4x4.Identity;
                return;
            }

            if (!TryComputeWorldAabb(SelectedNode, out var min, out var max, fast: fast))
            {
                ObjectHighlightGeometry = null;
                ObjectHighlightTransform = System.Windows.Media.Media3D.Transform3D.Identity;
                IsObjectHighlightVisible = false;
                return;
            }

            ObjectHighlightTransform = System.Windows.Media.Media3D.Transform3D.Identity;

            var b = new LineBuilder();
            var p000 = new Vector3(min.X, min.Y, min.Z);
            var p001 = new Vector3(min.X, min.Y, max.Z);
            var p010 = new Vector3(min.X, max.Y, min.Z);
            var p011 = new Vector3(min.X, max.Y, max.Z);
            var p100 = new Vector3(max.X, min.Y, min.Z);
            var p101 = new Vector3(max.X, min.Y, max.Z);
            var p110 = new Vector3(max.X, max.Y, min.Z);
            var p111 = new Vector3(max.X, max.Y, max.Z);

            b.AddLine(p000, p001); b.AddLine(p001, p011); b.AddLine(p011, p010); b.AddLine(p010, p000);
            b.AddLine(p100, p101); b.AddLine(p101, p111); b.AddLine(p111, p110); b.AddLine(p110, p100);
            b.AddLine(p000, p100); b.AddLine(p001, p101); b.AddLine(p010, p110); b.AddLine(p011, p111);

            ObjectHighlightGeometry = b.ToLineGeometry3D();
            IsObjectHighlightVisible = true;

            // ── 将 GizmoAnchor 定位到 AABB 几何中心，TransformManipulator3D 会自动同步 ──
            var center = (min + max) * 0.5f;
            float w = max.X - min.X, h = max.Y - min.Y, d = max.Z - min.Z;
            float maxDim = MathF.Max(w, MathF.Max(h, d));
            double gizmoScale = Math.Clamp(maxDim / 4.0, 10.0, 80.0);

            var tx = new System.Windows.Media.Media3D.MatrixTransform3D(
                new System.Windows.Media.Media3D.Matrix3D(
                    1, 0, 0, 0,
                    0, 1, 0, 0,
                    0, 0, 1, 0,
                    center.X, center.Y, center.Z, 1));

            _gizmoUpdating = true;
            GizmoAnchor.Transform = tx;
            _gizmoUpdating = false;

            // 记录 GizmoAnchor SceneNode 当前矩阵，供松手后 delta 计算
            if (GizmoAnchor.SceneNode is { } an)
                _gizmoLastMatrix = an.ModelMatrix;

            ObjectManipulator.SizeScale = gizmoScale;
        }


        private void RefreshFaceOverlay()
        {
            if (_editingMeshNode?.Geometry is not HxMesh geo)
            {
                ClearFaceOverlay();
                return;
            }

            var positions = geo.Positions;
            var indices = geo.Indices;
            if (positions == null || indices == null)
            {
                ClearFaceOverlay();
                return;
            }

            var world = GetWorldModelMatrix(_editingMeshNode);
            var edgeBuilder = new LineBuilder();
            var dotBuilder = new LineBuilder();
            float dotSize = 0.12f;
            int dotStride = indices.Count > 120000 ? 6 : 1;
            int triId = 0;
            for (int i = 0; i + 2 < indices.Count; i += 3)
            {
                int i0 = indices[i];
                int i1 = indices[i + 1];
                int i2 = indices[i + 2];
                if (i0 < 0 || i1 < 0 || i2 < 0 || i0 >= positions.Count || i1 >= positions.Count || i2 >= positions.Count) continue;

                var p0 = Vector3.Transform(positions[i0], world);
                var p1 = Vector3.Transform(positions[i1], world);
                var p2 = Vector3.Transform(positions[i2], world);

                edgeBuilder.AddLine(p0, p1);
                edgeBuilder.AddLine(p1, p2);
                edgeBuilder.AddLine(p2, p0);

                if (_isXRay && (triId % dotStride == 0))
                {
                    var c = (p0 + p1 + p2) / 3f;
                    dotBuilder.AddLine(new Vector3(c.X - dotSize, c.Y, c.Z), new Vector3(c.X + dotSize, c.Y, c.Z));
                    dotBuilder.AddLine(new Vector3(c.X, c.Y - dotSize, c.Z), new Vector3(c.X, c.Y + dotSize, c.Z));
                }
                triId++;
            }

            FaceWireframeGeometry = edgeBuilder.ToLineGeometry3D();
            FaceCenterDotGeometry = _isXRay ? dotBuilder.ToLineGeometry3D() : null;
            IsFaceCenterDotVisible = _isXRay;
            IsFaceOverlayVisible = true;
        }

        private void ClearFaceOverlay()
        {
            FaceWireframeGeometry = null;
            FaceCenterDotGeometry = null;
            FaceSelectionGeometry = null;
            FaceSelectionEdgeGeometry = null;
            IsFaceOverlayVisible = false;
            IsFaceCenterDotVisible = false;
            IsFaceSelectionVisible = false;
        }

        private void UpdateStatusInfo()
        {
            var vm = GetVM();
            bool faceMode = vm?.IsFaceMode == true;
            ModeInfoText = faceMode ? "模式：面编辑模式" : "模式：物体模式";

            if (!faceMode)
            {
                SelectionInfoText = SelectedNode == null ? string.Empty : $"选择：{SelectedNode.Name}";
                return;
            }

            string objName = SelectedNode?.Name
                ?? _editingMeshNode?.Name
                ?? string.Empty;

            if (string.IsNullOrWhiteSpace(objName))
            {
                SelectionInfoText = string.Empty;
                return;
            }

            if (_selectedFaces.Count == 0)
            {
                SelectionInfoText = $"选择：{objName}";
                return;
            }

            if (_editingFaceGroup != null && !_faceGroupDirty)
            {
                SelectionInfoText = $"选择：{objName}-{_editingFaceGroup.Name}";
                return;
            }

            SelectionInfoText = $"选择：{objName}-待保存的面组";
        }

        // ══════════════════════════════════════
        // Z 贴地约束
        // ══════════════════════════════════════
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, Vector3[]> _geometryVertexCache = new();

        /// <summary>
        /// 避免 BoundsWithTransform 在某些状态下返回非有限值（NaN/Infinity）导致推飞模型。
        /// 开户 fast: true 模式时，仅对网格自身原本的 8 个本地包围盒角点进行矩阵变换并做极值提取(极快且适合渲染高亮边框，但多重旋转会虚胖)。
        /// fast: false 时，通过缓存顶点极速遍历计算精确的 World AABB(适用贴地)。
        /// </summary>
        private static bool TryComputeWorldAabb(SceneNode node, out Vector3 min, out Vector3 max, bool fast = false)
        {
            min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            bool any = false;

            if (node == null) return false;

            try
            {
                foreach (var n in node.Traverse())
                {
                    if (n is MeshNode mn && mn.Geometry != null)
                    {
                        var worldM = GetWorldModelMatrix(mn);
                        
                        if (fast)
                        {
                            var bound = mn.Geometry.Bound;
                            Vector3[] corners = new Vector3[] {
                                new Vector3(bound.Minimum.X, bound.Minimum.Y, bound.Minimum.Z),
                                new Vector3(bound.Minimum.X, bound.Minimum.Y, bound.Maximum.Z),
                                new Vector3(bound.Minimum.X, bound.Maximum.Y, bound.Minimum.Z),
                                new Vector3(bound.Minimum.X, bound.Maximum.Y, bound.Maximum.Z),
                                new Vector3(bound.Maximum.X, bound.Minimum.Y, bound.Minimum.Z),
                                new Vector3(bound.Maximum.X, bound.Minimum.Y, bound.Maximum.Z),
                                new Vector3(bound.Maximum.X, bound.Maximum.Y, bound.Minimum.Z),
                                new Vector3(bound.Maximum.X, bound.Maximum.Y, bound.Maximum.Z)
                            };
                            for (int i = 0; i < 8; i++)
                            {
                                var wp = Vector3.Transform(corners[i], worldM);
                                if (wp.X < min.X) min.X = wp.X; else if (wp.X > max.X) max.X = wp.X;
                                if (wp.Y < min.Y) min.Y = wp.Y; else if (wp.Y > max.Y) max.Y = wp.Y;
                                if (wp.Z < min.Z) min.Z = wp.Z; else if (wp.Z > max.Z) max.Z = wp.Z;
                            }
                            any = true;
                        }
                        else
                        {
                            if (!_geometryVertexCache.TryGetValue(mn.Geometry, out Vector3[]? pts) || pts == null)
                            {
                                pts = mn.Geometry.Positions?.ToArray() ?? Array.Empty<Vector3>();
                                _geometryVertexCache.Add(mn.Geometry, pts);
                            }

                            int count = pts.Length;
                            for (int i = 0; i < count; i++)
                            {
                                var wp = Vector3.Transform(pts[i], worldM);
                                if (wp.X < min.X) min.X = wp.X; else if (wp.X > max.X) max.X = wp.X;
                                if (wp.Y < min.Y) min.Y = wp.Y; else if (wp.Y > max.Y) max.Y = wp.Y;
                                if (wp.Z < min.Z) min.Z = wp.Z; else if (wp.Z > max.Z) max.Z = wp.Z;
                            }
                            if (count > 0) any = true;
                        }
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
            // Vector3.Transform 使用的是“点 * 矩阵”语义，因此层级应按 local->parent 顺序累乘。
            var m = Matrix4x4.Identity;
            SceneNode? cur = node;
            while (cur != null)
            {
                m = m * cur.ModelMatrix;
                cur = cur.Parent;
            }

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
                // worldZ floor -> 用“后乘”在世界坐标系推移，因为矩阵是 RowMajor（node.ModelMatrix * T）
                node.ModelMatrix =
                    node.ModelMatrix *
                    Matrix4x4.CreateTranslation(0, 0, dz);
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
            RefreshObjectHighlight();
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
        private void SetFaceTool(string tool)
        {
            _faceSelTool = tool;
            _isFaceBrushing = false;
            _isFaceRangeSelecting = false;
            _isLassoSelecting = false;
            MainViewport.ReleaseMouseCapture();
            SetFaceToolActive(tool);
            BrushPanel.Visibility = tool == "E" ? Visibility.Visible : Visibility.Collapsed;
            LinkedPanel.Visibility = tool == "L" ? Visibility.Visible : Visibility.Collapsed;
            FaceRectSelection.Visibility = Visibility.Collapsed;
            FaceLassoPath.Visibility = Visibility.Collapsed;
            BrushPreviewCircle.Visibility = tool == "E" ? Visibility.Visible : Visibility.Collapsed;
            Cursor = tool == "E" ? Cursors.None : Cursors.Arrow;
            if (tool == "E")
            {
                var p = Mouse.GetPosition(MainViewport);
                _lastFaceBrushPos = p;
                UpdateBrushPreview(p);
            }
        }
        private void SelectAllFaces()
        {
            if (_halfEdge == null) return;
            for (int i = 0; i < _halfEdge.FaceCount; i++) _selectedFaces.Add(i);
            _faceGroupDirty = true;
            RefreshFaceHighlight();
        }
        private void SelectLinkedFaces()
        {
            var pos = Mouse.GetPosition(MainViewport);
            if (_halfEdge == null) return;
            int triCount = _editingMeshNode?.Geometry?.Indices?.Count / 3 ?? 0;
            if (triCount <= 0) return;
            var hits = GetMeshHitsAt(pos);
            if (hits.Count == 0) return;
            int fi = MapHitToFaceIndex(hits[0], triCount);
            if (fi < 0) return;
            if (_linkedSelectObjectMode)
            {
                _selectedFaces.Clear();
                for (int i = 0; i < triCount; i++) _selectedFaces.Add(i);
            }
            else
            {
                var comp = _halfEdge.GetConnectedComponent(fi);
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) _selectedFaces.ExceptWith(comp);
                else _selectedFaces.UnionWith(comp);
            }
            _faceGroupDirty = true;
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
                TrySetBooleanProperty(mc, "RenderBackFace", on);
                TrySetCullNone(mc, on);
            }
            RefreshFaceOverlay();
        }

        private static void TrySetBooleanProperty(object target, string propertyName, bool value)
        {
            var p = target.GetType().GetProperty(propertyName);
            if (p == null || p.PropertyType != typeof(bool) || !p.CanWrite) return;
            p.SetValue(target, value);
        }

        private static void TrySetCullNone(object target, bool on)
        {
            var p = target.GetType().GetProperty("CullMode");
            if (p == null || !p.CanWrite) return;
            var enumType = p.PropertyType;
            var wanted = on ? "None" : "Back";
            var names = Enum.GetNames(enumType);
            var name = names.FirstOrDefault(n => string.Equals(n, wanted, StringComparison.OrdinalIgnoreCase));
            if (name == null) return;
            var value = Enum.Parse(enumType, name);
            p.SetValue(target, value);
        }
        private void RefreshFaceHighlight()
        {
            if (_editingMeshNode?.Geometry is not HxMesh geo)
            {
                FaceSelectionGeometry = null;
                FaceSelectionEdgeGeometry = null;
                IsFaceSelectionVisible = false;
                UpdateStatusInfo();
                return;
            }

            var positions = geo.Positions;
            var indices = geo.Indices;
            if (positions == null || indices == null || _selectedFaces.Count == 0)
            {
                FaceSelectionGeometry = null;
                FaceSelectionEdgeGeometry = null;
                IsFaceSelectionVisible = false;
                UpdateStatusInfo();
                return;
            }

            var world = GetWorldModelMatrix(_editingMeshNode);
            var facePositions = new List<Vector3>();
            var faceIndices = new List<int>();
            var edgeBuilder = new LineBuilder();

            foreach (var faceIdx in _selectedFaces)
            {
                int baseIdx = faceIdx * 3;
                if (baseIdx + 2 >= indices.Count) continue;
                int i0 = indices[baseIdx];
                int i1 = indices[baseIdx + 1];
                int i2 = indices[baseIdx + 2];
                if (i0 < 0 || i1 < 0 || i2 < 0 || i0 >= positions.Count || i1 >= positions.Count || i2 >= positions.Count) continue;

                var p0 = Vector3.Transform(positions[i0], world);
                var p1 = Vector3.Transform(positions[i1], world);
                var p2 = Vector3.Transform(positions[i2], world);

                int baseVertex = facePositions.Count;
                facePositions.Add(p0);
                facePositions.Add(p1);
                facePositions.Add(p2);
                faceIndices.Add(baseVertex);
                faceIndices.Add(baseVertex + 1);
                faceIndices.Add(baseVertex + 2);
                edgeBuilder.AddLine(p0, p1);
                edgeBuilder.AddLine(p1, p2);
                edgeBuilder.AddLine(p2, p0);
            }

            FaceSelectionGeometry = facePositions.Count == 0
                ? null
                : new HxMesh
                {
                    Positions = new HelixToolkit.Vector3Collection(facePositions),
                    Indices = new HelixToolkit.IntCollection(faceIndices)
                };
            FaceSelectionEdgeGeometry = edgeBuilder.ToLineGeometry3D();
            IsFaceSelectionVisible = FaceSelectionGeometry != null;
            UpdateStatusInfo();
        }

        private void ClearFaceHighlight()
        {
            _selectedFaces.Clear();
            FaceSelectionGeometry = null;
            FaceSelectionEdgeGeometry = null;
            IsFaceSelectionVisible = false;
            UpdateStatusInfo();
        }
        private void SaveFaceGroup(bool over)
        {
            if (_selectedFaces.Count == 0) { MessageBox.Show("请先选择面片。"); return; }
            var vm = GetVM();
            if (vm == null || _editingMeshNode == null) return;
            if (over && _editingFaceGroup != null)
            {
                _editingFaceGroup.FaceIndices!.Clear();
                _editingFaceGroup.FaceIndices.AddRange(_selectedFaces);
                _faceGroupDirty = false;
            }
            else
            {
                // Find the top level Pivot node representing the object
                var rootObjectNode = FindRootNode(_editingMeshNode, vm.LoadedModel as SceneNodeGroupModel3D);
                if (rootObjectNode == null) return;
                
                var p = vm.OutlinerItems.FirstOrDefault(n => n.Node == rootObjectNode);
                if (p == null) return;
                
                int gi = p.Children.Count(c => c.IsFaceGroup) + 1;
                string baseName = "面片组";
                string newName = $"{baseName} {gi}";
                
                // Ensure unique name by checking existing children
                while (p.Children.Any(c => c.Name == newName))
                {
                    gi++;
                    newName = $"{baseName} {gi}";
                }
                
                var fv = new OutlinerNodeViewModel(_editingMeshNode, newName)
                    { FaceIndices = new List<int>(_selectedFaces) };
                fv.Name = newName;
                p.Children.Add(fv);
                _editingFaceGroup = fv;
                _faceGroupDirty = false;
            }
            UpdateStatusInfo();
        }

        // ══════════════════════════════════════
        // 鼠标交互
        // ══════════════════════════════════════
        private void MainViewport_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            this.Focus();

            if (_isModalTransformActive)
            {
                if (e.ChangedButton == MouseButton.Left)
                    ExitModalTransform(commit: true);
                else if (e.ChangedButton == MouseButton.Right || e.ChangedButton == MouseButton.Middle)
                    ExitModalTransform(commit: false);
                e.Handled = true;
                return;
            }

            if (e.ChangedButton == MouseButton.Left)
            {
                var pos = e.GetPosition(MainViewport);

                if (_isFacePickMode) { ExecuteBottomFacePick(pos); e.Handled = true; return; }

                // ── 优先判断是否点击了 Gizmo ──
                if (IsManipulatorVisible && IsGizmoHit(pos))
                {
                    // Gizmo 命中：设置拖拽标志，记录 undo 快照，不做选择逻辑
                    _inGizmoDrag = true;
                    if (SelectedNode != null) _snapMatrix = SelectedNode.ModelMatrix;
                    if (GizmoAnchor.SceneNode is { } an) _gizmoLastMatrix = an.ModelMatrix;
                    return; // 不设 Handled，让 WPF 事件继续传递给 Gizmo 的 Mouse3DDown
                }

                var vm = GetVM();
                if (vm?.IsFaceMode == true)
                {
                    if (_faceSelTool == "W")
                    {
                        BeginRangeSelection(pos, false);
                        return;
                    }
                    if (_faceSelTool == "R")
                    {
                        BeginRangeSelection(pos, true);
                        return;
                    }
                    if (_faceSelTool == "E")
                    {
                        _isFaceBrushing = true;
                        _lastFaceBrushPos = pos;
                        UpdateBrushPreview(pos);
                        HandleFaceClick(pos);
                        return;
                    }
                    HandleFaceClick(pos);
                    return;
                }

                // 物体模式：点选（排除 Gizmo 子树，防止点 Gizmo 时清空 SelectedNode）
                var hits = MainViewport.FindHits(pos);
                var manipRoot  = ObjectManipulator.SceneNode;
                var anchorRoot = GizmoAnchor.SceneNode;
                SceneNode? newSel      = null;
                bool       hasGizmoHit = false; // FindHits 是否命中了 Gizmo（但 IsGizmoHit 漏检）

                if (hits != null && hits.Count > 0)
                {
                    foreach (var hit in hits)
                    {
                        if (hit.ModelHit is not SceneNode sn) continue;

                        // 跳过属于 Gizmo 或 GizmoAnchor 子树的命中
                        bool isGizmoSN = (manipRoot  != null && IsInSceneNodeSubtree(sn, manipRoot)) ||
                                         (anchorRoot != null && IsInSceneNodeSubtree(sn, anchorRoot));
                        if (isGizmoSN) { hasGizmoHit = true; continue; }

                        if (sn is MeshNode || sn is GroupNode)
                        {
                            newSel = FindRootNode(sn, vm?.LoadedModel as SceneNodeGroupModel3D);
                            if (newSel != null) break;
                        }
                    }
                }

                // 如果 FindHits 命中了 Gizmo（IsGizmoHit 漏检的情况），也当作 Gizmo 拖拽处理
                if (newSel == null && hasGizmoHit && IsManipulatorVisible)
                {
                    _inGizmoDrag = true;
                    if (SelectedNode != null) _snapMatrix = SelectedNode.ModelMatrix;
                    if (GizmoAnchor.SceneNode is { } an) _gizmoLastMatrix = an.ModelMatrix;
                    return;
                }

                if (SelectedNode != newSel)
                    SelectedNode = newSel;
                return;
            }

            // 中键：相机控制
            if (e.ChangedButton == MouseButton.Middle)
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
                {
                    _altMiddleDownPos = e.GetPosition(MainViewport);
                    _isAltMiddleActive = true;
                    e.Handled = true;
                    return;
                }

                _lastMousePos    = e.GetPosition(MainViewport);
                _currentDragMode = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
                    ? DragMode.Pan : DragMode.Rotate;
                _isDragging = true;
                MainViewport.CaptureMouse();
                e.Handled = true;
            }
        }

        private Point _altMiddleDownPos;
        private bool  _isAltMiddleActive;

        private void MainViewport_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            // ── Blender 模态交互接管 ──
            if (_isModalTransformActive)
            {
                // 如果没有输入数字，则执行基于鼠标的相对位移！
                if (string.IsNullOrEmpty(_modalInputBuffer) || _modalInputBuffer == "-")
                {
                    UpdateModalTransform();
                }
                e.Handled = true;
                return;
            }

            // ── Gizmo 拖拽：实时将 GizmoAnchor 的增量 delta 同步到 SelectedNode ──
            if (_inGizmoDrag && e.LeftButton == MouseButtonState.Pressed)
            {
                SyncGizmoToSelectedNode();
                // 实时更新蓝色包围盒（不重置 Gizmo 位置，避免干扰正在进行的拖拽）
                RefreshBoundingBoxOnly();
                // 不 return：后续相机/面模式逻辑仍需运行（但 _isDragging 为 false 时不进入相机逻辑）
            }

            var vm = GetVM();
            if (vm?.IsFaceMode == true)
            {
                var fp = e.GetPosition(MainViewport);
                if (_faceSelTool == "E") UpdateBrushPreview(fp);

                if (_isFaceRangeSelecting && e.LeftButton == MouseButtonState.Pressed)
                {
                    UpdateRangeSelection(fp);
                    e.Handled = true;
                    return;
                }

                if (_isFaceBrushing && e.LeftButton == MouseButtonState.Pressed && _faceSelTool == "E")
                {
                    var bdx = fp.X - _lastFaceBrushPos.X;
                    var bdy = fp.Y - _lastFaceBrushPos.Y;
                    if (bdx * bdx + bdy * bdy >= 9)
                    {
                        _lastFaceBrushPos = fp;
                        HandleFaceClick(fp);
                    }
                    e.Handled = true;
                    return;
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
            if (e.ChangedButton == MouseButton.Left)
            {
                _isFaceBrushing = false;
                if (_isFaceRangeSelecting)
                {
                    EndRangeSelection();
                    e.Handled = true;
                    return;
                }
            }

            if (e.ChangedButton == MouseButton.Middle)
            {
                if (_isAltMiddleActive)
                {
                    _isAltMiddleActive = false;
                    var pos = e.GetPosition(MainViewport);
                    var delta = pos - _altMiddleDownPos;
                    if (delta.Length < 10)
                    {
                        // Task 3: 点击 (智能聚焦)
                        SmartZoomExtents();
                    }
                    else
                    {
                        // Task 2: 滑动正交吸附
                        SwipeToOrthographicView(delta);
                    }
                    e.Handled = true;
                    return;
                }

                _isDragging      = false;
                _currentDragMode = DragMode.None;
                MainViewport.ReleaseMouseCapture();
                e.Handled = true;
            }

            // ── Gizmo 拖拽结束：最终同步 + Z-Floor + Undo ──
            if (_inGizmoDrag && e.ChangedButton == MouseButton.Left)
            {
                _inGizmoDrag = false;
                SyncGizmoToSelectedNode(); // 最终同步一次（捕获 MouseMove 末帧可能遗漏的 delta）

                if (SelectedNode != null && string.IsNullOrEmpty(_activeTransformKey))
                {
                    var cur = SelectedNode.ModelMatrix;
                    if (cur != _snapMatrix)
                    {
                        ApplyZFloor(SelectedNode);  // 贴地：保持模型底面在 Z=0
                        CommandDispatcher.Push(new TransformCommand(SelectedNode, _snapMatrix, SelectedNode.ModelMatrix, "Gizmo 变换"));
                        _snapMatrix = SelectedNode.ModelMatrix;
                    }
                }
                RefreshObjectHighlight(); // 重置 GizmoAnchor 到新 AABB 中心
                return;
            }

            // 键盘变换（G/R/S）放手后记录 Undo
            if (e.ChangedButton == MouseButton.Left && SelectedNode != null &&
                string.IsNullOrEmpty(_activeTransformKey))
            {
                var cur = SelectedNode.ModelMatrix;
                if (cur != _snapMatrix)
                {
                    ApplyZFloor(SelectedNode);
                    CommandDispatcher.Push(new TransformCommand(SelectedNode, _snapMatrix, SelectedNode.ModelMatrix, "键盘变换"));
                    _snapMatrix = SelectedNode.ModelMatrix;
                    RefreshObjectHighlight();
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
        private void HandleFaceClick(Point pos)
        {
            if (_halfEdge == null) return;
            int triCount = _editingMeshNode?.Geometry?.Indices?.Count / 3 ?? 0;
            if (triCount <= 0) return;
            var hits = GetMeshHitsAt(pos);
            if (hits.Count == 0) return;
            bool sub = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            bool alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
            var before = _selectedFaces.Count;

            if (alt)
            {
                int fi = ResolveFaceFromHits(hits, triCount);
                if (fi < 0) return;
                var loop = _halfEdge.GetFaceLoop(fi);
                if (sub) foreach (var f in loop) _selectedFaces.Remove(f);
                else     foreach (var f in loop) _selectedFaces.Add(f);
            }
            else if (!_isXRay)
            {
                int fi = ResolveFaceFromHits(hits, triCount);
                if (fi < 0) return;
                if (sub) _selectedFaces.Remove(fi); else _selectedFaces.Add(fi);
            }
            else
            {
                if (StrictCenterPickInXRay && _faceSelTool == "Q")
                {
                    int fi = ResolveFaceFromHits(hits, triCount);
                    if (fi < 0) return;
                    if (sub) _selectedFaces.Remove(fi); else _selectedFaces.Add(fi);
                    if (_selectedFaces.Count != before) _faceGroupDirty = true;
                    RefreshFaceHighlight();
                    return;
                }
                foreach (var hit in hits)
                {
                    int fi = MapHitToFaceIndex(hit, triCount);
                    if (fi < 0) continue;
                    if (StrictCenterPickInXRay && !IsHitNearFaceCenter(hit, fi)) continue;
                    if (sub) _selectedFaces.Remove(fi); else _selectedFaces.Add(fi);
                }
            }

            if (_selectedFaces.Count != before) _faceGroupDirty = true;
            RefreshFaceHighlight();
        }

        private void BeginRangeSelection(Point startPos, bool lasso)
        {
            _isFaceRangeSelecting = true;
            _isLassoSelecting = lasso;
            _rangeStartPos = startPos;
            _rangeCurrentPos = startPos;
            _lassoPoints.Clear();
            _lassoPoints.Add(startPos);
            MainViewport.CaptureMouse();
            if (lasso)
            {
                FaceLassoPath.Visibility = Visibility.Visible;
                FaceRectSelection.Visibility = Visibility.Collapsed;
                FaceLassoPath.Points = new PointCollection(_lassoPoints);
            }
            else
            {
                FaceRectSelection.Visibility = Visibility.Visible;
                FaceLassoPath.Visibility = Visibility.Collapsed;
                UpdateRectVisual(startPos, startPos);
            }
        }

        private void UpdateRangeSelection(Point currentPos)
        {
            _rangeCurrentPos = currentPos;
            if (_isLassoSelecting)
            {
                if (_lassoPoints.Count == 0 || DistanceSquared(_lassoPoints[^1], currentPos) > 9)
                {
                    _lassoPoints.Add(currentPos);
                    FaceLassoPath.Points = new PointCollection(_lassoPoints);
                }
            }
            else
            {
                UpdateRectVisual(_rangeStartPos, currentPos);
            }
        }

        private void EndRangeSelection()
        {
            var rect = MakeRect(_rangeStartPos, _rangeCurrentPos);
            var lasso = _lassoPoints.ToList();
            bool useLasso = _isLassoSelecting && lasso.Count >= 3;

            _isFaceRangeSelecting = false;
            _isLassoSelecting = false;
            MainViewport.ReleaseMouseCapture();
            HideSelectionOverlays();

            int triCount = _editingMeshNode?.Geometry?.Indices?.Count / 3 ?? 0;
            if (triCount <= 0) return;
            var result = CollectFacesInArea(rect, useLasso ? lasso : null, triCount);
            if (result.Count == 0) return;

            bool sub = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            int before = _selectedFaces.Count;
            foreach (var fi in result)
            {
                if (sub) _selectedFaces.Remove(fi); else _selectedFaces.Add(fi);
            }
            if (_selectedFaces.Count != before) _faceGroupDirty = true;
            RefreshFaceHighlight();
        }

        private HashSet<int> CollectFacesInArea(Rect rect, List<Point>? lasso, int triCount)
        {
            var picked = new HashSet<int>();
            if (rect.Width < 2 || rect.Height < 2) return picked;
            double step = Math.Clamp(Math.Min(rect.Width, rect.Height) / 10.0, 6.0, 18.0);
            int maxSamples = 2000;
            int sampled = 0;

            for (double y = rect.Top; y <= rect.Bottom; y += step)
            {
                for (double x = rect.Left; x <= rect.Right; x += step)
                {
                    var p = new Point(x, y);
                    if (lasso != null && !IsPointInPolygon(p, lasso)) continue;
                    var hs = GetMeshHitsAt(p);
                    if (hs.Count == 0) continue;
                    if (_isXRay)
                    {
                        foreach (var h in hs)
                        {
                            int fi = MapHitToFaceIndex(h, triCount);
                            if (StrictCenterPickInXRay && fi >= 0 && !IsHitNearFaceCenter(h, fi)) continue;
                            if (fi >= 0) picked.Add(fi);
                        }
                    }
                    else
                    {
                        int fi = MapHitToFaceIndex(hs[0], triCount);
                        if (fi >= 0) picked.Add(fi);
                    }
                    sampled++;
                    if (sampled > maxSamples) return picked;
                }
            }
            return picked;
        }

        private void HideSelectionOverlays()
        {
            FaceRectSelection.Visibility = Visibility.Collapsed;
            FaceLassoPath.Visibility = Visibility.Collapsed;
            BrushPreviewCircle.Visibility = Visibility.Collapsed;
        }

        private void UpdateBrushPreview(Point pos)
        {
            _lastFaceBrushPos = pos;
            if (_faceSelTool != "E")
            {
                BrushPreviewCircle.Visibility = Visibility.Collapsed;
                return;
            }
            BrushPreviewCircle.Visibility = Visibility.Visible;
            BrushPreviewCircle.Width = _brushRadiusPx * 2;
            BrushPreviewCircle.Height = _brushRadiusPx * 2;
            Canvas.SetLeft(BrushPreviewCircle, pos.X - _brushRadiusPx);
            Canvas.SetTop(BrushPreviewCircle, pos.Y - _brushRadiusPx);
        }

        private static Rect MakeRect(Point a, Point b)
        {
            return new Rect(new Point(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)),
                            new Point(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));
        }

        private void UpdateRectVisual(Point a, Point b)
        {
            var r = MakeRect(a, b);
            Canvas.SetLeft(FaceRectSelection, r.Left);
            Canvas.SetTop(FaceRectSelection, r.Top);
            FaceRectSelection.Width = r.Width;
            FaceRectSelection.Height = r.Height;
        }

        private static bool IsPointInPolygon(Point p, List<Point> polygon)
        {
            bool inside = false;
            int j = polygon.Count - 1;
            for (int i = 0; i < polygon.Count; i++)
            {
                var pi = polygon[i];
                var pj = polygon[j];
                bool intersect = ((pi.Y > p.Y) != (pj.Y > p.Y)) &&
                                 (p.X < (pj.X - pi.X) * (p.Y - pi.Y) / ((pj.Y - pi.Y) + 1e-6) + pi.X);
                if (intersect) inside = !inside;
                j = i;
            }
            return inside;
        }

        private static double DistanceSquared(Point a, Point b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        private List<HxHit> GetMeshHitsAt(Point pos)
        {
            var raw = MainViewport.FindHits(pos);
            if (raw == null || raw.Count == 0) return new List<HxHit>();
            return raw.OfType<HxHit>()
                      .Where(IsHitOnEditingMesh)
                      .OrderBy(h => h.Distance)
                      .ToList();
        }

        private bool IsHitOnEditingMesh(HxHit hit)
        {
            if (_editingMeshNode == null) return false;
            return hit.ModelHit is SceneNode sn && ReferenceEquals(sn, _editingMeshNode);
        }

        private int MapHitToFaceIndex(HxHit hit, int faceCount)
        {
            int v = hit.IndiceStartLocation;
            if (v < 0 || faceCount <= 0) return -1;
            if (v % 3 == 0 && v / 3 < faceCount) return v / 3;
            if (v < faceCount) return v;
            int by3 = v / 3;
            return by3 >= 0 && by3 < faceCount ? by3 : -1;
        }

        private int ResolveFaceFromHits(List<HxHit> hits, int triCount)
        {
            if (_isXRay && StrictCenterPickInXRay)
            {
                foreach (var h in hits)
                {
                    int fi = MapHitToFaceIndex(h, triCount);
                    if (fi < 0) continue;
                    if (IsHitNearFaceCenter(h, fi)) return fi;
                }
                return -1;
            }
            return hits.Count > 0 ? MapHitToFaceIndex(hits[0], triCount) : -1;
        }

        private bool IsHitNearFaceCenter(HxHit hit, int fi)
        {
            if (_editingMeshNode?.Geometry is not HxMesh geo) return false;
            var positions = geo.Positions;
            var indices = geo.Indices;
            if (positions == null || indices == null) return false;
            int b = fi * 3;
            if (b + 2 >= indices.Count) return false;
            int i0 = indices[b];
            int i1 = indices[b + 1];
            int i2 = indices[b + 2];
            if (i0 < 0 || i1 < 0 || i2 < 0 || i0 >= positions.Count || i1 >= positions.Count || i2 >= positions.Count) return false;

            var world = GetWorldModelMatrix(_editingMeshNode);
            var p0 = Vector3.Transform(positions[i0], world);
            var p1 = Vector3.Transform(positions[i1], world);
            var p2 = Vector3.Transform(positions[i2], world);
            var c = (p0 + p1 + p2) / 3f;
            var hp = hit.PointHit;
            var h = new Vector3(hp.X, hp.Y, hp.Z);

            var avgEdge = (Vector3.Distance(p0, p1) + Vector3.Distance(p1, p2) + Vector3.Distance(p2, p0)) / 3f;
            if (avgEdge <= 1e-6f) return false;
            return Vector3.Distance(c, h) <= avgEdge * CenterPickRatio;
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
                Position = new Point3D(112.5 + 300, 112.5 - 300, 250), LookDirection = new Vector3D(-300, 300, -250),
                UpDirection = new Vector3D(0, 0, 1), NearPlaneDistance = 0.5,
                FarPlaneDistance = 5000, FieldOfView = 45
            };
            _pivotPoint = new Point3D(112.5, 112.5, 0);
        }

        private void SetOrthographicCamera()
        {
            MainViewport.Camera = new HxOrtho
            {
                Position = new Point3D(112.5 + 300, 112.5 - 300, 250), LookDirection = new Vector3D(-300, 300, -250),
                UpDirection = new Vector3D(0, 0, 1), NearPlaneDistance = 0.5,
                FarPlaneDistance = 5000, Width = 400
            };
            _pivotPoint = new Point3D(112.5, 112.5, 0);
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

        private void GetTargetCenterAndDistance(out Point3D centerPos, out double evalDistance)
        {
            Vector3 minP, maxP;
            if (SelectedNode != null)
            {
                var bb = SelectedNode.BoundsWithTransform;
                minP = bb.Minimum;
                maxP = bb.Maximum;
            }
            else
            {
                // 此时0,0原点在左上/前左，平台尺寸依然为 225x225
                minP = new Vector3(0f, 0f, 0f);
                maxP = new Vector3(225f, 225f, 250f);
            }

            var center = (minP + maxP) / 2f;
            var radius = (maxP - minP).Length() / 2f;

            // 根据默认的 45度 FOV 计算完美框显视距 ( d = r / sin(fov/2) )
            evalDistance = radius / Math.Sin(45.0 / 2.0 * Math.PI / 180.0);
            centerPos = new Point3D(center.X, center.Y, center.Z);
        }

        private void SmartZoomExtents()
        {
            if (MainViewport.Camera == null) return;
            GetTargetCenterAndDistance(out Point3D center, out double distance);

            var look = MainViewport.Camera.LookDirection;
            look.Normalize();
            var up = MainViewport.Camera.UpDirection;
            up.Normalize();

            _pivotPoint = center;
            AnimateCameraTo(look, up, _pivotPoint, distance);
        }

        private void SwipeToOrthographicView(System.Windows.Vector delta)
        {
            if (MainViewport.Camera == null) return;
            var cam = MainViewport.Camera;
            
            var look = cam.LookDirection;
            look.Normalize();
            var up = cam.UpDirection;
            up.Normalize();
            var right = Vector3D.CrossProduct(look, up);
            right.Normalize();

            // Screen X is right, Screen Y is down.
            // Map 2D swipe to 3D world direction.
            var worldSwipe = right * delta.X + (-up) * delta.Y;
            worldSwipe.Normalize();

            // Define cardinal directions
            var cardinals = new[] {
                new Vector3D(1, 0, 0), new Vector3D(-1, 0, 0),
                new Vector3D(0, 1, 0), new Vector3D(0, -1, 0),
                new Vector3D(0, 0, 1), new Vector3D(0, 0, -1)
            };

            var bestAxis = cardinals.OrderByDescending(c => Vector3D.DotProduct(c, worldSwipe)).First();

            // Swipe direction matches rotation intent.
            var targetLook = bestAxis;
            var targetUp = new Vector3D(0,0,1);
            if (targetLook.X == 0 && targetLook.Y == 0)
                targetUp = new Vector3D(0,1,0); // 顶视或底视图
            else
            {
                var r = Vector3D.CrossProduct(targetLook, new Vector3D(0,0,1));
                if (r.LengthSquared > 0.001)
                {
                    targetUp = Vector3D.CrossProduct(r, targetLook);
                    targetUp.Normalize();
                }
            }

            double distance = cam.LookDirection.Length;
            if (distance < 1) distance = 250;
            AnimateCameraTo(targetLook, targetUp, _pivotPoint, distance);
        }

        private void AnimateCameraTo(Vector3D targetLookDir, Vector3D targetUpDir, Point3D targetCenter, double targetDistance)
        {
            if (MainViewport.Camera is not HelixToolkit.Wpf.SharpDX.ProjectionCamera cam) return;

            var newLook = targetLookDir * targetDistance;
            var newPos = targetCenter - newLook;

            var time = TimeSpan.FromMilliseconds(300);
            var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut };

            var lookAnim = new System.Windows.Media.Animation.Vector3DAnimation(newLook, time) { EasingFunction = ease };
            var upAnim = new System.Windows.Media.Animation.Vector3DAnimation(targetUpDir, time) { EasingFunction = ease };
            var posAnim = new System.Windows.Media.Animation.Point3DAnimation(newPos, time) { EasingFunction = ease };

            lookAnim.Completed += (s, e) => {
                cam.BeginAnimation(HelixToolkit.Wpf.SharpDX.ProjectionCamera.LookDirectionProperty, null);
                cam.LookDirection = newLook;
            };
            upAnim.Completed += (s, e) => {
                cam.BeginAnimation(HelixToolkit.Wpf.SharpDX.ProjectionCamera.UpDirectionProperty, null);
                cam.UpDirection = targetUpDir;
            };
            posAnim.Completed += (s, e) => {
                cam.BeginAnimation(HelixToolkit.Wpf.SharpDX.ProjectionCamera.PositionProperty, null);
                cam.Position = newPos;
            };

            cam.BeginAnimation(HelixToolkit.Wpf.SharpDX.ProjectionCamera.LookDirectionProperty, lookAnim);
            cam.BeginAnimation(HelixToolkit.Wpf.SharpDX.ProjectionCamera.UpDirectionProperty, upAnim);
            cam.BeginAnimation(HelixToolkit.Wpf.SharpDX.ProjectionCamera.PositionProperty, posAnim);
        }

        // ==========================================
        // 2D ViewCube 实现
        // ==========================================
        private class BaseFace
        {
            public Vector3D Normal;
            public System.Windows.Shapes.Polygon Shape;
            public Vector3D[] Corners;
        }

        private class ZonePoly
        {
            public Vector3D ZoneDir;
            public System.Windows.Shapes.Polygon Shape;
            public Vector3D[] Corners;
        }

        private List<BaseFace> _baseFaces = new();
        private List<ZonePoly> _zonePolys = new();
        private System.Windows.Shapes.Line _axisX, _axisY, _axisZ;
        private TextBlock _lblX, _lblY, _lblZ;
        private Dictionary<Vector3D, TextBlock> _faceTexts = new();

        private void InitializeViewCube2D()
        {
            ViewCubeCanvas.Children.Clear();
            _baseFaces.Clear();
            _zonePolys.Clear();
            _faceTexts.Clear();

            // 1. 生成 6 个基础面（提供立方体的实体感和边缘线）
            Vector3D[][] faceCorners = {
                new Vector3D[] { new(-1,-1,1), new(1,-1,1), new(1,-1,-1), new(-1,-1,-1) }, // 前 (0,-1,0)
                new Vector3D[] { new(1,1,1), new(-1,1,1), new(-1,1,-1), new(1,1,-1) }, // 后 (0,1,0)
                new Vector3D[] { new(-1,-1,1), new(-1,1,1), new(1,1,1), new(1,-1,1) }, // 顶 (0,0,1)
                new Vector3D[] { new(-1,1,-1), new(-1,-1,-1), new(1,-1,-1), new(1,1,-1) }, // 底 (0,0,-1)
                new Vector3D[] { new(1,-1,1), new(1,1,1), new(1,1,-1), new(1,-1,-1) }, // 右 (1,0,0)
                new Vector3D[] { new(-1,1,1), new(-1,-1,1), new(-1,-1,-1), new(-1,1,-1) }  // 左 (-1,0,0)
            };
            Vector3D[] faceNormals = {
                new(0,-1,0), new(0,1,0), new(0,0,1), new(0,0,-1), new(1,0,0), new(-1,0,0)
            };

            for (int i = 0; i < 6; i++)
            {
                var basePoly = new System.Windows.Shapes.Polygon
                {
                    Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(120, 240, 240, 240)), // 半透明偏白材质
                    Stroke = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White), // 纯白描边框线
                    StrokeThickness = 1.0,
                    StrokeLineJoin = PenLineJoin.Round, // 防止锐角产生突出毛刺
                    IsHitTestVisible = false // 基础面不接收点击
                };
                _baseFaces.Add(new BaseFace { Normal = faceNormals[i], Shape = basePoly, Corners = faceCorners[i] });
                ViewCubeCanvas.Children.Add(basePoly);
            }

            // 2. 生成 26 个交互热区（本身隐形，悬停时高亮）
            double threshold = 0.65;
            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    for (int z = -1; z <= 1; z++)
                    {
                        if (x == 0 && y == 0 && z == 0) continue;

                        var minX = x == 0 ? -threshold : (x == 1 ? threshold : -1);
                        var maxX = x == 0 ?  threshold : (x == 1 ? 1 : -threshold);
                        var minY = y == 0 ? -threshold : (y == 1 ? threshold : -1);
                        var maxY = y == 0 ?  threshold : (y == 1 ? 1 : -threshold);
                        var minZ = z == 0 ? -threshold : (z == 1 ? threshold : -1);
                        var maxZ = z == 0 ?  threshold : (z == 1 ? 1 : -threshold);

                        var corners = new Vector3D[] {
                            new Vector3D(minX, minY, minZ), new Vector3D(maxX, minY, minZ),
                            new Vector3D(minX, maxY, minZ), new Vector3D(maxX, maxY, minZ),
                            new Vector3D(minX, minY, maxZ), new Vector3D(maxX, minY, maxZ),
                            new Vector3D(minX, maxY, maxZ), new Vector3D(maxX, maxY, maxZ)
                        };

                        var poly = new System.Windows.Shapes.Polygon
                        {
                            Fill = System.Windows.Media.Brushes.Transparent, 
                            Stroke = System.Windows.Media.Brushes.Transparent,
                            StrokeThickness = 0,
                            Cursor = Cursors.Hand,
                            Tag = new Vector3D(x, y, z)
                        };

                        // 参考拓竹：悬停时赋予明显的蓝色高亮区
                        poly.MouseEnter += (s, e) => { (s as System.Windows.Shapes.Polygon).Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(100, 0, 119, 255)); };
                        poly.MouseLeave += (s, e) => { (s as System.Windows.Shapes.Polygon).Fill = System.Windows.Media.Brushes.Transparent; };

                        _zonePolys.Add(new ZonePoly { ZoneDir = new Vector3D(x, y, z), Shape = poly, Corners = corners });
                        ViewCubeCanvas.Children.Add(poly);
                    }
                }
            }

            _axisX = new System.Windows.Shapes.Line { Stroke = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 80, 80)), StrokeThickness = 2, IsHitTestVisible = false };
            _axisY = new System.Windows.Shapes.Line { Stroke = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(80, 220, 80)), StrokeThickness = 2, IsHitTestVisible = false };
            _axisZ = new System.Windows.Shapes.Line { Stroke = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(80, 150, 255)), StrokeThickness = 2, IsHitTestVisible = false };
            ViewCubeCanvas.Children.Add(_axisX);
            ViewCubeCanvas.Children.Add(_axisY);
            ViewCubeCanvas.Children.Add(_axisZ);

            TextBlock CreateLabel(string text, System.Windows.Media.Color c, bool bold = false)
            {
                var tb = new TextBlock { Text = text, Foreground = new System.Windows.Media.SolidColorBrush(c), FontSize = 12, IsHitTestVisible = false, TextAlignment = TextAlignment.Center, Width = 40, Height = 16 };
                if (bold) tb.FontWeight = FontWeights.Bold;
                ViewCubeCanvas.Children.Add(tb);
                return tb;
            }

            _lblX = CreateLabel("X", System.Windows.Media.Color.FromRgb(255, 80, 80), true);
            _lblY = CreateLabel("Y", System.Windows.Media.Color.FromRgb(80, 200, 80), true);
            _lblZ = CreateLabel("Z", System.Windows.Media.Color.FromRgb(80, 150, 255), true);

            var faceColor = System.Windows.Media.Colors.White;
            _faceTexts[new Vector3D(1, 0, 0)] = CreateLabel("右面", faceColor, true);
            _faceTexts[new Vector3D(-1, 0, 0)] = CreateLabel("左面", faceColor, true);
            _faceTexts[new Vector3D(0, 1, 0)] = CreateLabel("后面", faceColor, true);
            _faceTexts[new Vector3D(0, -1, 0)] = CreateLabel("前面", faceColor, true);
            _faceTexts[new Vector3D(0, 0, 1)] = CreateLabel("顶部", faceColor, true);
            _faceTexts[new Vector3D(0, 0, -1)] = CreateLabel("底部", faceColor, true);

            CompositionTarget.Rendering += UpdateViewCubeCanvas;
        }

        private void UpdateViewCubeCanvas(object sender, EventArgs e)
        {
            if (MainViewport.Camera == null) return;
            var look = MainViewport.Camera.LookDirection;
            look.Normalize();
            var up = MainViewport.Camera.UpDirection;
            up.Normalize();
            var right = Vector3D.CrossProduct(look, up);
            if (right.LengthSquared > 0.001) right.Normalize();

            // 完美正交化屏幕的 Y 轴（修正透视畸变）
            var trueUp = Vector3D.CrossProduct(right, look);
            if (trueUp.LengthSquared > 0.001) trueUp.Normalize();
            else trueUp = up;

            double scale = 22.0; 
            double cx = 50.0, cy = 50.0; 

            // 沿完美的正交相机坐标系进行二维投影
            Point Project(Vector3D w) => new Point(cx + scale * Vector3D.DotProduct(right, w), cy - scale * Vector3D.DotProduct(trueUp, w));

            // 1. 渲染基础面（实体感与外框线）
            foreach (var bf in _baseFaces)
            {
                double dot = Vector3D.DotProduct(bf.Normal, look);
                if (dot > -0.001)
                {
                    bf.Shape.Visibility = Visibility.Collapsed;
                    continue;
                }
                bf.Shape.Visibility = Visibility.Visible;
                Canvas.SetZIndex(bf.Shape, (int)(-dot * 100)); // 底层

                var pts = new List<Point>();
                foreach (var c in bf.Corners) pts.Add(Project(c));
                bf.Shape.Points = ConvexHull2D(pts);
            }

            // 2. 渲染交互热区（26 区网格）
            foreach (var zp in _zonePolys)
            {
                double dot = Vector3D.DotProduct(zp.ZoneDir, look);
                if (dot > -0.001) // 同步隐藏判定
                {
                    zp.Shape.Visibility = Visibility.Collapsed;
                    continue;
                }
                zp.Shape.Visibility = Visibility.Visible;
                Canvas.SetZIndex(zp.Shape, (int)(-dot * 100) + 10); // 浮在基础面上

                var pts = new List<Point>();
                foreach (var c in zp.Corners) pts.Add(Project(c));
                zp.Shape.Points = ConvexHull2D(pts);
            }

            // 将 XYZ 坐标系彻底依附于 ViewCube 真实映射的 3D 原点 (-1, -1, -1)
            var origin3D = new Vector3D(-1, -1, -1);
            
            _axisX.Visibility = _axisY.Visibility = _axisZ.Visibility = Visibility.Visible;
            _lblX.Visibility = _lblY.Visibility = _lblZ.Visibility = Visibility.Visible;
            
            var pO = Project(origin3D);
            _axisX.X1 = pO.X; _axisX.Y1 = pO.Y; 
            _axisY.X1 = pO.X; _axisY.Y1 = pO.Y; 
            _axisZ.X1 = pO.X; _axisZ.Y1 = pO.Y; 
            
            // 线条稍微长出魔方边缘（魔方跨度为 -1 到 1，此处设 1.2 起到突出的参考线效果）
            var pX = Project(new Vector3D(1.2, -1, -1)); _axisX.X2 = pX.X; _axisX.Y2 = pX.Y;
            var pY = Project(new Vector3D(-1, 1.2, -1)); _axisY.X2 = pY.X; _axisY.Y2 = pY.Y;
            var pZ = Project(new Vector3D(-1, -1, 1.2)); _axisZ.X2 = pZ.X; _axisZ.Y2 = pZ.Y;

            // ZIndex 绝对固定：基础面最大不到 100，这里将坐标轴设定在基础面之上，文字图层之下
            Canvas.SetZIndex(_axisX, 150); Canvas.SetZIndex(_axisY, 150); Canvas.SetZIndex(_axisZ, 150);

            void MoveLbl(TextBlock t, Vector3D v) { var p = Project(v); Canvas.SetLeft(t, p.X - 20); Canvas.SetTop(t, p.Y - 8); Canvas.SetZIndex(t, 151); }
            MoveLbl(_lblX, new Vector3D(1.5, -1, -1));
            MoveLbl(_lblY, new Vector3D(-1, 1.5, -1));
            MoveLbl(_lblZ, new Vector3D(-1, -1, 1.5));

            foreach (var kvp in _faceTexts)
            {
                double dot = Vector3D.DotProduct(kvp.Key, look);
                if (dot > -0.001)
                {
                    kvp.Value.Visibility = Visibility.Collapsed;
                }
                else
                {
                    kvp.Value.Visibility = Visibility.Visible;
                    var p = Project(kvp.Key * 1.05);
                    Canvas.SetLeft(kvp.Value, p.X - 20);
                    Canvas.SetTop(kvp.Value, p.Y - 8);
                    // 完全确保所有文本悬浮在坐标轴之上
                    Canvas.SetZIndex(kvp.Value, (int)(-dot * 100) + 300); 
                }
            }
        }

        private PointCollection ConvexHull2D(List<Point> points)
        {
            if (points.Count <= 3) return new PointCollection(points);
            points = points.OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
            var lower = new List<Point>();
            foreach (var p in points)
            {
                while (lower.Count >= 2 && Cross(lower[lower.Count - 2], lower[lower.Count - 1], p) <= 0) lower.RemoveAt(lower.Count - 1);
                lower.Add(p);
            }
            var upper = new List<Point>();
            for (int i = points.Count - 1; i >= 0; i--)
            {
                var p = points[i];
                while (upper.Count >= 2 && Cross(upper[upper.Count - 2], upper[upper.Count - 1], p) <= 0) upper.RemoveAt(upper.Count - 1);
                upper.Add(p);
            }
            lower.RemoveAt(lower.Count - 1);
            upper.RemoveAt(upper.Count - 1);
            lower.AddRange(upper);
            return new PointCollection(lower);
        }

        private double Cross(Point o, Point a, Point b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

        private void ViewCubeCanvas_MouseMove(object sender, MouseEventArgs e) { }
        private void ViewCubeCanvas_MouseLeave(object sender, MouseEventArgs e) { }

        private void ViewCubeCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is System.Windows.Shapes.Polygon poly && poly.Tag is Vector3D zone)
            {
                var targetLook = new Vector3D(-zone.X, -zone.Y, -zone.Z);
                targetLook.Normalize();

                var targetUp = new Vector3D(0, 0, 1);
                if (zone.X == 0 && zone.Y == 0) 
                    targetUp = new Vector3D(0, 1, 0);
                else
                {
                    var r = Vector3D.CrossProduct(targetLook, new Vector3D(0,0,1));
                    if (r.LengthSquared > 0.001)
                    {
                        targetUp = Vector3D.CrossProduct(r, targetLook);
                        targetUp.Normalize();
                    }
                }
                
                // 获取基于全局模型或选区的完美框显中心点和视距
                GetTargetCenterAndDistance(out Point3D targetCenter, out double distance);
                _pivotPoint = targetCenter;
                
                AnimateCameraTo(targetLook, targetUp, _pivotPoint, distance);
                
                e.Handled = true;
            }
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

        /// <summary>
        /// 使用 3D 射线检测判断屏幕位置是否命中了 Gizmo（GizmoAnchor 或 ObjectManipulator 的子树）。
        /// Gizmo 在 DirectX 里渲染，不在 WPF VisualTree 中，所以必须用 FindHits 来检测。
        /// </summary>
        private bool IsGizmoHit(Point screenPos)
        {
            // Gizmo 的命中测试根节点：ObjectManipulator 内部 AlwaysHitGroupNode + GizmoAnchor
            var manipNode  = ObjectManipulator.SceneNode;
            var anchorNode = GizmoAnchor.SceneNode;
            if (manipNode == null && anchorNode == null) return false;

            try
            {
                var hits = MainViewport.FindHits(screenPos);
                if (hits == null || hits.Count == 0) return false;

                foreach (var hit in hits)
                {
                    if (hit.ModelHit is not SceneNode sn) continue;
                    if (manipNode  != null && IsInSceneNodeSubtree(sn, manipNode))  return true;
                    if (anchorNode != null && IsInSceneNodeSubtree(sn, anchorNode)) return true;
                }
            }
            catch { }

            return false;
        }

        /// <summary>
        /// 判断 node 是否在以 root 为根的 SceneNode 子树中（含 root 本身）。
        /// </summary>
        private static bool IsInSceneNodeSubtree(SceneNode node, SceneNode root)
        {
            var cur = node;
            while (cur != null)
            {
                if (ReferenceEquals(cur, root)) return true;
                cur = cur.Parent;
            }
            return false;
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
