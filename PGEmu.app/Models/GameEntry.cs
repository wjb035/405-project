using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PGEmu.app;

public partial class GameEntry : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Title => Name;

    [ObservableProperty]
    public string achievementNum = "Loading...";

    public int retroAchievementsGameId = -1;

    public override string ToString() => Title;
}
