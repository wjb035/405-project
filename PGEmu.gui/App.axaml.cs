using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using PGEmu.gui.ViewModels;
using PGEmu.gui.Views;

namespace PGEmu.gui;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            
            // create view model for each screen
            var mainWindowViewModel = new MainWindowViewModel();
            mainWindowViewModel.mainWindowViewModel = mainWindowViewModel;
            
            
            var homeScreenViewModel = new HomeScreenViewModel();
            
            homeScreenViewModel.mainWindowViewModel = mainWindowViewModel;
            
            var loginViewModel = new LoginViewModel();
            loginViewModel.homeScreenViewModel = homeScreenViewModel;
            loginViewModel.mainWindowViewModel = mainWindowViewModel;


            var gameScreenViewModel = new GameScreenViewModel();
            gameScreenViewModel.homeScreenViewModel = homeScreenViewModel;
            gameScreenViewModel.mainWindowViewModel = mainWindowViewModel;
            gameScreenViewModel.loginViewModel = loginViewModel;
            
            
            
            var profileScreenViewModel = new ProfileScreenViewModel();
            profileScreenViewModel.mainWindowViewModel = mainWindowViewModel;
            profileScreenViewModel.homeScreenViewModel = homeScreenViewModel;
            
            homeScreenViewModel.ProfileScreenViewModel = profileScreenViewModel;
            homeScreenViewModel.GameScreenViewModel = gameScreenViewModel;
            homeScreenViewModel.loginViewModel = loginViewModel;

            var userSettingsViewModel = new UserSettingsViewModel();
            userSettingsViewModel.mainWindowViewModel = mainWindowViewModel;
            userSettingsViewModel.homeScreenViewModel = homeScreenViewModel;
            
            homeScreenViewModel.userSettingsViewModel = userSettingsViewModel;
            

            var mainWindow = new MainWindow
            {
                DataContext = mainWindowViewModel,
            };
            
            desktop.MainWindow = mainWindow;
            desktop.MainWindow.Show();
            
            //await Task.Delay(3000);
            
            mainWindowViewModel.SwitchScreens(homeScreenViewModel);


        }
        

        base.OnFrameworkInitializationCompleted();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}