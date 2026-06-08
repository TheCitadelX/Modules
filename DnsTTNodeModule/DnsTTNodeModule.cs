using CitadelX.Node.Abstractions;
using Microsoft.Extensions.Logging;

namespace CitadelX.DnsTTNodeModule;

public sealed class DnsTTNodeModule : INodeCoreModule
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly AtomicFileWriter _fileWriter = new();

    public DnsTTNodeModule(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public string CoreId => "DnsTT";
    public IReadOnlyList<string> Aliases => new[] { "dnstt", "dns-tunnel", "dns-over-dns" };

    public INodeServer Create(ServerLaunchProfile profile)
        => new DnsTTNodeServer(profile, _fileWriter, _loggerFactory.CreateLogger<DnsTTNodeServer>());
}
