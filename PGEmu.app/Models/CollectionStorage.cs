namespace PGEmu.app;
using System.Collections.Generic;
using RetroAchievements.Api;
using RetroAchievements.Api.Response.Users.Records;

using System.Collections;
public static class CollectionStorage
{
    public static List<KeyValuePair<string, List<GameEntry>>> collections = new List<KeyValuePair<string, List<GameEntry>>>();
}