using System.Net;
using System.Net.Sockets;

namespace Labyrinth;

public static class NetUtil
{
    public static string GetLocalIp()
    {
        try
        {
            using var s = new Socket(AddressFamily.InterNetwork,
                                     SocketType.Dgram, ProtocolType.Udp);
            s.Connect("8.8.8.8", 80);
            return ((IPEndPoint)s.LocalEndPoint!).Address.ToString();
        }
        catch { return "127.0.0.1"; }
    }
}