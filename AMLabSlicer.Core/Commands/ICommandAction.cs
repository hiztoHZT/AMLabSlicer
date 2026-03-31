namespace AMLabSlicer.Core.Commands
{
    /// <summary>
    /// 标准撤销/重做命令接口
    /// </summary>
    public interface ICommandAction
    {
        /// <summary>
        /// 命令名称（用于历史记录提示）
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 执行该动作
        /// </summary>
        void Execute();

        /// <summary>
        /// 撤销该动作
        /// </summary>
        void Undo();
    }
}
