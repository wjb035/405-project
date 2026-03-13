using System;
using System.Net;

namespace PGEmu.app;
using System.Collections.Generic;
using RetroAchievements.Api;
using RetroAchievements.Api.Response.Users.Records;
using System.Text.Json;
using System.Threading.Tasks;
using System.IO;

using System.Collections;
public static class PlaytimeStorage
{
    
    // maps the name of an emulator to the list of the games pf that platform that have a playtime tracked
    public static List<KeyValuePair<string, List<GameEntry>>> playtimeTracked = new List<KeyValuePair<string, List<GameEntry>>>();
    public static List<GameEntry> currentCollection = null;

    
   
}