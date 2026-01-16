using System;
using System.Collections.ObjectModel;
using PGEmu.app;

namespace PGEmu.gui.ViewModels;

public class ProfileScreenViewModel : ViewModelBase
{
    public ObservableCollection<string> Friends { get; } = new();
    public ProfileScreenViewModel()
    {
        Friends.Add("Friend A");
        Friends.Add("Friend B");
        Friends.Add("Friend C");
    }
   
    public void SwitchScreens(ViewModelBase vm)
    {
           
        mainWindowViewModel.SwitchScreens(vm);
            

    }
}