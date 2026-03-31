using AMLabSlicer.Core.Commands;
using AMLabSlicer.ViewModel;
using HelixToolkit.SharpDX.Model.Scene;
using System.Collections.ObjectModel;

namespace AMLabSlicer.Commands
{
    public class DeleteCommand : ICommandAction
    {
        private readonly ObservableCollection<OutlinerNodeViewModel> _parentCollection;
        private readonly OutlinerNodeViewModel _targetItem;
        private readonly GroupNode _parentGroup;
        private readonly SceneNode _targetNode;
        private readonly int _indexInCollection;

        public string Name => $"删除 {_targetItem.Name}";

        public DeleteCommand(
            ObservableCollection<OutlinerNodeViewModel> parentCollection, 
            OutlinerNodeViewModel targetItem,
            GroupNode parentGroup,
            SceneNode targetNode)
        {
            _parentCollection = parentCollection;
            _targetItem = targetItem;
            _parentGroup = parentGroup;
            _targetNode = targetNode;
            _indexInCollection = _parentCollection.IndexOf(targetItem);
        }

        public void Execute()
        {
            // 从 UI 中移除
            _parentCollection.Remove(_targetItem);
            // 从场景图中移除
            _parentGroup.RemoveChildNode(_targetNode);
        }

        public void Undo()
        {
            // 在原位置插入
            if (_indexInCollection >= 0 && _indexInCollection <= _parentCollection.Count)
            {
                _parentCollection.Insert(_indexInCollection, _targetItem);
            }
            else
            {
                _parentCollection.Add(_targetItem);
            }
            _parentGroup.AddChildNode(_targetNode);
        }
    }
}
