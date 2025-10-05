
using System.Net;
using System.Net.Sockets;

namespace ATVCompanion.Core.Networking;

public static class WolClient
{
    public static void Wake(string mac, int port = 9, string? broadcast = null)
    {
        var packet = BuildMagicPacket(mac);
        using var client = new UdpClient();
        client.EnableBroadcast = true;
        var address = string.IsNullOrWhiteSpace(broadcast) ? IPAddress.Broadcast : IPAddress.Parse(broadcast);
        client.Connect(address, port);
        client.Send(packet, packet.Length);
    }

    private static byte[] BuildMagicPacket(string mac)
    {
        var macBytes = ParseMac(mac);
        var packet = new byte[102];
        for (int i = 0; i < 6; i++) packet[i] = 0xFF;
        for (int i = 6; i < 102; i += 6) Buffer.BlockCopy(macBytes, 0, packet, i, 6);
        return packet;
    }

    private static byte[] ParseMac(string mac)
    {
        var clean = new string(mac.Where(c => "0123456789ABCDEFabcdef".Contains(c)).ToArray());
        if (clean.Length != 12) throw new ArgumentException("MAC must be 12 hex digits", nameof(mac));
        var bytes = new byte[6];
        for (int i = 0; i < 6; i++)
            bytes[i] = Convert.ToByte(clean.Substring(i * 2, 2), 16);
        return bytes;
    }
}
