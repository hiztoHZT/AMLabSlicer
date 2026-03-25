using System;
using System.Collections.Generic;

namespace AMLabSlicer.Core.Parameters
{
    public interface IParameterStore
    {
        event EventHandler<string>? ParameterChanged;

        void SetParameter<T>(string key, T value);
        T? GetParameter<T>(string key);
        IEnumerable<SliceParameter> GetParametersByCategory(string category);
        IEnumerable<SliceParameter> GetAllParameters();
        void RegisterParameter(SliceParameter parameter);
    }
}
