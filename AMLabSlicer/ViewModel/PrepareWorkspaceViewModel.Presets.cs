using System;
using System.Collections.ObjectModel;
using System.Linq;
using AMLabSlicer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
namespace AMLabSlicer.ViewModel;
public partial class PrepareWorkspaceViewModel
{
 public ObservableCollection<SlicePreset> ParameterPresets {get;}=new();
 [ObservableProperty] private SlicePreset? _selectedParameterPreset;
 private string _presetScope="";
 private bool _loadingPresets;
 partial void OnSelectedParameterPresetChanged(SlicePreset? value)
 {
  if(_loadingPresets || value==null || SelectedAlgorithm==null)return;
  try {_slicePresets.Select(_presetScope,value.Id);_ = RebuildParametersForAlgorithmAsync(SelectedAlgorithm.AlgorithmId);}
  catch(Exception ex){_dialogs.ShowMessage(ex.Message);}
 }
 [RelayCommand]
 private void NewParameterPreset()
 {
  if (_parametersLoading || SelectedAlgorithm is not { } algorithm ||
      algorithm.AlgorithmId != _activeParameterAlgorithmId || string.IsNullOrEmpty(_presetScope)) return;

  var name = _dialogs.PromptPresetName("新建参数配置", "新参数配置");
  if (name == null) return;

  try
  {
   // With no overrides, loading this preset uses the algorithm template defaults.
   _slicePresets.Create(_presetScope, name, new());
   _ = RebuildParametersForAlgorithmAsync(algorithm.AlgorithmId);
  }
  catch (Exception ex) { _dialogs.ShowMessage(ex.Message); }
 }
 [RelayCommand] private void RenameParameterPreset()
 {
  if(SelectedParameterPreset==null)return;
  var name=_dialogs.PromptPresetName("重命名参数配置",SelectedParameterPreset.Name);if(name==null)return;
  try {_slicePresets.Rename(_presetScope,SelectedParameterPreset.Id,name);_ = RebuildParametersForAlgorithmAsync(SelectedAlgorithm!.AlgorithmId);}
  catch(Exception ex){_dialogs.ShowMessage(ex.Message);}
 }
 [RelayCommand] private void DeleteParameterPreset()
 {
  if(SelectedParameterPreset==null)return;
  if(ParameterPresets.Count<=1){_dialogs.ShowMessage("至少保留一个参数配置。");return;}
  if(!_dialogs.ConfirmPresetDelete(SelectedParameterPreset.Name))return;
  try {_slicePresets.Delete(_presetScope,SelectedParameterPreset.Id);_ = RebuildParametersForAlgorithmAsync(SelectedAlgorithm!.AlgorithmId);}
  catch(Exception ex){_dialogs.ShowMessage(ex.Message);}
 }
}
