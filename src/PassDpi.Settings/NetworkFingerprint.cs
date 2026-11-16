using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace PassDpi.Settings;

/// <summary>A network identity: what we hash, plus a human label we only keep in memory.</summary>
public sealed record NetworkIdentity(string Hash, string Label);

/// <summary>
/// Identifies "which network am I on" locally: Wi-Fi SSID (if any) + default gateway IP + gateway MAC.
/// Nothing is sent anywhere; the result is a salted SHA-256 so the stored file reveals no SSIDs.
/// </summary>
public static partial class NetworkFingerprint
{
    public static NetworkIdentity? GetCurrent(byte[] salt)
    {
        var (nic, gateway) = FindPrimary();
        if (nic == null || gateway == null) return null;

        var mac = GetGatewayMac(gateway);
        string? ssid = nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? Wlan.TryGetSsid(nic.Id) : null;

        var raw = $"{nic.NetworkInterfaceType}|{ssid}|{gateway}|{mac}";
        var label = ssid ?? $"{nic.Name} ({gateway})";
        return new NetworkIdentity(Compute(salt, raw), label);
    }

    internal static string Compute(byte[] salt, string raw)
    {
        var data = new byte[salt.Length + Encoding.UTF8.GetByteCount(raw)];
        salt.CopyTo(data, 0);
        Encoding.UTF8.GetBytes(raw, 0, raw.Length, data, salt.Length);
        return Convert.ToHexString(SHA256.HashData(data));
    }

    /// <summary>Loads or creates the per-machine 32-byte salt.</summary>
    public static byte[] LoadOrCreateSalt(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var b = File.ReadAllBytes(path);
                if (b.Length == 32) return b;
            }
        }
        catch (IOException) { }
        var salt = RandomNumberGenerator.GetBytes(32);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, salt);
        return salt;
    }

    [LibraryImport("iphlpapi.dll")]
    private static partial int GetBestInterface(uint destAddr, out uint index);

    /// <summary>
    /// The interface Windows would route Internet traffic through (local routing-table lookup,
    /// no packets are sent), plus its IPv4 default gateway.
    /// </summary>
    private static (NetworkInterface?, IPAddress?) FindPrimary()
    {
        // 1.1.1.1 in network byte order, only used as a routing-table key.
        uint bestIndex = 0;
        var haveBest = GetBestInterface(0x01010101, out bestIndex) == 0;

        NetworkInterface? fallback = null; IPAddress? fallbackGw = null;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            var props = nic.GetIPProperties();
            var gw = props.GatewayAddresses
                .Select(g => g.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any));
            if (gw == null) continue;
            int idx;
            try { idx = props.GetIPv4Properties()?.Index ?? -1; } catch { idx = -1; }
            if (haveBest && idx == (int)bestIndex) return (nic, gw);
            fallback ??= nic; fallbackGw ??= gw;
        }
        return (fallback, fallbackGw);
    }

    [LibraryImport("iphlpapi.dll")]
    private static partial int SendARP(uint destIp, uint srcIp, [Out] byte[] mac, ref int len);

    private static string GetGatewayMac(IPAddress gw)
    {
        try
        {
#pragma warning disable CS0618 // Address is fine for IPv4
            var ip = (uint)gw.Address;
#pragma warning restore CS0618
            var mac = new byte[6]; var len = mac.Length;
            return SendARP(ip, 0, mac, ref len) == 0 ? Convert.ToHexString(mac, 0, len) : "";
        }
        catch { return ""; }
    }

    /// <summary>Minimal Native Wifi (wlanapi) wrapper to read the connected SSID.</summary>
    private static unsafe partial class Wlan
    {
        [LibraryImport("wlanapi.dll")] private static partial int WlanOpenHandle(uint ver, nint res, out uint neg, out nint h);
        [LibraryImport("wlanapi.dll")] private static partial int WlanCloseHandle(nint h, nint res);
        [LibraryImport("wlanapi.dll")] private static partial void WlanFreeMemory(nint p);
        [LibraryImport("wlanapi.dll")] private static partial int WlanQueryInterface(nint h, Guid* iface, int opcode, nint res, out uint size, out nint data, nint type);

        // WLAN_CONNECTION_ATTRIBUTES: isState(4) + wlanConnectionMode(4) + strProfileName(256 wchar = 512)
        // then WLAN_ASSOCIATION_ATTRIBUTES starts with DOT11_SSID { ULONG uSSIDLength; UCHAR ucSSID[32]; }
        private const int SsidOffset = 4 + 4 + 512;

        public static string? TryGetSsid(string interfaceId)
        {
            if (!Guid.TryParse(interfaceId, out var guid)) return null;
            nint h = 0;
            try
            {
                if (WlanOpenHandle(2, 0, out _, out h) != 0) return null;
                if (WlanQueryInterface(h, &guid, 7 /*wlan_intf_opcode_current_connection*/, 0, out var size, out var data, 0) != 0)
                    return null;
                try
                {
                    if (size < SsidOffset + 36) return null;
                    var len = Marshal.ReadInt32(data, SsidOffset);
                    if (len is <= 0 or > 32) return null;
                    var bytes = new byte[len];
                    Marshal.Copy(data + SsidOffset + 4, bytes, 0, len);
                    return Encoding.UTF8.GetString(bytes);
                }
                finally { WlanFreeMemory(data); }
            }
            catch { return null; }
            finally { if (h != 0) WlanCloseHandle(h, 0); }
        }
    }
}
