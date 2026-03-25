using System;

namespace AMLabSlicer.Core.Parameters
{
    public class SliceParameter
    {
        public string Key { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public Type ParameterType { get; set; } = typeof(object);
        public object? Value { get; set; }

        // 对于枚举或者其他特定的属性（可选）
        public object[]? Options { get; set; }
    }
}
