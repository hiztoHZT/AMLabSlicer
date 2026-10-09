using System.Globalization;
using System.Numerics;
using AMLabSlicer.Core.Parameters;
using AMLabSlicer.Grpc;
using Google.Protobuf;
using HelixToolkit.SharpDX;
using HelixToolkit.SharpDX.Model.Scene;

namespace AMLabSlicer.Services;

public sealed class SliceRequestFactory : ISliceRequestFactory
{
    public SliceRequest Create(string algorithmId, IEnumerable<SliceParameter> parameters,
        IEnumerable<SceneObject> objects)
    {
        if (string.IsNullOrWhiteSpace(algorithmId))
            throw new InvalidOperationException("未选择切片算法。");

        var request = new SliceRequest
        {
            AlgorithmId = algorithmId,
            RequestId = Guid.NewGuid().ToString("N"),
            FiveAxisConfig = new FiveAxisConfig { IsEnabled = false }
        };
        foreach (var parameter in parameters)
            request.Parameters[parameter.Key] = ToGrpcValue(parameter.Value, parameter.ControlType);

        long objectId = 1;
        foreach (var sceneObject in objects)
        {
            foreach (var meshNode in sceneObject.Node.Traverse().OfType<MeshNode>())
            {
                if (meshNode.Geometry is not MeshGeometry3D geometry ||
                    geometry.Positions is not { Count: > 0 } positions ||
                    geometry.Indices is not { Count: > 0 } indices)
                    continue;
                if (indices.Count % 3 != 0)
                    throw new InvalidOperationException($"模型“{sceneObject.Name}”的三角形索引数量无效。");

                var world = SceneTransforms.GetWorldMatrix(meshNode);
                var packedPositions = new float[positions.Count * 3];
                for (var i = 0; i < positions.Count; i++)
                {
                    var point = Vector3.Transform(positions[i], world);
                    if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z))
                        throw new InvalidOperationException($"模型“{sceneObject.Name}”包含非有限坐标。");
                    packedPositions[i * 3] = point.X;
                    packedPositions[i * 3 + 1] = point.Y;
                    packedPositions[i * 3 + 2] = point.Z;
                }

                var packedIndices = indices.ToArray();
                if (packedIndices.Any(index => index < 0 || index >= positions.Count))
                    throw new InvalidOperationException($"模型“{sceneObject.Name}”包含越界索引。");

                request.Objects.Add(new MeshObject
                {
                    Id = objectId++,
                    Name = sceneObject.Name,
                    Units = "mm",
                    CoordinateSystem = "world",
                    TransformApplied = true,
                    Vertices = ByteString.CopyFrom(ToBytes(packedPositions)),
                    Indices = ByteString.CopyFrom(ToBytes(packedIndices))
                });
            }
        }
        return request;
    }

    private static byte[] ToBytes(Array values)
    {
        var bytes = new byte[Buffer.ByteLength(values)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static ParameterValue ToGrpcValue(object? value, UIControlType controlType)
    {
        if (value == null) return new ParameterValue { StringValue = string.Empty };
        if (controlType == UIControlType.CheckBox)
            return new ParameterValue { BoolValue = value is bool flag ? flag : bool.TryParse(value.ToString(), out var parsed) && parsed };
        if (controlType is UIControlType.NumericBox or UIControlType.Slider)
        {
            if (value is int integer) return new ParameterValue { IntValue = integer };
            if (value is long longInteger) return new ParameterValue { IntValue = longInteger };
            if (value is double number) return new ParameterValue { DoubleValue = number };
            if (value is float single) return new ParameterValue { DoubleValue = single };
            if (double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed) ||
                double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                return new ParameterValue { DoubleValue = parsed };
        }
        return new ParameterValue { StringValue = value.ToString() ?? string.Empty };
    }
}
