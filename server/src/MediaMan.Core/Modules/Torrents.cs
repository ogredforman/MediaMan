using System.Diagnostics.CodeAnalysis;
using System.Net;
using MediaMan.Core.Domain;

namespace MediaMan.Core.Modules;

/// <summary>The BitTorrent engine. It only ever binds to the configured VPN interface or SOCKS5 proxy (SPEC §5, §7.8).</summary>
public interface ITorrentEngine
{
    /// <summary>Starts the engine bound to the given network path.</summary>
    Task StartAsync(TorrentNetworkConfig net, CancellationToken ct);

    Task<TorrentHandle> AddAsync(TorrentSource src, AddOptions opts, CancellationToken ct);

    Task PauseAsync(InfoHash h, CancellationToken ct);

    Task ResumeAsync(InfoHash h, CancellationToken ct);

    Task PauseAllAsync(CancellationToken ct);

    Task ResumeAllAsync(CancellationToken ct);

    Task RemoveAsync(InfoHash h, bool deleteData, CancellationToken ct);

    Task SetFilePriorityAsync(InfoHash h, int fileIndex, FilePriority p, CancellationToken ct);

    IReadOnlyList<TorrentStatus> GetAll();

    /// <summary>Progress, completion and errors.</summary>
    event EventHandler<TorrentEvent> Changed;
}

/// <summary>OS network adapters.</summary>
public interface INetworkInterfaces
{
    IReadOnlyList<NetInterface> List();

    /// <summary>Returns null if the adapter is missing or down.</summary>
    [SuppressMessage("Naming", "CA1716", Justification = "Name fixed by SPEC §10.3; C# is the only consumer.")]
    NetInterface? Get(string id);
}

/// <summary>The leak-check service.</summary>
public interface IPublicIpProbe
{
    Task<IPAddress> GetViaDefaultRouteAsync(CancellationToken ct);

    Task<IPAddress> GetViaTorrentPathAsync(TorrentNetworkConfig net, CancellationToken ct);
}

/// <summary>
/// The network path handed to the engine. <see cref="BindAddress"/> is the VPN interface address in
/// <see cref="VpnMode.Interface"/> mode; <see cref="Socks"/> is set in <see cref="VpnMode.Socks5"/> mode.
/// </summary>
public sealed record TorrentNetworkConfig(VpnMode Mode, IPAddress? BindAddress, Socks5Proxy? Socks);

/// <summary>SOCKS5 proxy settings. <see cref="ToString"/> redacts the password so it can never reach a log (SPEC §9).</summary>
public sealed record Socks5Proxy(string Host, int Port, string? Username, string? Password)
{
    public override string ToString() =>
        $"Socks5Proxy {{ Host = {Host}, Port = {Port}, Username = {Username}, Password = {(Password is null ? "<none>" : "***")} }}";
}

/// <summary>Where a torrent comes from: a magnet link or the bytes of a .torrent file.</summary>
public abstract record TorrentSource;

public sealed record MagnetLink(string Uri) : TorrentSource;

public sealed record TorrentFile(ReadOnlyMemory<byte> Content) : TorrentSource;

public sealed record AddOptions
{
    public required string SavePath { get; init; }
    public bool StartPaused { get; init; }
    public double? SeedRatioLimit { get; init; }
    public TimeSpan? SeedTimeLimit { get; init; }
}

public sealed record TorrentHandle(InfoHash InfoHash, string Name);

public sealed record TorrentStatus
{
    public required InfoHash InfoHash { get; init; }
    public required string Name { get; init; }
    public DownloadState State { get; init; }
    public double Progress { get; init; }
    public long TotalBytes { get; init; }
    public long DownloadedBytes { get; init; }
    public long DownloadRateBytesPerSecond { get; init; }
    public long UploadRateBytesPerSecond { get; init; }
    public int Peers { get; init; }
    public int Seeds { get; init; }
    public double Ratio { get; init; }
    public TimeSpan? Eta { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<TorrentFileStatus> Files { get; init; } = [];
}

public sealed record TorrentFileStatus(int Index, string Path, long SizeBytes, double Progress, FilePriority Priority);

public enum TorrentEventKind
{
    Progress,
    Completed,
    Error,
}

public sealed record TorrentEvent(TorrentEventKind Kind, TorrentStatus Status);

public sealed record NetInterface(string Id, string Name, bool IsUp, IReadOnlyList<IPAddress> Addresses);
