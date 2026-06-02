using CitadelX.Node.Abstractions;
using Microsoft.Extensions.Logging;

namespace CitadelX.WireGuardNodeModule;

public sealed class WireGuardNodeModule : INodeCoreModule
{
    private readonly AtomicFileWriter _fileWriter;
    private readonly ILoggerFactory _loggerFactory;

    public WireGuardNodeModule(AtomicFileWriter fileWriter, ILoggerFactory loggerFactory)
    {
        _fileWriter = fileWriter;
        _loggerFactory = loggerFactory;
    }

    public string CoreId => "WireGuard";
    public IReadOnlyList<string> Aliases => new[] { "wireguard", "wg", "wg-quick" };

    public INodeServer Create(ServerLaunchProfile profile)
    {
        return new WireGuardNodeServer(profile, _fileWriter, _loggerFactory.CreateLogger<WireGuardNodeServer>());
    }
}
