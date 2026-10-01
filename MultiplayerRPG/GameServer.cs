using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Labyrinth;

public sealed class GameServer : IDisposable
{
    public const int Port = 5555;
    private const int W = 31, H = 15;
    private const string Symbols = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    private static readonly Dictionary<char, (int dx, int dy)> Dirs = new()
    {
        ['w'] = (0, -1), ['s'] = (0, 1), ['a'] = (-1, 0), ['d'] = (1, 0),
    };

    private sealed class Player
    {
        public int X, Y;
        public string Sym = "?";
        public TcpClient Client = null!;
    }

    private readonly object _lock = new();
    private readonly Dictionary<int, Player> _players = new();
    private readonly Random _rng = new();
    private int _nextId;
    private WinnerInfo? _winner;
    private int[,] _maze = new int[W, H];

    private TcpListener? _listener;
    private volatile bool _stopping;

    public string LocalIp { get; private set; } = "127.0.0.1";

    public void Start()
    {
        _maze = GenerateMaze(W, H);
        _listener = new TcpListener(IPAddress.Any, Port);
        _listener.Start();
        LocalIp = NetUtil.GetLocalIp();
        new Thread(AcceptLoop) { IsBackground = true }.Start();
    }

    public void Dispose()
    {
        _stopping = true;
        try { _listener?.Stop(); } catch { }
        lock (_lock)
        {
            foreach (var p in _players.Values)
                try { p.Client.Close(); } catch { }
            _players.Clear();
        }
    }

    // ------------------------------------------------------------- accept
    private void AcceptLoop()
    {
        try
        {
            while (!_stopping)
            {
                var client = _listener!.AcceptTcpClient();
                if (_stopping) { client.Close(); break; }
                new Thread(() => HandleClient(client)) { IsBackground = true }.Start();
            }
        }
        catch { /* listener stopped */ }
    }

    // ------------------------------------------------------------- maze
    private int[,] GenerateMaze(int w, int h)
    {
        var grid = new int[w, h];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                grid[x, y] = 1;
        grid[1, 1] = 0;

        var stack = new Stack<(int x, int y)>();
        stack.Push((1, 1));
        int[] dxs = { 2, -2, 0, 0 };
        int[] dys = { 0, 0, 2, -2 };

        while (stack.Count > 0)
        {
            var (x, y) = stack.Peek();
            var opts = new List<(int nx, int ny, int dx, int dy)>();
            for (var i = 0; i < 4; i++)
            {
                var nx = x + dxs[i];
                var ny = y + dys[i];
                if (nx > 0 && nx < w - 1 && ny > 0 && ny < h - 1 && grid[nx, ny] == 1)
                    opts.Add((nx, ny, dxs[i], dys[i]));
            }

            if (opts.Count > 0)
            {
                var (nx, ny, dx, dy) = opts[_rng.Next(opts.Count)];
                grid[x + dx / 2, y + dy / 2] = 0;
                grid[nx, ny] = 0;
                stack.Push((nx, ny));
            }
            else stack.Pop();
        }
        return grid;
    }

    private (int x, int y)? PickSpawn()
    {
        var cells = new List<(int x, int y)>();
        for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
                if (_maze[x, y] == 0)
                    cells.Add((x, y));

        for (var i = cells.Count - 1; i > 0; i--)
        {
            var j = _rng.Next(i + 1);
            (cells[i], cells[j]) = (cells[j], cells[i]);
        }

        var taken = _players.Values.Select(p => (p.X, p.Y)).ToHashSet();
        var goal = (W - 2, H - 2);
        foreach (var c in cells)
            if (!taken.Contains(c) && c != goal)
                return c;
        return null;
    }

    // ------------------------------------------------------------- send / broadcast
    private static void Send(TcpClient client, object obj)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(obj) + "\n");
            client.GetStream().Write(bytes, 0, bytes.Length);
        }
        catch { }
    }

    private void Broadcast()
    {
        Dictionary<string, object?> state;
        List<TcpClient> targets;

        lock (_lock)
        {
            var dict = new Dictionary<string, object[]>();
            foreach (var (pid, p) in _players)
                dict[pid.ToString()] = new object[] { p.X, p.Y, p.Sym };

            state = new()
            {
                ["type"]    = "state",
                ["players"] = dict,
                ["winner"]  = _winner is null ? null : new object[] { _winner.Pid, _winner.Sym },
            };
            targets = _players.Values.Select(p => p.Client).ToList();
        }

        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(state) + "\n");
        foreach (var t in targets)
            try { t.GetStream().Write(bytes, 0, bytes.Length); } catch { }
    }

    private void DoMove(int pid, char cmd)
    {
        if (!Dirs.TryGetValue(cmd, out var d)) return;

        lock (_lock)
        {
            if (!_players.TryGetValue(pid, out var p)) return;
            if (_winner is not null) return;

            var nx = p.X + d.dx;
            var ny = p.Y + d.dy;
            if (nx < 0 || nx >= W || ny < 0 || ny >= H) return;
            if (_maze[nx, ny] == 1) return;

            p.X = nx; p.Y = ny;
            if (nx == W - 2 && ny == H - 2)
                _winner = new WinnerInfo { Pid = pid, Sym = p.Sym };
        }
        Broadcast();
    }

    // ------------------------------------------------------------- client thread
    private void HandleClient(TcpClient client)
    {
        var pid = -1;
        var sym = "?";

        try
        {
            lock (_lock)
            {
                var spawn = PickSpawn();
                if (spawn is null)
                {
                    Send(client, new Dictionary<string, object?>
                    {
                        ["type"] = "error",
                        ["msg"]  = "No free spawn point.",
                    });
                    return;
                }

                pid = _nextId++;
                sym = Symbols[pid % Symbols.Length].ToString();
                _players[pid] = new Player
                {
                    X = spawn.Value.x, Y = spawn.Value.y, Sym = sym, Client = client,
                };

                var lines = new List<string>(H);
                for (var y = 0; y < H; y++)
                {
                    var sb = new StringBuilder(W);
                    for (var x = 0; x < W; x++) sb.Append(_maze[x, y] == 1 ? '#' : '.');
                    lines.Add(sb.ToString());
                }

                Send(client, new Dictionary<string, object?>
                {
                    ["type"]   = "init",
                    ["id"]     = pid,
                    ["sym"]    = sym,
                    ["width"]  = W,
                    ["height"] = H,
                    ["maze"]   = lines,
                    ["exit"]   = new[] { W - 2, H - 2 },
                    ["winner"] = _winner is null ? null : new object[] { _winner.Pid, _winner.Sym },
                });
            }

            Broadcast();

            var stream = client.GetStream();
            var buffer = new byte[1024];
            var acc = new StringBuilder();
            while (!_stopping)
            {
                int n;
                try { n = stream.Read(buffer, 0, buffer.Length); }
                catch { break; }
                if (n <= 0) break;

                acc.Append(Encoding.UTF8.GetString(buffer, 0, n));
                var s = acc.ToString();
                int idx;
                while ((idx = s.IndexOf('\n')) >= 0)
                {
                    var line = s[..idx].Trim().ToLowerInvariant();
                    s = s[(idx + 1)..];
                    if (line.Length > 0) DoMove(pid, line[0]);
                }
                acc.Clear();
                acc.Append(s);
            }
        }
        catch { }
        finally
        {
            if (pid >= 0)
            {
                lock (_lock) { _players.Remove(pid); }
                Broadcast();
            }
            try { client.Close(); } catch { }
        }
    }
}