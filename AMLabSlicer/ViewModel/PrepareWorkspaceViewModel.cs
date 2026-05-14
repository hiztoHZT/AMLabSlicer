using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using System.Numerics;
using System.Collections.ObjectModel;
using AMLabSlicer.Core.Parameters;
using AMLabSlicer.State;
using Grpc.Net.Client;
using AMLabSlicer.Grpc;
using System.Windows;
using System.Threading.Tasks;
using System.Linq;
using System;
using System.IO;
using Grpc.Core;
using HelixToolkit.SharpDX.Model.Scene;

namespace AMLabSlicer.ViewModel
{
    public partial class PrepareWorkspaceViewModel : ObservableObject
    {
        // 存放载入的 3D 模型
        [ObservableProperty]
        private Element3D? _loadedModel;

        // 存放主网格数据 (每 10mm 一根)
        [ObservableProperty]
        private Geometry3D? _majorGridGeometry;

        // 存放细网格数据 (每 1mm 一根)
        [ObservableProperty]
        private Geometry3D? _minorGridGeometry;

        // 视口模式状态机
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsObjectMode))]
        [NotifyPropertyChangedFor(nameof(IsFaceMode))]
        private ViewportMode _viewportMode = ViewportMode.ObjectMode;

        public bool IsObjectMode => _viewportMode == ViewportMode.ObjectMode;
        public bool IsFaceMode => _viewportMode == ViewportMode.FaceMode;

        [RelayCommand]
        private void ToggleViewportMode()
        {
            ViewportMode = ViewportMode == ViewportMode.ObjectMode
                ? ViewportMode.FaceMode
                : ViewportMode.ObjectMode;
        }

        // 左侧面板开关状态
        [ObservableProperty]
        private bool _isParameterPanelOpen = true;

        [RelayCommand]
        private void TogglePanel() => IsParameterPanelOpen = !IsParameterPanelOpen;

        // 面向大纲视图的模型节点树
        public ObservableCollection<OutlinerNodeViewModel> OutlinerItems { get; } = new ObservableCollection<OutlinerNodeViewModel>();

        // 可选切片算法集合
        public ObservableCollection<string> SlicingAlgorithms { get; } = new ObservableCollection<string>();

        private string _selectedAlgorithm = "";
        public string SelectedAlgorithm
        {
            get => _selectedAlgorithm;
            set
            {
                if (SetProperty(ref _selectedAlgorithm, value))
                {
                    if (!string.IsNullOrEmpty(value))
                    {
                        _ = RebuildParametersForAlgorithmAsync(value);
                    }
                }
            }
        }

        // 暴露给 UI 绑定的参数集合
        public ObservableCollection<SliceParameter> Parameters { get; } = new ObservableCollection<SliceParameter>();

        private readonly IParameterStore _parameterStore;
        
        public PreferencesViewModel AppPrefs { get; }

        public PrepareWorkspaceViewModel(IParameterStore parameterStore, PreferencesViewModel appPrefs)
        {
            _parameterStore = parameterStore;
            AppPrefs = appPrefs;

            // 初始化算法切换事件
            _ = InitializeGrpcAsync();

            // 在工作区初始化时，立刻生成切片平台网格
            GeneratePlatformGrid();
        }

        private GrpcChannel? _grpcChannel;
        private SlicerService.SlicerServiceClient? _grpcClient;

        private async Task InitializeGrpcAsync()
        {
            try
            {
                _grpcChannel = GrpcChannel.ForAddress("http://localhost:50051", new GrpcChannelOptions 
                { 
                    MaxReceiveMessageSize = null, 
                    MaxSendMessageSize = null 
                });
                _grpcClient = new SlicerService.SlicerServiceClient(_grpcChannel);

                var response = await _grpcClient.GetAvailableAlgorithmsAsync(new Empty());
                
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    SlicingAlgorithms.Clear();
                    foreach (var alg in response.Algorithms)
                    {
                        SlicingAlgorithms.Add(alg);
                    }

                    if (SlicingAlgorithms.Count > 0)
                    {
                        SelectedAlgorithm = SlicingAlgorithms[0];
                    }
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("无法连接到后端引擎 (amlabslicer.engine)。\n请确保后端服务已在 http://localhost:50051 运行。\n" + ex.Message, "连接失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async Task RebuildParametersForAlgorithmAsync(string algorithm)
        {
            if (_grpcClient == null) return;

            try
            {
                var response = await _grpcClient.GetAlgorithmParametersAsync(new AlgorithmRequest { AlgorithmName = algorithm });

                // 缓存旧参数用于继承
                var oldParams = _parameterStore.GetAllParameters().ToDictionary(p => p.Key, p => p.Value);

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    _parameterStore.ClearAll();

                    foreach (var pDef in response.Parameters)
                    {
                        var param = new SliceParameter
                        {
                            Key = pDef.Key,
                            DisplayName = pDef.DisplayName,
                            Category = pDef.Category,
                            Subcategory = pDef.Subcategory,
                            Order = pDef.Order,
                            ControlType = (UIControlType)pDef.ControlType,
                            Unit = pDef.Unit,
                            Description = pDef.Description,
                            MinValue = pDef.MinValue,
                            MaxValue = pDef.MaxValue,
                            Step = pDef.Step,
                            IsAdvanced = pDef.IsAdvanced,
                            VisibleIf = pDef.VisibleIf,
                            EnabledIf = pDef.EnabledIf
                        };

                        if (oldParams.TryGetValue(pDef.Key, out var oldVal))
                        {
                            param.Value = oldVal;
                        }
                        else
                        {
                            if (param.ControlType == UIControlType.CheckBox)
                            {
                                param.Value = bool.TryParse(pDef.DefaultValue, out bool b) && b;
                            }
                            else if (param.ControlType == UIControlType.NumericBox || param.ControlType == UIControlType.Slider)
                            {
                                if (double.TryParse(pDef.DefaultValue, out double d)) param.Value = d;
                            }
                            else
                            {
                                param.Value = pDef.DefaultValue;
                                if (param.ControlType == UIControlType.ComboBox)
                                {
                                    param.Options = pDef.Options.ToList();
                                }
                            }
                        }

                        _parameterStore.RegisterParameter(param);
                    }

                    Parameters.Clear();
                    foreach (var p in _parameterStore.GetAllParameters())
                    {
                        Parameters.Add(p);
                    }
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("获取参数列表失败: " + ex.Message);
            }
        }

        [RelayCommand]
        private async Task StartSlicingAsync()
        {
            if (_grpcClient == null)
            {
                MessageBox.Show("gRPC 客户端未初始化。无法连接到后端引擎。");
                return;
            }

            try
            {
                var req = new SliceRequest { AlgorithmName = SelectedAlgorithm ?? "", FiveAxisConfig = new FiveAxisConfig { IsEnabled = false } };
                foreach (var p in _parameterStore.GetAllParameters())
                {
                    req.GlobalParameters[p.Key] = p.Value?.ToString() ?? "";
                }

                // 将场景中加载的模型转化为 MeshObject
                // 注意：OutlinerItem.Node 是 GroupNode (pivotNode)，
                // 实际 MeshNode 在其子树中，需要递归遍历
                long objectId = 1;
                foreach (var item in OutlinerItems)
                {
                    if (item.Node == null) continue;

                    // 遍历该 outliner 节点的整个子树，收集所有 MeshNode
                    foreach (var descendant in item.Node.Traverse())
                    {
                        if (descendant is MeshNode meshNode && meshNode.Geometry is MeshGeometry3D geometry)
                        {
                            if (geometry.Positions == null || geometry.Indices == null) continue;

                            var mo = new MeshObject { Id = objectId++, Name = item.Name };

                            // 将顶点坐标变换到世界坐标系（考虑 pivot/平移等变换）
                            var worldMatrix = System.Numerics.Matrix4x4.Identity;
                            var stack = new System.Collections.Generic.Stack<SceneNode>();
                            SceneNode? cur = meshNode;
                            while (cur != null) { stack.Push(cur); cur = cur.Parent; }
                            while (stack.Count > 0)
                                worldMatrix = worldMatrix * stack.Pop().ModelMatrix;

                            var posArray = geometry.Positions.ToArray();
                            var floatArray = new float[posArray.Length * 3];
                            for (int i = 0; i < posArray.Length; i++)
                            {
                                // 变换到世界坐标
                                var wp = System.Numerics.Vector3.Transform(posArray[i], worldMatrix);
                                floatArray[i * 3] = wp.X;
                                floatArray[i * 3 + 1] = wp.Y;
                                floatArray[i * 3 + 2] = wp.Z;
                            }
                            var posBytes = new byte[floatArray.Length * 4];
                            Buffer.BlockCopy(floatArray, 0, posBytes, 0, posBytes.Length);
                            mo.Vertices = Google.Protobuf.ByteString.CopyFrom(posBytes);

                            var indicesArray = geometry.Indices.ToArray();
                            var indicesBytes = new byte[indicesArray.Length * 4];
                            Buffer.BlockCopy(indicesArray, 0, indicesBytes, 0, indicesBytes.Length);
                            mo.Indices = Google.Protobuf.ByteString.CopyFrom(indicesBytes);

                            req.Objects.Add(mo);
                        }
                    }
                }

                // 开启双端通信流
                using var call = _grpcClient.Slice();
                
                // 1. 客户端发送切片请求
                await call.RequestStream.WriteAsync(new SliceClientMessage { StartRequest = req });

                // 2. 接收服务端不断传回的进度、日志和最终结果
                await Task.Run(async () =>
                {
                    await foreach (var response in call.ResponseStream.ReadAllAsync())
                    {
                        if (response.MsgCase == SliceServerMessage.MsgOneofCase.Log)
                        {
                            Console.WriteLine($"[Backend Log]: {response.Log.Text}");
                        }
                        else if (response.MsgCase == SliceServerMessage.MsgOneofCase.Progress)
                        {
                            // 此处未来可绑定到 UI 进度条
                            Console.WriteLine($"[Progress]: {response.Progress.Progress * 100}% - {response.Progress.CurrentStage}");
                        }
                        else if (response.MsgCase == SliceServerMessage.MsgOneofCase.Result)
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                if (response.Result.Success)
                                {
                                    var dlg = new Microsoft.Win32.SaveFileDialog
                                    {
                                        Title = "保存切片 G-Code 文件",
                                        Filter = "G-Code Files (*.gcode)|*.gcode|All Files (*.*)|*.*",
                                        DefaultExt = ".gcode",
                                        FileName = "slice_output.gcode"
                                    };

                                    if (dlg.ShowDialog() == true)
                                    {
                                        File.WriteAllBytes(dlg.FileName, response.Result.Gcode.ToByteArray());
                                        MessageBox.Show($"文件已保存至：\n{dlg.FileName}", "切片完成", MessageBoxButton.OK, MessageBoxImage.Information);
                                    }
                                }
                                else
                                {
                                    MessageBox.Show($"切片失败: {response.Result.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                                }
                            });
                        }
                    }
                });

                // 通知服务端客户端发送完毕
                await call.RequestStream.CompleteAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("发送切片请求失败: " + ex.Message);
            }
        }
        /// <summary>
        /// </summary>
        private void GeneratePlatformGrid()
        {
            var majorBuilder = new LineBuilder();
            var minorBuilder = new LineBuilder();

            // 设定平台尺寸 225
            int width = 225;
            int depth = 225;
            
            int halfWidth = width / 2;
            int halfDepth = depth / 2;

            // 1. 沿着 X 轴画线（平行于 Y 轴的线）
            for (int x = 0; x <= width; x++)
            {
                // 如果能被 10 整除，就是主线（粗线），否则是细线
                if (x % 10 == 0)
                {
                    majorBuilder.AddLine(new Vector3(x, 0, 0), new Vector3(x, depth, 0));
                }
                else
                {
                    minorBuilder.AddLine(new Vector3(x, 0, 0), new Vector3(x, depth, 0));
                }
            }

            // 2. 沿着 Y 轴画线（平行于 X 轴的线）
            for (int y = 0; y <= depth; y++)
            {
                if (y % 10 == 0)
                {
                    majorBuilder.AddLine(new Vector3(0, y, 0), new Vector3(width, y, 0));
                }
                else
                {
                    minorBuilder.AddLine(new Vector3(0, y, 0), new Vector3(width, y, 0));
                }
            }

            // 将打包好的线框数据转换为渲染引擎认识的 Geometry3D，并绑定给前台
            MajorGridGeometry = majorBuilder.ToLineGeometry3D();
            MinorGridGeometry = minorBuilder.ToLineGeometry3D();
        }
    }
}