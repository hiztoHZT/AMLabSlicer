using System;
using System.Collections.Generic;

namespace AMLabSlicer.Core.Commands
{
    public class CommandManager
    {
        private readonly Stack<ICommandAction> _undoStack = new Stack<ICommandAction>();
        private readonly Stack<ICommandAction> _redoStack = new Stack<ICommandAction>();

        private int _maxDepth = 25;
        public int MaxDepth
        {
            get => _maxDepth;
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
                if (_maxDepth == value) return;
                _maxDepth = value;
                Trim(_undoStack);
                Trim(_redoStack);
                CommandExecuted?.Invoke(this, EventArgs.Empty);
            }
        }

        private void Trim(Stack<ICommandAction> stack)
        {
            if (stack.Count <= MaxDepth) return;
            var newest = stack.ToArray();
            stack.Clear();
            for (int i = MaxDepth - 1; i >= 0; i--)
                stack.Push(newest[i]);
        }

        public event EventHandler? CommandExecuted;

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        /// <summary>
        /// 将新命令压栈并清空重做栈
        /// </summary>
        public void Push(ICommandAction command)
        {
            ArgumentNullException.ThrowIfNull(command);
            _undoStack.Push(command);
            _redoStack.Clear();

            Trim(_undoStack);
            CommandExecuted?.Invoke(this, EventArgs.Empty);
        }

        public void ExecuteCommand(ICommandAction command)
        {
            ArgumentNullException.ThrowIfNull(command);
            command.Execute();
            Push(command);
        }

        public void Undo()
        {
            if (CanUndo)
            {
                var cmd = _undoStack.Peek();
                cmd.Undo();
                _undoStack.Pop();
                _redoStack.Push(cmd);
                CommandExecuted?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Redo()
        {
            if (CanRedo)
            {
                var cmd = _redoStack.Peek();
                cmd.Execute();
                _redoStack.Pop();
                _undoStack.Push(cmd);
                Trim(_undoStack);
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
