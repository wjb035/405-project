using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using PGEmu.gui.ViewModels;

namespace PGEmu.gui.Views;

public partial class ProfileScreenView : UserControl
{
    public ProfileScreenView()
    {
        InitializeComponent();
    }
    
    public void navigateHome(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ProfileScreenViewModel vm) {
            vm.SwitchScreens(vm.homeScreenViewModel);
            
        }
    }
}