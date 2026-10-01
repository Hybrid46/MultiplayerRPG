using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Labyrinth;

public sealed class GameClient : IDisposable
{
    public const int Port = 5555;

    private TcpClient? _sock;
    private volatile bool _running;
    private volatile GameState _state = new();
    private string _connectError = "";

    public GameState State => _state;

    public void Connect(string host, int timeoutMs = 4000)
    {
        var sock = new TcpClient();
        var task = sock.ConnectAsync(host, Port);
        if (!task.Wait(timeoutMs))
        {
            sock.Dispose();
            throw new TimeoutException($"Timed out connecting to {host}:{Port}");
        }

        _sock = sock;
        _running = true;
        new Thread(NetLoop) { IsBackground = true }.Start();
    }

    public void Dispose()
    {
        _running = false;
        try { _sock?.Close(); } catch { }
        try { _sock?.Dispose(); } catch { }
        _sock = null;
    }

    public void SendMove(char cmd)
    {
        var s = _sock;
        if (s is null) return;
        try
        {
            var bytes = Encoding.UTF8.GetBytes(cmd + "\n");
            s.GetStream().Write(bytes, 0, bytes.Length);
        }
        catch { _running = false; }
    }

    // ------------------------------------------------------------- net loop
    private void NetLoop()
    {
        try
        {
            var stream = _sock!.GetStream();
            var buffer = new byte[8192];
            var acc = new StringBuilder();

            while (_running)
            {
                var n = stream.Read(buffer, 0, buffer.Length);
                if (n <= 0) break;

                acc.Append(Encoding.UTF8.GetString(buffer, 0, n));
                var s = acc.ToString();
                int idx;
                while ((idx = s.IndexOf('\n')) >= 0)
                {
                    var line = s[..idx];
                    s = s[(idx + 1)..];
                    if (!string.IsNullOrWhiteSpace(line)) ProcessMessage(line);
                }
                acc.Clear();
                acc.Append(s);
            }
        }
        catch { }
        finally { _running = false; }
    }

    private void ProcessMessage(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();

            switch (type)
            {
                case "init":
                {
                    var ns = new GameState
                    {
                        MyId        = root.GetProperty("id").GetInt32(),
                        MySym       = root.GetProperty("sym").GetString() ?? "?",
                        ExitX       = root.GetProperty("exit")[0].GetInt32(),
                        ExitY       = root.GetProperty("exit")[1].GetInt32(),
                        Initialized = true,
                        Winner      = ReadWinner(root),
                    };
                    ns.Maze = root.GetProperty("maze").EnumerateArray()
                        .Select(e => e.GetString() ?? "").ToArray();
                    _state = ns;
                    break;
                }
                case "state":
                {
                    var old = _state;
                    var ns = new GameState
                    {
                        MyId = old.MyId, MySym = old.MySym,
                        Maze = old.Maze, ExitX = old.ExitX, ExitY = old.ExitY,
                        Initialized = old.Initialized,
                    };
                    foreach (var prop in root.GetProperty("players").EnumerateObject())
                    {
                        var pid = int.Parse(prop.Name);
                        var arr = prop.Value;
                        ns.Players[pid] = new PlayerInfo
                        {
                            Id  = pid,
                            X   = arr[0].GetInt32(),
                            Y   = arr[1].GetInt32(),
                            Sym = arr[2].GetString() ?? "?",
                        };
                    }
                    ns.Winner = ReadWinner(root);
                    _state = ns;
                    break;
                }
                case "error":
                    _connectError = root.GetProperty("msg").GetString() ?? "";
                    _running = false;
                    break;
            }
        }
        catch { /* ignore malformed line */ }
    }

    private static WinnerInfo? ReadWinner(JsonElement root)
    {
        if (!root.TryGetProperty("winner", out var w)) return null;
        if (w.ValueKind != JsonValueKind.Array || w.GetArrayLength() < 2) return null;
        return new WinnerInfo
        {
            Pid = w[0].GetInt32(),
            Sym = w[1].GetString() ?? "",
        };
    }
}