using System;
using CommunityToolkit.Mvvm.ComponentModel;
using ReactiveUI;

namespace PGEmu.gui.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    public MainWindowViewModel mainWindowViewModel { get; set; }
    public ViewModelBase homeScreenViewModel { get; set; }
    public ViewModelBase loginViewModel { get; set; }
    public ViewModelBase ProfileScreenViewModel { get; set; }
    public ViewModelBase GameScreenViewModel { get; set; }
}
