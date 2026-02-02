using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
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
    // retroachievements api requires a username and api key to access the client
    public static string username = "";
    static string apiKey = "";
    public RetroAchievementsHttpClient client = new RetroAchievementsHttpClient(new RetroAchievementsAuthenticationData(username, apiKey));
    public HomeScreenViewModel()
    {
        LoadConfigAndPlatforms();
        
        // these belong to me, keeping these a secret
        
        retro(client);
        
    }


    
    public ViewModelBase userSettingsViewModel { get; set; }
    
    public ObservableCollection<PlatformConfig> Platforms { get; } = new();
    public ObservableCollection<WindowPlatformItem> WindowPlatforms { get; } = new();
    public ObservableCollection<GameEntry> Games { get; } = new();
   
    [ObservableProperty] private PlatformConfig? selectedPlatform;
    [ObservableProperty] private WindowPlatformItem? selectedWindowPlatform;

   

    [ObservableProperty] private GameEntry? selectedGame;

    [ObservableProperty] private string status = "";

    private AppConfig? _config;

    //requires the use of an async task function that we call when the platform is changed
    // NOTE! IF YOU ARE HAVING ISSUES WITH ANY OF THIS, IT'S LIKELY YOUR CONFIG.JSON FILE ISN'T LAID OUT PROPERLY. PLEASE LET ME KNOW
    // IF YOU NEED HELP WITH THIS!!!!!!!!!!!!!!!!!!!!!!
    async Task retro(RetroAchievementsHttpClient client)
    {  
        
        
        Console.WriteLine(selectedPlatform.retroachievementsPlatformID);
       // load the list of games for the selected platform
       if (SelectedPlatform.retroachievementsPlatformID != -1)
       {
           var gameList = await client.GetGamesListAsync(SelectedPlatform.retroachievementsPlatformID, true);
           string pattern = @"[\s:-]";
           string pattern2 =  @"\([^)]*\)";

           // This loop looks nasty, but all it does is iterate through every single game in the retroachievements database 
           // for the given platform, and checks if we have that game. If we do have it, we will display the information given by 
           // retroachievements about that game next to it.
           foreach (var g in Games)
           {

               // Sanitizing the name of the game given in the files so that we can match it to a retroachievements title
               string userGameFileName = g.Title;
               userGameFileName = Regex.Replace(userGameFileName, pattern, String.Empty);
               userGameFileName = Regex.Replace(userGameFileName, pattern2, String.Empty);
               
               Console.WriteLine(userGameFileName);


               foreach (var games in gameList.Items)
               {

                   // sanitizing the name of the retroachievements game so that it can be matched to by a user game file name
                   string retroAchievementGameName = games.Title;
                   retroAchievementGameName = Regex.Replace(games.Title, pattern, String.Empty);

                   // if the name of the user's game file contains the shorter and more concise retroachievements game name, then we have a match
                   if (userGameFileName == retroAchievementGameName)
                   {

                       var disposableGame = await client.GetGameDataAndUserProgressAsync(games.Id, "");
                       g.AchievementNum = disposableGame.EarnedAchievementsCount + "/" +
                                          disposableGame.AchievementsCount;
                       //Console.WriteLine(g.AchievementNum);

                       break;
                   }
                   else
                   {
                       // if it can't detect that we have a game, just display a 0/0
                       g.AchievementNum = "0/0";
                   }
                   //Console.WriteLine(g.Title);

               }
           }

           //}

        }
       else
       {
           foreach (var g in Games)
           {
               g.AchievementNum = "0/0";
           }
       }
      
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
            BuildWindowPlatforms();
           
        }
        catch (Exception ex)
        {
            Status = $"Config load failed: {ex.Message}";
        }
    }

    partial void OnSelectedPlatformChanged(PlatformConfig? value)
    {
        LoadGames();
       _ = retro(client);
       BuildWindowPlatforms();
        
        
    }

    partial void OnSelectedGameChanged(GameEntry? value)
    {
        PlayCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedWindowPlatformChanged(WindowPlatformItem? value)
    {
        if (value?.Platform == null) return;
        if (SelectedPlatform == value.Platform && value.Slot == 1) return;

        if (value.Slot == 1)
            return;

        var currentIndex = Platforms.IndexOf(SelectedPlatform);
        if (currentIndex < 0) return;

        var nextIndex = value.Slot == 2
            ? (currentIndex + 1) % Platforms.Count
            : (currentIndex - 1 + Platforms.Count) % Platforms.Count;

        SelectedPlatform = Platforms[nextIndex];
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

    public void MoveWindow(int delta)
    {
        if (Platforms.Count == 0) return;

        var current = SelectedPlatform ?? Platforms.FirstOrDefault();
        if (current == null) return;

        var currentIndex = Platforms.IndexOf(current);
        if (currentIndex < 0) currentIndex = 0;

        var nextIndex = (currentIndex + delta) % Platforms.Count;
        if (nextIndex < 0) nextIndex += Platforms.Count;

        SelectedPlatform = Platforms[nextIndex];
    }
   

    private void BuildWindowPlatforms()
    {
        if (Platforms.Count == 0)
        {
            WindowPlatforms.Clear();
            SelectedWindowPlatform = null;
            return;
        }

        var current = SelectedPlatform ?? Platforms.FirstOrDefault();
        if (current == null) return;

        var currentIndex = Platforms.IndexOf(current);
        if (currentIndex < 0) currentIndex = 0;

        var prevIndex = (currentIndex - 1 + Platforms.Count) % Platforms.Count;
        var nextIndex = (currentIndex + 1) % Platforms.Count;

        var prevItem = new WindowPlatformItem(Platforms[prevIndex], 0);
        var currentItem = new WindowPlatformItem(Platforms[currentIndex], 1);
        var nextItem = new WindowPlatformItem(Platforms[nextIndex], 2);

        if (WindowPlatforms.Count == 3)
        {
            WindowPlatforms[0] = prevItem;
            WindowPlatforms[1] = currentItem;
            WindowPlatforms[2] = nextItem;
        }
        else
        {
            WindowPlatforms.Clear();
            WindowPlatforms.Add(prevItem);
            WindowPlatforms.Add(currentItem);
            WindowPlatforms.Add(nextItem);
        }

        SelectedWindowPlatform = WindowPlatforms[1];
    }

    public sealed class WindowPlatformItem
    {
        public WindowPlatformItem(PlatformConfig platform, int slot)
        {
            Platform = platform;
            Slot = slot;
        }

        public PlatformConfig Platform { get; }
        public int Slot { get; }
    }

}
