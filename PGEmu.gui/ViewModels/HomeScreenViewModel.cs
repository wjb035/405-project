using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PGEmu.app;
using RetroAchievements.Api;
using PGEmu.gui.Views;
using RetroAchievements.Api.Requests.Users;
using RetroAchievements.Api.Response.Users;


namespace PGEmu.gui.ViewModels;

public partial class HomeScreenViewModel : ViewModelBase
{
    public HomeScreenViewModel()
    {
        LoadConfigAndPlatforms();
        
        // these belong to me, keeping these a secret
        string username = "";
        string apiKey = "";
        RetroAchievementsHttpClient client = new RetroAchievementsHttpClient(new RetroAchievementsAuthenticationData(username, apiKey));
        retro(client);
        
    }


    
    public ViewModelBase userSettingsViewModel { get; set; }
    
    public ObservableCollection<PlatformConfig> Platforms { get; } = new();
    public ObservableCollection<GameEntry> Games { get; } = new();

    [ObservableProperty] private PlatformConfig? selectedPlatform;

   

    [ObservableProperty] private GameEntry? selectedGame;

    [ObservableProperty] private string status = "";

    private AppConfig? _config;

    async Task retro(RetroAchievementsHttpClient client)
    {  
        
        
        //var response = await client.GetAchievementsEarnedOnDayAsync("badacctname", DateTime.Now);
        //var response = await client.GetConsoleIdsAsync();
        //var response = await client.GetGamesListAsync(21, true);
        var response = await client.GetGameDataAndUserProgressAsync(2689, "badacctname");
        //foreach (var achievement in response.)
        //{
        //Console.WriteLine($"[{achievement.Id}] {achievement.Name}");
            
        Console.WriteLine(response.EarnedAchievementsCount);
        Console.WriteLine(response.Title);
        subStrHelp(client, response);
        foreach (var p in Platforms)
        {
            var list = LibraryScanner.Scan(p, _config.LibraryRoot);
            foreach (var g in list)
            {
                if (g.Title.Contains(response.Title))
                {
                    Console.WriteLine(response.Title + " is part of " + g.Title);
                    Console.WriteLine("User has " + response.EarnedAchievementsCount + " achievements unlocked out of " + response.AchievementsCount);
                }
                else
                {
                    Console.WriteLine(response.Title + " is not part of " + g.Title);
                }
                //Console.WriteLine(g.Title);
            }
            
        }
        //}

    }

    private void subStrHelp(RetroAchievementsHttpClient client, GetGameDataAndUserProgressResponse response)
    {
        
    }
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