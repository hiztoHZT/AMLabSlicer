using AMLabSlicer.Core.Parameters;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace AMLabSlicer.ViewModel
{
    public partial class ParameterCategoryGroup : ObservableObject
    {
        public ParameterCategoryGroup(string name)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "未分类" : name;
        }

        public string Name { get; }

        public ObservableCollection<ParameterSubcategoryGroup> Subcategories { get; } = new();

        [ObservableProperty]
        private bool _isSelected;

        public ParameterSubcategoryGroup GetOrAddSubcategory(string name, bool isExpanded)
        {
            var displayName = string.IsNullOrWhiteSpace(name) ? "常规" : name;
            foreach (var group in Subcategories)
            {
                if (group.Name == displayName)
                    return group;
            }

            var created = new ParameterSubcategoryGroup(displayName, isExpanded);
            Subcategories.Add(created);
            return created;
        }
    }

    public partial class ParameterSubcategoryGroup : ObservableObject
    {
        public ParameterSubcategoryGroup(string name, bool isExpanded)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "常规" : name;
            _isExpanded = isExpanded;
            ToggleCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
        }

        public string Name { get; }

        public ObservableCollection<SliceParameter> Parameters { get; } = new();

        public RelayCommand ToggleCommand { get; }

        public string ToggleText => IsExpanded ? "-" : "+";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ToggleText))]
        private bool _isExpanded = true;
    }
}
