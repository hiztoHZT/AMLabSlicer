using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace AMLabSlicer.Core.Parameters
{
    public class ParameterStore : IParameterStore
    {
        private readonly ConcurrentDictionary<string, SliceParameter> _parameters = new();

        public event EventHandler<string>? ParameterChanged;

        public void RegisterParameter(SliceParameter parameter)
        {
            _parameters[parameter.Key] = parameter;
        }

        public IEnumerable<SliceParameter> GetAllParameters()
        {
            return _parameters.Values;
        }

        public IEnumerable<SliceParameter> GetParametersByCategory(string category)
        {
            return _parameters.Values.Where(p => p.Category == category);
        }

        public T? GetParameter<T>(string key)
        {
            if (_parameters.TryGetValue(key, out var param) && param.Value is T typedValue)
            {
                return typedValue;
            }
            return default;
        }

        public void SetParameter<T>(string key, T value)
        {
            if (_parameters.TryGetValue(key, out var param))
            {
                param.Value = value;
                ParameterChanged?.Invoke(this, key);
            }
        }
    }
}
