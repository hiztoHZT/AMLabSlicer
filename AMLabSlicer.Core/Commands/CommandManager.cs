using System;
using System.Collections.Generic;

namespace AMLabSlicer.Core.Commands
{
    public class CommandManager
    {
        private readonly Stack<ICommandAction> _undoStack = new Stack<ICommandAction>();
        private readonly Stack<ICommandAction> _redoStack = new Stack<ICommandAction>();

        public int MaxDepth { get; set; } = 25;

        public event EventHandler? CommandExecuted;

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        /// <summary>
        /// 将新命令压栈并清空重做栈
        /// </summary>
        public void Push(ICommandAction command)
        {
            _undoStack.Push(command);
            _redoStack.Clear();

            // 限制最大深度
            if (_undoStack.Count > MaxDepth)
            {
                // 用数组倒腾或者更高级的数据结构截断栈底。简单起见：
                var array = _undoStack.ToArray();
                _undoStack.Clear();
                for (int i = array.Length - 2; i >= 0; i--)
                {
                    _undoStack.Push(array[i]);
                }
            }
            CommandExecuted?.Invoke(this, EventArgs.Empty);
        }

        public void ExecuteCommand(ICommandAction command)
        {
            command.Execute();
            Push(command);
        }

        public void Undo()
        {
            if (CanUndo)
            {
                var cmd = _undoStack.Pop();
                cmd.Undo();
                _redoStack.Push(cmd);
                CommandExecuted?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Redo()
        {
            if (CanRedo)
            {
                var cmd = _redoStack.Pop();
                cmd.Execute();
                _undoStack.Push(cmd);
                CommandExecuted?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
            CommandExecuted?.Invoke(this, EventArgs.Empty);
        }
    }
}
