using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using RetroAchievements.Api;
using Godot;
namespace PGEmu.app;

public static class RetroAchievementsService
{
    //requires the use of an async task function that we call when the platform is changed
    // NOTE! IF YOU ARE HAVING ISSUES WITH ANY OF THIS, IT'S LIKELY YOUR CONFIG.JSON FILE ISN'T LAID OUT PROPERLY. PLEASE LET ME KNOW
    // IF YOU NEED HELP WITH THIS!!!!!!!!!!!!!!!!!!!!!!
    static string username = "";
    static string apiKey = "";
			
    static RetroAchievementsHttpClient client = new RetroAchievementsHttpClient(new RetroAchievementsAuthenticationData(username, apiKey));
    public static async Task Retro(
        
        PlatformConfig? selectedPlatform,
        IEnumerable<GameEntry> games)
    {
        if (client == null) throw new ArgumentNullException(nameof(client));
        if (games == null) throw new ArgumentNullException(nameof(games));

        Console.WriteLine(selectedPlatform?.retroachievementsPlatformID);
       // load the list of games for the selected platform
       //if (selectedPlatform != null && selectedPlatform.retroachievementsPlatformID != -1)
       if (selectedPlatform != null)
       {
           //var gameList = await client.GetGamesListAsync(selectedPlatform.retroachievementsPlatformID, true);
           var gameList = await client.GetGamesListAsync(16, true);
           string pattern = @"[\s:-]";
           string pattern2 =  @"\([^)]*\)";


           foreach (var gamesItem in gameList.Items)
           {
               //GD.Print(gamesItem.Title);
           }
           // This loop looks nasty, but all it does is iterate through every single game in the retroachievements database 
           // for the given platform, and checks if we have that game. If we do have it, we will display the information given by 
           // retroachievements about that game next to it.

           foreach (var g in games)
           {
               //GD.Print(g.Title);
           }
           foreach (var g in games)
           {

               // Sanitizing the name of the game given in the files so that we can match it to a retroachievements title
               string userGameFileName = g.Title;
               userGameFileName = Regex.Replace(userGameFileName, pattern, String.Empty);
               userGameFileName = Regex.Replace(userGameFileName, pattern2, String.Empty);
               
               Console.WriteLine(userGameFileName);
               GD.Print("test!");

               foreach (var gamesItem in gameList.Items)
               {

                   // sanitizing the name of the retroachievements game so that it can be matched to by a user game file name
                   string retroAchievementGameName = gamesItem.Title;
                   retroAchievementGameName = Regex.Replace(gamesItem.Title, pattern, String.Empty);

                   // if the name of the user's game file contains the shorter and more concise retroachievements game name, then we have a match
                   if (userGameFileName == retroAchievementGameName)
                   {
                       GD.Print(userGameFileName);
                       GD.Print(retroAchievementGameName);
                       var disposableGame = await client.GetGameDataAndUserProgressAsync(gamesItem.Id, username);
                       g.AchievementNum = disposableGame.EarnedAchievementsCount + "/" +
                                          disposableGame.AchievementsCount;
                       //Console.WriteLine(g.AchievementNum);
                       GD.Print("aaron can you smell this");
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
           foreach (var g in games)
           {
               g.AchievementNum = "0/0";
           }
       }
    }
}
