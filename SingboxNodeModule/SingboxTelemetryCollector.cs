using CitadelX.Modules.Abstractions;
using CitadelX.SingboxNodeModule.V2RayApi;
using Grpc.Core;
using Grpc.Net.Client;

namespace CitadelX.SingboxNodeModule;

public sealed class SingboxTelemetryCollector
{
    private static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(3);
    private readonly object _sync = new();
    private readonly Dictionary<string, UserCounterState> _states = new(StringComparer.Ordinal);
    private string _serverId;
    private string? _listenAddress;
    private IReadOnlyList<string> _userIds = Array.Empty<string>();

    static SingboxTelemetryCollector()
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
    }

    public SingboxTelemetryCollector(string? serverId)
    {
        _serverId = serverId ?? string.Empty;
    }

    public string? ListenAddress
    {
        get
        {
            lock (_sync)
            {
                return _listenAddress;
            }
        }
    }

    public void SetServerId(string? serverId)
    {
        lock (_sync)
        {
            _serverId = serverId ?? string.Empty;
        }
    }

    public void UpdateConfiguration(SingboxV2RayApiConfiguration configuration)
    {
        lock (_sync)
        {
            _listenAddress = configuration.ListenAddress;
            _userIds = configuration.UserIds.ToArray();

            var activeUsers = new HashSet<string>(_userIds, StringComparer.Ordinal);
            foreach (var removedUser in _states.Keys.Where(userId => !activeUsers.Contains(userId)).ToArray())
            {
                _states.Remove(removedUser);
            }
        }
    }

    public IReadOnlyList<ServerUserRuntimeSnapshot> Collect(bool isRunning)
    {
        string serverId;
        string? listenAddress;
        IReadOnlyList<string> userIds;
        lock (_sync)
        {
            serverId = _serverId;
            listenAddress = _listenAddress;
            userIds = _userIds.ToArray();
        }

        if (string.IsNullOrWhiteSpace(serverId) || userIds.Count == 0)
        {
            return Array.Empty<ServerUserRuntimeSnapshot>();
        }

        var now = DateTimeOffset.UtcNow;
        if (!isRunning)
        {
            return BuildUnavailable(
                serverId,
                userIds,
                now,
                ServerUserTelemetryHealth.Offline,
                "Sing-box is stopped.");
        }

        if (string.IsNullOrWhiteSpace(listenAddress))
        {
            return BuildUnavailable(
                serverId,
                userIds,
                now,
                ServerUserTelemetryHealth.Unavailable,
                "V2Ray StatsService is not configured.");
        }

        try
        {
            var counters = QueryCounters(listenAddress);
            return BuildSnapshots(serverId, userIds, counters, now);
        }
        catch (Exception exception) when (exception is RpcException
                                          or HttpRequestException
                                          or IOException
                                          or InvalidOperationException)
        {
            return BuildUnavailable(
                serverId,
                userIds,
                now,
                ServerUserTelemetryHealth.Unavailable,
                $"V2Ray StatsService unavailable: {exception.Message}");
        }
    }

    private static IReadOnlyDictionary<string, long> QueryCounters(string listenAddress)
    {
        using var handler = new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true,
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(2)
        };
        using var channel = GrpcChannel.ForAddress($"http://{listenAddress}", new GrpcChannelOptions
        {
            HttpHandler = handler
        });
        var client = new StatsService.StatsServiceClient(channel);
        var response = client.QueryStats(
            new QueryStatsRequest(),
            deadline: DateTime.UtcNow.AddSeconds(2));

        return response.Stat
            .GroupBy(stat => stat.Name, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => Math.Max(0, group.Last().Value),
                StringComparer.Ordinal);
    }

    private IReadOnlyList<ServerUserRuntimeSnapshot> BuildSnapshots(
        string serverId,
        IReadOnlyList<string> userIds,
        IReadOnlyDictionary<string, long> counters,
        DateTimeOffset now)
    {
        var snapshots = new List<ServerUserRuntimeSnapshot>(userIds.Count);
        lock (_sync)
        {
            foreach (var userId in userIds)
            {
                var rxBytes = ReadCounter(counters, userId, "uplink");
                var txBytes = ReadCounter(counters, userId, "downlink");
                _states.TryGetValue(userId, out var previous);

                var trafficChanged = previous is null
                    ? rxBytes > 0 || txBytes > 0
                    : rxBytes > previous.RxBytes
                      || txBytes > previous.TxBytes
                      || (rxBytes < previous.RxBytes && rxBytes > 0)
                      || (txBytes < previous.TxBytes && txBytes > 0);
                var lastSeenAt = trafficChanged ? now : previous?.LastSeenAt;
                _states[userId] = new UserCounterState(rxBytes, txBytes, lastSeenAt);

                var online = lastSeenAt is not null && now - lastSeenAt.Value <= OnlineWindow;
                snapshots.Add(new ServerUserRuntimeSnapshot
                {
                    ServerId = serverId,
                    UserId = userId,
                    IsOnline = online,
                    LastSeenAt = lastSeenAt,
                    RxBytes = rxBytes,
                    TxBytes = txBytes,
                    TrafficBytes = rxBytes + txBytes,
                    Health = online
                        ? ServerUserTelemetryHealth.Online
                        : lastSeenAt is null
                            ? ServerUserTelemetryHealth.Offline
                            : ServerUserTelemetryHealth.Idle,
                    ReportedAt = now
                });
            }
        }

        return snapshots;
    }

    private static long ReadCounter(
        IReadOnlyDictionary<string, long> counters,
        string userId,
        string direction)
    {
        var name = $"user>>>{userId}>>>traffic>>>{direction}";
        return counters.TryGetValue(name, out var value) ? Math.Max(0, value) : 0;
    }

    private static IReadOnlyList<ServerUserRuntimeSnapshot> BuildUnavailable(
        string serverId,
        IReadOnlyList<string> userIds,
        DateTimeOffset now,
        ServerUserTelemetryHealth health,
        string message)
    {
        return userIds.Select(userId => new ServerUserRuntimeSnapshot
        {
            ServerId = serverId,
            UserId = userId,
            IsOnline = false,
            Health = health,
            StatusMessage = message,
            ReportedAt = now
        }).ToArray();
    }

    private sealed record UserCounterState(long RxBytes, long TxBytes, DateTimeOffset? LastSeenAt);
}
