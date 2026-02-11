using System.Collections.Generic;
using RetroAchievements.Api;
using RetroAchievements.Api.Response.Users.Records;

namespace PGEmu.app;
using System.Collections;
public static class AchievementStorage
{
    public static Hashtable gameToString = new Hashtable();
    public static List<KeyValuePair<string, UserProgressAchievement>> achievementData = null;
    public static int gameId = -1;
    
    public static string gameName = "";
}