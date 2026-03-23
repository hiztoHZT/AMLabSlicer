using System.Windows.Controls;
using HelixToolkit.SharpDX;

// 1. 引入命名空间
using HelixToolkit.Wpf.SharpDX;

namespace AMLabSlicer.Views
{
    public partial class ModelPreviewView : UserControl
    {
        public ModelPreviewView()
        {
            InitializeComponent();
            MainViewport.EffectsManager = new DefaultEffectsManager();
        }
    }
}