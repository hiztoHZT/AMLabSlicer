using AMLabSlicer.Core.Commands;
using HelixToolkit.SharpDX.Model.Scene;
using System.Numerics;

namespace AMLabSlicer.Commands
{
    public class TransformCommand : ICommandAction
    {
        private readonly SceneNode _targetNode;
        private readonly Matrix4x4 _oldTransform;
        private readonly Matrix4x4 _newTransform;
        private readonly string _commandName;

        public string Name => _commandName;

        public TransformCommand(SceneNode targetNode, Matrix4x4 oldTransform, Matrix4x4 newTransform, string commandName = "位移缩放")
        {
            _targetNode = targetNode;
            _oldTransform = oldTransform;
            _newTransform = newTransform;
            _commandName = commandName;
        }

        public void Execute()
        {
            _targetNode.ModelMatrix = _newTransform;
        }

        public void Undo()
        {
            _targetNode.ModelMatrix = _oldTransform;
        }
    }
}
