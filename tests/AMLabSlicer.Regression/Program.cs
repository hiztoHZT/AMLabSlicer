using System.Globalization;
using System.Numerics;
using System.Windows.Threading;
using AMLabSlicer.Core.Commands;
using AMLabSlicer.Core.Parameters;
using AMLabSlicer.Grpc;
using AMLabSlicer.Services;
using AMLabSlicer.ViewModel;
using HelixToolkit.SharpDX.Model.Scene;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        int result = 1;
        dispatcher.InvokeAsync(async () =>
        {
            try
            {
                if (args is ["--plugin-probe", var removeDirectory, var removeData, var removeCount, "--remove"])
                    await PluginRuntimeChecks.RunAsync(removeDirectory, removeData, int.Parse(removeCount), remove: true);
                else if (args is ["--plugin-probe", var directory, var data, var count])
                    await PluginRuntimeChecks.RunAsync(directory, data, int.Parse(count));
                else await RunAsync();
                result = 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
        });
        Dispatcher.Run();
        return result;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }

    private static async Task RunAsync()
    {
        var history = new AMLabSlicer.Core.Commands.CommandManager();
        int value = 0;
        for (int i = 0; i < 5; i++) history.ExecuteCommand(new ActionCommand(() => value++, () => value--));
        history.MaxDepth = 2;
        history.Undo(); history.Undo(); history.Undo();
        Check(value == 3, "reducing history depth keeps only newest commands");
        history.Redo();
        Check(value == 4, "redo order survives trimming");
        history.MaxDepth = 0;
        Check(!history.CanUndo && !history.CanRedo, "zero history depth clears both stacks");
        history.MaxDepth = 2;
        history.Push(new ActionCommand(() => { }, () => throw new InvalidOperationException("expected")));
        try { history.Undo(); } catch (InvalidOperationException) { }
        Check(history.CanUndo && !history.CanRedo, "failed undo preserves history");

        using var parent = new GroupNode { ModelMatrix = Matrix4x4.CreateRotationZ(MathF.PI / 2) };
        var child = new GroupNode { ModelMatrix = Matrix4x4.CreateTranslation(10, 0, 0) };
        parent.AddChildNode(child);
        var world = Vector3.Transform(Vector3.Zero, SceneTransforms.GetWorldMatrix(child));
        Check(Vector3.Distance(world, new Vector3(0, 10, 0)) < 0.001f, "nested translation then parent rotation matches row-vector convention");

        using var mesh = new MeshNode
        {
            Geometry = new HelixToolkit.SharpDX.MeshGeometry3D
            {
                Positions = new HelixToolkit.Vector3Collection(new[] { new Vector3(3, 3, 3), new Vector3(2, 2, 2), new Vector3(1, 1, 1) }),
                Indices = new HelixToolkit.IntCollection(new[] { 0, 1, 2 })
            },
            ModelMatrix = Matrix4x4.CreateRotationZ(MathF.PI / 4)
        };
        Check(SceneTransforms.TryComputeWorldAabb(mesh, out var min, out var max) && max.Z == 3 && min.Z == 1,
            "descending vertices update both AABB extrema");
        var oldMatrix = mesh.ModelMatrix;
        var arrangement = SceneArrangement.CreateCommand(new[] { mesh });
        arrangement.Execute();
        SceneTransforms.TryComputeWorldAabb(mesh, out min, out max);
        Check(Math.Abs(min.Z) < 0.001f && min.X >= -0.001f && min.Y >= -0.001f && max.X <= 225 && max.Y <= 225,
            "rotated model arrangement stays on the visible bed");
        arrangement.Undo();
        Check(mesh.ModelMatrix == oldMatrix, "arrangement is undoable");

        var requestFactory = new SliceRequestFactory();
        var request = requestFactory.Create("test", Array.Empty<SliceParameter>(),
            new[] { new SceneObject("triangle", mesh) });
        Check(request.Objects.Count == 1 && request.Objects[0].Vertices.Length == 9 * sizeof(float),
            "slice request factory serializes transformed scene meshes");
        var invalidMesh = new MeshNode
        {
            Geometry = new HelixToolkit.SharpDX.MeshGeometry3D
            {
                Positions = new HelixToolkit.Vector3Collection(new[] { Vector3.Zero }),
                Indices = new HelixToolkit.IntCollection(new[] { 0, 1, 0 })
            }
        };
        try
        {
            requestFactory.Create("test", Array.Empty<SliceParameter>(), new[] { new SceneObject("invalid", invalidMesh) });
            throw new InvalidOperationException("out-of-range indices were accepted");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("越界索引", StringComparison.Ordinal))
        {
            Console.WriteLine("PASS slice request factory rejects out-of-range indices");
        }

        var disconnectedMesh = new MeshNode
        {
            Name = "two-parts",
            Geometry = new HelixToolkit.SharpDX.MeshGeometry3D
            {
                Positions = new HelixToolkit.Vector3Collection(new[]
                {
                    new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0),
                    new Vector3(10, 0, 0), new Vector3(11, 0, 0), new Vector3(10, 1, 0)
                }),
                Indices = new HelixToolkit.IntCollection(new[] { 0, 1, 2, 3, 4, 5 })
            }
        };
        var parts = MeshComponentSplitter.Split(disconnectedMesh);
        Check(parts.Count == 2 && parts.All(part => part.Geometry?.Indices?.Count == 3),
            "mesh splitter creates one mesh for each connected component");
        var splitParent = new GroupNode();
        splitParent.AddChildNode(disconnectedMesh);
        var splitOutliner = new System.Collections.ObjectModel.ObservableCollection<OutlinerNodeViewModel>();
        var splitCommand = new AMLabSlicer.Commands.SplitMeshCommand(splitParent, disconnectedMesh, parts, splitOutliner);
        splitCommand.Execute();
        Check(splitParent.Items.Count == 2 && splitOutliner.Count == 2, "split command updates scene and outliner");
        splitCommand.Undo();
        Check(splitParent.Items.Count == 1 && ReferenceEquals(splitParent.Items[0], disconnectedMesh),
            "split command restores the original mesh");

        var service = new FakeSlicing();
        var dialogs = new FakeDialogs();
        var workspace = new PrepareWorkspaceViewModel(new ParameterStore(), new PreferencesViewModel(), service,
            new SliceRequestFactory(), dialogs);
        Check(service.Requests.Count == 0, "view model constructor does not start network I/O");
        workspace.SelectedAlgorithm = new AlgorithmInfo { AlgorithmId = "old" };
        workspace.SelectedAlgorithm = new AlgorithmInfo { AlgorithmId = "new" };
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        service.Requests["new"].SetResult(Template("new", "0.25"));
        await Task.Delay(25);
        service.Requests["old"].SetResult(Template("old", "9"));
        await Task.Delay(25);
        Check(workspace.Parameters.Single().Key == "new", "stale algorithm response cannot overwrite current parameters");
        Check(Convert.ToDouble(workspace.Parameters[0].Value) == 0.25, "protocol numeric defaults use invariant culture");
        CultureInfo.CurrentCulture = culture;
        await workspace.StartSlicingCommand.ExecuteAsync(null);
        Check(service.SliceCalls == 0 && dialogs.Messages.Count == 1, "empty scene is rejected before transport");

        var main = new MainWindowViewModel(workspace, dialogs, new FakeImporter());
        workspace.History.ExecuteCommand(new ActionCommand(() => value++, () => value--));
        var before = value;
        main.UndoCommand.Execute(null);
        Check(value == before - 1, "main window undo uses workspace history without a view");
        main.RedoCommand.Execute(null);
        Check(value == before, "main window redo uses workspace history without a view");
        await PreferencesAndAgentChecks.RunAsync(Check, dialogs);
        Console.WriteLine("All regression checks passed.");
    }

    private static ParameterTemplateList Template(string key, string value)
    {
        var result = new ParameterTemplateList();
        result.Parameters.Add(new ParameterTemplate { Key = key, DefaultValue = value, ControlType = ControlType.NumericBox });
        return result;
    }
}

internal sealed class ActionCommand(Action execute, Action undo) : ICommandAction
{
    public string Name => "test";
    public void Execute() => execute();
    public void Undo() => undo();
}

internal sealed class FakeDialogs : IUserDialogService
{
    public List<string> Messages { get; } = [];
    public string[] SelectModels() => [];
    public string? SelectGCodeDestination() => null;
    public void ShowMessage(string message, string title = "提示") => Messages.Add(message);
    public void OpenPreferences() { }
    public string? ExtensionsSection { get; private set; }
    public void OpenExtensions(string section = "Agent") => ExtensionsSection = section;
}

internal sealed class FakeImporter : IModelImportService
{
    public Task<SceneNode?> LoadAsync(string filePath) => Task.FromResult<SceneNode?>(null);
}

internal sealed class FakeSlicing : ISlicingService
{
    public Dictionary<string, TaskCompletionSource<ParameterTemplateList>> Requests { get; } = [];
    public int SliceCalls { get; private set; }
    public Task<AlgorithmList> GetAlgorithmsAsync(CancellationToken cancellationToken = default) => Task.FromResult(new AlgorithmList());
    public Task<ParameterTemplateList> GetParametersAsync(string algorithm, CancellationToken cancellationToken = default)
    {
        var completion = new TaskCompletionSource<ParameterTemplateList>();
        Requests[algorithm] = completion;
        return completion.Task;
    }
    public async IAsyncEnumerable<SliceServerMessage> SliceAsync(SliceRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        SliceCalls++;
        await Task.CompletedTask;
        yield break;
    }
}
