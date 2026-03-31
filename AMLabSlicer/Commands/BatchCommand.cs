using AMLabSlicer.Core.Commands;
using System.Collections.Generic;

namespace AMLabSlicer.Commands
{
    /// <summary>
    /// 将多个命令打包为单条 Undo/Redo 记录（用于自动摆放等批量操作）
    /// </summary>
    public class BatchCommand : ICommandAction
    {
        private readonly List<ICommandAction> _cmds;
        public string Name { get; }

        public BatchCommand(IEnumerable<ICommandAction> commands, string name = "批量操作")
        {
            _cmds = new List<ICommandAction>(commands);
            Name  = name;
        }

        public void Execute()
        {
            foreach (var cmd in _cmds) cmd.Execute();
        }

        public void Undo()
        {
            for (int i = _cmds.Count - 1; i >= 0; i--)
                _cmds[i].Undo();
        }
    }
}
