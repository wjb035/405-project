namespace PGEmu.app;

public class GameEntry
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Title => Name;

    public override string ToString() => Title;
}
