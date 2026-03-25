using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace AMLabSlicer.Views
{
    /// <summary>
    /// ParameterPanelView.xaml 的交互逻辑
    /// </summary>
    public partial class ParameterPanelView : UserControl
    {
        public ParameterPanelView()
        {
            InitializeComponent();
        }
    }

    public class ParameterTemplateSelector : System.Windows.Controls.DataTemplateSelector
    {
        public System.Windows.DataTemplate? BooleanTemplate { get; set; }
        public System.Windows.DataTemplate? NumberTemplate { get; set; }
        public System.Windows.DataTemplate? EnumTemplate { get; set; }
        public System.Windows.DataTemplate? DefaultTemplate { get; set; }

        public override System.Windows.DataTemplate? SelectTemplate(object item, System.Windows.DependencyObject container)
        {
            if (item is AMLabSlicer.Core.Parameters.SliceParameter param)
            {
                if (param.ParameterType == typeof(bool)) return BooleanTemplate;
                if (param.ParameterType == typeof(int) || param.ParameterType == typeof(double) || param.ParameterType == typeof(float)) return NumberTemplate;
                if (param.ParameterType.IsEnum || param.Options != null) return EnumTemplate;
                return DefaultTemplate ?? base.SelectTemplate(item, container);
            }
            return base.SelectTemplate(item, container);
        }
    }
}
