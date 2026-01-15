using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.Input;
using PGEmu.gui.ViewModels;

namespace PGEmu.gui.Views;

public partial class GameScreenView : UserControl
{
    public GameScreenView()
    {
        InitializeComponent();
    }


    public void SwitchBack(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GameScreenViewModel vm)
        {
            vm.SwitchScreens(vm.homeScreenViewModel);
        }
    }
}