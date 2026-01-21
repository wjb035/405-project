using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PGEmu.app;
using PGEmu.gui.Views;

namespace PGEmu.gui.ViewModels;

public partial class HomeScreenViewModel : ViewModelBase
{
    public HomeScreenViewModel()
    {
        LoadConfigAndPlatforms();
    }
    
    public ViewModelBase userSettingsViewModel { get; set; }
    
    public ObservableCollection<PlatformConfig> Platforms { get; } = new();
    public ObservableCollection<GameEntry> Games { get; } = new();

    [ObservableProperty] private PlatformConfig? selectedPlatform;

   

    [ObservableProperty] private GameEntry? selectedGame;

    [ObservableProperty] private string status = "";

    private AppConfig? _config;
    
    
    private void LoadConfigAndPlatforms()
    {
        try
        {
            var cfgPath = ConfigFinder.FindConfigPath();
            if (cfgPath == null)
            {
                Status = "config.json not found. Put it in the Capstone root or next to the built app.";
                return;
            }

            _config = AppConfig.Load(cfgPath);
            Status = $"Loaded config: {cfgPath}";

            Platforms.Clear();
            foreach (var p in _config.Platforms)
                Platforms.Add(p);

            SelectedPlatform = Platforms.FirstOrDefault();
           
        }
        catch (Exception ex)
        {
            Status = $"Config load failed: {ex.Message}";
        }
    }

    partial void OnSelectedPlatformChanged(PlatformConfig? value)
    {
        LoadGames();
        
    }

    partial void OnSelectedGameChanged(GameEntry? value)
    {
        PlayCommand.NotifyCanExecuteChanged();
    }

    private void LoadGames()
    {
        Games.Clear();
        SelectedGame = null;

        if (_config == null || SelectedPlatform == null) return;

        try
        {
            var list = LibraryScanner.Scan(SelectedPlatform, _config.LibraryRoot);
            foreach (var g in list) Games.Add(g);

            Status = $"{SelectedPlatform.Name}: {Games.Count} game(s)";
        }
        catch (Exception ex)
        {
            Status = $"Scan failed: {ex.Message}";
        }
    }

    private bool CanPlay()
    {
        return SelectedGame != null;
    }

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private void Play()
    {
        if (_config == null || SelectedPlatform == null || SelectedGame == null) return;

        try
        {
            Launcher.LaunchFromConfig(_config, SelectedPlatform, SelectedGame);
        }
        catch (Exception ex)
        {
            Status = $"Launch failed: {ex.Message}";
        }
    }
    
    public void SwitchScreens(ViewModelBase vm)
    {
           
            mainWindowViewModel.SwitchScreens(vm);
            

    }
   

}