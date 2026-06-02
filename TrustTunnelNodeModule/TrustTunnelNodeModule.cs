using CitadelX.Node.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;

namespace CitadelX.TrustTunnelNodeModule;

public sealed class TrustTunnelNodeModule : INodeCoreModule
{
    public string CoreId => "TrustTunnel";
    public IReadOnlyList<string> Aliases => new[] { "trusttunnel", "tt", "adguard-vpn" };

    public INodeServer Create(ServerLaunchProfile profile)
        => new TrustTunnelNodeServer(profile, new AtomicFileWriter(), NullLogger<TrustTunnelNodeServer>.Instance);
}
