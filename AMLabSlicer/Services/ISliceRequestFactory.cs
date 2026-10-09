using AMLabSlicer.Core.Parameters;
using AMLabSlicer.Grpc;
using HelixToolkit.SharpDX.Model.Scene;

namespace AMLabSlicer.Services;

public readonly record struct SceneObject(string Name, SceneNode Node);

public interface ISliceRequestFactory
{
    SliceRequest Create(string algorithmId, IEnumerable<SliceParameter> parameters,
        IEnumerable<SceneObject> objects);
}
