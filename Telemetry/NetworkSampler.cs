using System.Diagnostics;
using System.Net.NetworkInformation;

namespace TaskbarMonitor.Telemetry;

public sealed record NetworkAdapterInfo(string Id, string Name, string Description)
{
    public override string ToString() => Name;
}

/// <summary>Upload/download speed from interface byte counters. Deltas are tracked per interface so
/// adapters appearing or disappearing never produce spikes or negative speeds.</summary>
internal sealed class NetworkSampler : IDisposable
{
    private static readonly string[] VirtualMarkers =
        ["hyper-v", "virtual", "vmware", "virtualbox", "vethernet", "loopback", "wfp", "filter", "qos", "npcap", "pseudo"];

    private readonly Dictionary<string, (long Sent, long Received)> _last = [];
    private NetworkInterface[] _interfaces = [];
    private long _lastTimestamp;
    private long _refreshedAt;
    private volatile bool _dirty = true;

    public NetworkSampler() => NetworkChange.NetworkAddressChanged += OnAddressChanged;

    private void OnAddressChanged(object? sender, EventArgs e) => _dirty = true;

    public (double Up, double Down) Sample(string adapterId)
    {
        long now = Stopwatch.GetTimestamp();
        if (_dirty || Stopwatch.GetElapsedTime(_refreshedAt, now) > TimeSpan.FromSeconds(60))
        {
            _dirty = false;
            _refreshedAt = now;
            try
            {
                _interfaces = NetworkInterface.GetAllNetworkInterfaces();
            }
            catch (NetworkInformationException)
            {
                _interfaces = [];
            }
        }

        double seconds = _lastTimestamp == 0 ? 0 : Stopwatch.GetElapsedTime(_lastTimestamp, now).TotalSeconds;
        _lastTimestamp = now;

        long up = 0, down = 0;
        var seen = new HashSet<string>();
        foreach (var ni in _interfaces)
        {
            if (!Matches(ni, adapterId))
                continue;
            IPInterfaceStatistics stats;
            try
            {
                stats = ni.GetIPStatistics();
            }
            catch (NetworkInformationException)
            {
                continue;
            }
            seen.Add(ni.Id);
            if (_last.TryGetValue(ni.Id, out var previous))
            {
                up += Math.Max(0, stats.BytesSent - previous.Sent);
                down += Math.Max(0, stats.BytesReceived - previous.Received);
            }
            _last[ni.Id] = (stats.BytesSent, stats.BytesReceived);
        }
        foreach (var gone in _last.Keys.Where(k => !seen.Contains(k)).ToList())
            _last.Remove(gone);

        return seconds <= 0 ? (0, 0) : (up / seconds, down / seconds);
    }

    private static bool Matches(NetworkInterface ni, string adapterId)
    {
        if (!string.IsNullOrEmpty(adapterId))
            return string.Equals(ni.Id, adapterId, StringComparison.OrdinalIgnoreCase);
        return IsPhysical(ni);
    }

    // "All adapters" counts physical ones only: virtual switches mirror the same traffic.
    private static bool IsPhysical(NetworkInterface ni)
    {
        if (ni.OperationalStatus != OperationalStatus.Up)
            return false;
        if (ni.NetworkInterfaceType is not (NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.GigabitEthernet))
            return false;
        var text = (ni.Name + " " + ni.Description).ToLowerInvariant();
        return !text.Contains('*') && !VirtualMarkers.Any(text.Contains);
    }

    public static IReadOnlyList<NetworkAdapterInfo> ListAdapters()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up
                             && ni.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                             && !ni.Name.Contains('*'))
                .OrderByDescending(IsPhysical)
                .ThenBy(ni => ni.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(ni => new NetworkAdapterInfo(ni.Id, ni.Name, ni.Description))
                .ToList();
        }
        catch (NetworkInformationException)
        {
            return [];
        }
    }

    public void Dispose() => NetworkChange.NetworkAddressChanged -= OnAddressChanged;
}
