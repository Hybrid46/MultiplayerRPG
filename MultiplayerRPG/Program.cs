using Raylib_cs;

namespace Labyrinth;

internal enum AppState { Menu, Join, Connecting, Playing, Error }

internal static class Program
{
    private const int ScreenW = 1100;
    private const int ScreenH = 650;
    private const int TileSize = 32;
    private const int MazeOffsetX = 50;
    private const int MazeOffsetY = 100;

    private static readonly Color[] PlayerColors =
    {
        new(255, 70, 70, 255),
        new(70, 230, 70, 255),
        new(240, 220, 60, 255),
        new(80, 120, 255, 255),
        new(230, 80, 230, 255),
        new(70, 220, 230, 255),
        new(240, 240, 240, 255),
    };

    private static AppState _state = AppState.Menu;
    private static string _errorMsg = "";
    private static string _ipInput = "127.0.0.1";
    private static string _localIp = "127.0.0.1";

    private static GameServer? _server;
    private static GameClient? _client;
    private static bool _isHost;

    // Async connect
    private static Task<bool>? _connectTask;
    private static string _connectError = "";

    // ----------------------------------------------------------- main
    private static void Main()
    {
        _localIp = NetUtil.GetLocalIp();

        Raylib.InitWindow(ScreenW, ScreenH, "Labyrinth");
        Raylib.SetTargetFPS(60);

        while (!Raylib.WindowShouldClose())
        {
            switch (_state)
            {
                case AppState.Menu:       UpdateMenu();       break;
                case AppState.Join:       UpdateJoin();       break;
                case AppState.Connecting: UpdateConnecting(); break;
                case AppState.Playing:    UpdatePlaying();    break;
                case AppState.Error:      UpdateError();      break;
            }
        }

        Cleanup();
        Raylib.CloseWindow();
    }

    // ----------------------------------------------------------- state transitions
    private static void Cleanup()
    {
        _client?.Dispose();
        _client = null;
        _server?.Dispose();
        _server = null;
        _isHost = false;
    }

    private static void ReturnToMenu()
    {
        Cleanup();
        _ipInput = "127.0.0.1";
        _errorMsg = "";
        _connectError = "";
        _connectTask = null;
        _state = AppState.Menu;
    }

    private static void StartHosting()
    {
        try
        {
            _server = new GameServer();
            _server.Start();
        }
        catch (Exception e)
        {
            _errorMsg = "Could not start server:\n" + e.Message +
                        "\n(Is port 5555 already in use?)";
            _state = AppState.Error;
            Cleanup();
            return;
        }

        // Immediately connect a client to our own server.
        try
        {
            _client = new GameClient();
            _client.Connect("127.0.0.1", 3000);
            _isHost = true;
            _state = AppState.Playing;
        }
        catch (Exception e)
        {
            _errorMsg = "Could not connect to local server:\n" + e.Message;
            _state = AppState.Error;
            Cleanup();
        }
    }

    private static void StartJoinConnect()
    {
        var host = _ipInput.Trim();
        if (host.Length == 0) return;

        _connectError = "";
        _state = AppState.Connecting;

        _connectTask = Task.Run(() =>
        {
            try
            {
                var c = new GameClient();
                c.Connect(host, 4000);
                _client = c;
                _isHost = false;
                return true;
            }
            catch (Exception e)
            {
                _connectError = e.Message;
                return false;
            }
        });
    }

    private static void UpdateConnecting()
    {
        if (_connectTask is null) { _state = AppState.Menu; return; }
        if (!_connectTask.IsCompleted) return;

        var ok = _connectTask.Result;
        _connectTask = null;

        if (ok) { _state = AppState.Playing; return; }

        _errorMsg = "Could not connect to " + _ipInput + ":\n" + _connectError;
        _state = AppState.Error;
        _client = null;
    }

    // ----------------------------------------------------------- menu
    private static void UpdateMenu()
    {
        Raylib.BeginDrawing();
        Raylib.ClearBackground(new Color(18, 18, 26, 255));

        DrawCentered("LABYRINTH", 80, 60, Color.White);

        var subtitle = $"Your IP: {_localIp}    •    Server port: {GameServer.Port}";
        DrawCenteredText(subtitle, 170, 20, new Color(140, 140, 170, 255));

        var host = new Rectangle((ScreenW - 300) / 2f, 260, 300, 60);
        var join = new Rectangle((ScreenW - 300) / 2f, 340, 300, 60);
        var quit = new Rectangle((ScreenW - 300) / 2f, 420, 300, 60);

        if (DrawButton(host, "Host Game")) StartHosting();
        if (DrawButton(join, "Join Game")) { _state = AppState.Join; }
        if (DrawButton(quit, "Quit"))      Raylib.CloseWindow();

        DrawCenteredText("W A S D to move  •  ESC to leave",
                         ScreenH - 50, 18, new Color(100, 100, 130, 255));

        Raylib.EndDrawing();
    }

    // ----------------------------------------------------------- join screen
    private static void UpdateJoin()
    {
        // Text field input
        int c;
        while ((c = Raylib.GetCharPressed()) != 0)
        {
            var ch = (char)c;
            if (_ipInput.Length < 40 && (char.IsLetterOrDigit(ch) || ch == '.' || ch == '-' || ch == ':'))
                _ipInput += ch;
        }
        if (Raylib.IsKeyPressed(KeyboardKey.Backspace) && _ipInput.Length > 0)
            _ipInput = _ipInput[..^1];
        if (Raylib.IsKeyPressed(KeyboardKey.Enter)) StartJoinConnect();
        if (Raylib.IsKeyPressed(KeyboardKey.Escape)) ReturnToMenu();

        Raylib.BeginDrawing();
        Raylib.ClearBackground(new Color(18, 18, 26, 255));

        DrawCentered("JOIN GAME", 100, 48, Color.White);
        DrawCenteredText("Enter the host's IP address", 180, 20, Color.LightGray);

        var field = new Rectangle((ScreenW - 440) / 2f, 260, 440, 60);
        Raylib.DrawRectangleRec(field, new Color(30, 30, 45, 255));
        Raylib.DrawRectangleLinesEx(field, 2, new Color(120, 140, 200, 255));
        Raylib.DrawText(_ipInput, (int)field.X + 15, (int)field.Y + 18, 26, Color.White);

        var connect = new Rectangle((ScreenW - 220) / 2f, 360, 220, 60);
        var back    = new Rectangle((ScreenW - 220) / 2f, 440, 220, 60);

        if (DrawButton(connect, "Connect")) StartJoinConnect();
        if (DrawButton(back, "Back"))       ReturnToMenu();

        Raylib.EndDrawing();
    }

    // ----------------------------------------------------------- error
    private static void UpdateError()
    {
        if (Raylib.IsKeyPressed(KeyboardKey.Escape)) ReturnToMenu();

        Raylib.BeginDrawing();
        Raylib.ClearBackground(new Color(30, 15, 20, 255));

        DrawCentered("ERROR", 100, 48, new Color(255, 120, 120, 255));

        var lines = _errorMsg.Split('\n');
        var y = 220;
        foreach (var l in lines)
        {
            DrawCenteredText(l, y, 22, Color.White);
            y += 34;
        }

        var back = new Rectangle((ScreenW - 220) / 2f, 460, 220, 60);
        if (DrawButton(back, "Back to Menu")) ReturnToMenu();

        Raylib.EndDrawing();
    }

    // ----------------------------------------------------------- playing
    private static void UpdatePlaying()
    {
        if (_client is null) { ReturnToMenu(); return; }

        // Leave on ESC
        if (Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            ReturnToMenu();
            return;
        }

        var s = _client.State;

        // Movement input
        if (s.Winner is null)
        {
            if      (Raylib.IsKeyPressed(KeyboardKey.W)) _client.SendMove('w');
            else if (Raylib.IsKeyPressed(KeyboardKey.A)) _client.SendMove('a');
            else if (Raylib.IsKeyPressed(KeyboardKey.S)) _client.SendMove('s');
            else if (Raylib.IsKeyPressed(KeyboardKey.D)) _client.SendMove('d');
        }

        Raylib.BeginDrawing();
        Raylib.ClearBackground(new Color(18, 18, 26, 255));

        // Header
        Raylib.DrawText("LABYRINTH", MazeOffsetX, 25, 34, Color.White);
        Raylib.DrawText("You:", 320, 34, 22, Color.LightGray);
        Raylib.DrawText(s.MySym, 372, 34, 22, ColorFor(s.MySym));
        Raylib.DrawText($"Players: {s.Players.Count}", 440, 34, 22, Color.LightGray);

        if (_isHost)
            Raylib.DrawText($"HOSTING on {_localIp}:{GameServer.Port}",
                            680, 34, 20, new Color(120, 200, 120, 255));

        if (!s.Initialized)
        {
            Raylib.DrawText("Waiting for server...", MazeOffsetX, 300, 24, Color.Gray);
            Raylib.EndDrawing();
            return;
        }

        // Tiles
        for (var y = 0; y < s.Maze.Length; y++)
        {
            var row = s.Maze[y];
            for (var x = 0; x < row.Length; x++)
            {
                var px = MazeOffsetX + x * TileSize;
                var py = MazeOffsetY + y * TileSize;
                if (row[x] == '#')
                {
                    Raylib.DrawRectangle(px, py, TileSize, TileSize, new Color(55, 55, 78, 255));
                    Raylib.DrawRectangleLines(px, py, TileSize, TileSize, new Color(80, 80, 110, 255));
                }
                else
                {
                    Raylib.DrawRectangle(px, py, TileSize, TileSize, new Color(12, 12, 20, 255));
                }
            }
        }

        // Exit
        var ex = MazeOffsetX + s.ExitX * TileSize;
        var ey = MazeOffsetY + s.ExitY * TileSize;
        Raylib.DrawRectangle(ex, ey, TileSize, TileSize, new Color(60, 200, 90, 255));
        Raylib.DrawText("E", ex + 9, ey + 3, 26, Color.Black);

        // Players
        foreach (var p in s.Players.Values)
        {
            var cx = MazeOffsetX + p.X * TileSize + TileSize / 2;
            var cy = MazeOffsetY + p.Y * TileSize + TileSize / 2;
            var col = ColorFor(p.Sym);

            Raylib.DrawCircle(cx, cy, TileSize / 2f - 4f, col);
            if (p.Id == s.MyId)
                Raylib.DrawCircleLines(cx, cy, TileSize / 2f - 3f, Color.White);

            var tw = Raylib.MeasureText(p.Sym, 18);
            Raylib.DrawText(p.Sym, cx - tw / 2, cy - 9, 18, Color.Black);
        }

        // Footer
        var fy = MazeOffsetY + 15 * TileSize + 20;
        if (s.Winner is not null)
        {
            var who = s.Winner.Pid == s.MyId ? "YOU" : $"Player {s.Winner.Sym}";
            Raylib.DrawText($"{who} reached the exit!  Press ESC to leave.",
                MazeOffsetX, fy, 24, new Color(80, 230, 100, 255));
        }
        else
        {
            Raylib.DrawText("Move: W A S D      Leave: ESC",
                MazeOffsetX, fy, 20, Color.LightGray);
        }

        Raylib.EndDrawing();
    }

    // ----------------------------------------------------------- helpers
    private static Color ColorFor(string sym)
    {
        if (string.IsNullOrEmpty(sym)) return PlayerColors[0];
        var c = sym[0];
        int idx = c switch
        {
            >= 'A' and <= 'Z' => c - 'A',
            >= '0' and <= '9' => 26 + (c - '0'),
            _ => 0,
        };
        return PlayerColors[idx % PlayerColors.Length];
    }

    private static bool DrawButton(Rectangle rect, string text)
    {
        var mouse = Raylib.GetMousePosition();
        var hover = Raylib.CheckCollisionPointRec(mouse, rect);
        var bg = hover ? new Color(70, 90, 150, 255) : new Color(45, 55, 90, 255);
        var border = hover ? new Color(160, 180, 240, 255) : new Color(120, 140, 200, 255);

        Raylib.DrawRectangleRec(rect, bg);
        Raylib.DrawRectangleLinesEx(rect, 2, border);

        var tw = Raylib.MeasureText(text, 24);
        Raylib.DrawText(text,
            (int)(rect.X + (rect.Width - tw) / 2),
            (int)(rect.Y + (rect.Height - 24) / 2),
            24, Color.White);

        return hover && Raylib.IsMouseButtonPressed(MouseButton.Left);
    }

    private static void DrawCentered(string text, int y, int fontSize, Color color)
    {
        var tw = Raylib.MeasureText(text, fontSize);
        Raylib.DrawText(text, (ScreenW - tw) / 2, y, fontSize, color);
    }

    private static void DrawCenteredText(string text, int y, int fontSize, Color color)
    {
        DrawCentered(text, y, fontSize, color);
    }
}