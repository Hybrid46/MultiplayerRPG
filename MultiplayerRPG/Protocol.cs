namespace Labyrinth;

public sealed class PlayerInfo
{
    public int Id;
    public int X;
    public int Y;
    public string Sym = "?";
}

public sealed class WinnerInfo
{
    public int Pid;
    public string Sym = "";
}

/// <summary>Immutable snapshot handed to the renderer.</summary>
public sealed class GameState
{
    public int MyId = -1;
    public string MySym = "?";
    public string[] Maze = Array.Empty<string>();
    public int ExitX;
    public int ExitY;
    public Dictionary<int, PlayerInfo> Players = new();
    public WinnerInfo? Winner;
    public bool Initialized;
}