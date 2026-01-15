using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PGEmu.app;
using PGEmu.gui.Views;
using ReactiveUI;

namespace PGEmu.gui.ViewModels;


public partial class MainWindowViewModel : ViewModelBase
{
    
    public MainWindowViewModel()
    {
        _contentViewModel = new SplashScreenViewModel();
        
    }
    
    public bool loggedIn = false;
    private ViewModelBase _contentViewModel;
    public ViewModelBase ContentViewModel
    {
        get => _contentViewModel;
        private set => this.SetProperty(ref _contentViewModel, value);
    }
    
    
    public void SwitchScreens(ViewModelBase vm)
    {
        
        ContentViewModel = vm;
    }
    
}