using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using PGEmu.gui.ViewModels;

namespace PGEmu.gui.Views;

public partial class HomeScreenView : UserControl
{
    public HomeScreenView()
    {
        InitializeComponent();
    }
    public void navigateLogin(object? sender, RoutedEventArgs e)
    {
        if (DataContext is HomeScreenViewModel vm) {
            vm.SwitchScreens(vm.GameScreenViewModel);
            
        }
    }
    
    public void navigateProfile(object? sender, RoutedEventArgs e)
    {
        if (DataContext is HomeScreenViewModel vm) {
            vm.SwitchScreens(vm.ProfileScreenViewModel);
            
        }
    }

   
}