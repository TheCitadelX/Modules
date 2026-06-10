using CitadelX.Node.Abstractions;
using Microsoft.Extensions.Logging;

namespace CitadelX.SlipstreamNodeModule;

public sealed class SlipstreamNodeModule : INodeCoreModule
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly AtomicFileWriter _fileWriter = new();

    public SlipstreamNodeModule(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public string CoreId => "Slipstream";
    public IReadOnlyList<string> Aliases => new[] { "slipstream", "slipstream-rust", "quic-dns", "dns-quic-tunnel" };

    public INodeServer Create(ServerLaunchProfile profile)
        => new SlipstreamNodeServer(profile, _fileWriter, _loggerFactory.CreateLogger<SlipstreamNodeServer>());
}
