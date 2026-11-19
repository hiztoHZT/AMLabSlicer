using System.Windows;
namespace AMLabSlicer.Views;
public partial class PresetNameWindow : ThemedWindow
{
 public string Result => NameInput.Text.Trim();
 public PresetNameWindow(string title,string prompt,string? name)
 {
  InitializeComponent();Title=title;PromptText.Text=prompt;
  if(name==null) NameInput.Visibility=Visibility.Collapsed;
  else {NameInput.Text=name;Loaded+=(_,_)=>{NameInput.Focus();NameInput.SelectAll();};}
 }
 private void Accept_Click(object sender,RoutedEventArgs e)
 {
  if(NameInput.Visibility==Visibility.Visible && string.IsNullOrWhiteSpace(Result))return;
  DialogResult=true;
 }
}
