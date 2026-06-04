using CitadelX.Node.Abstractions;
using Microsoft.Extensions.Logging;

namespace CitadelX.AmneziaWGNodeModule;

public sealed class AmneziaWGNodeModule : INodeCoreModule
{
    private readonly AtomicFileWriter _fileWriter;
    private readonly ILoggerFactory _loggerFactory;

    public AmneziaWGNodeModule(AtomicFileWriter fileWriter, ILoggerFactory loggerFactory)
    {
        _fileWriter = fileWriter;
        _loggerFactory = loggerFactory;
    }

    public string CoreId => "AmneziaWG";
    public IReadOnlyList<string> Aliases => new[] { "amneziawg", "amnezia-wg", "amnezia", "awg", "awg-quick" };

    public INodeServer Create(ServerLaunchProfile profile)
    {
        return new AmneziaWGNodeServer(profile, _fileWriter, _loggerFactory.CreateLogger<AmneziaWGNodeServer>());
    }
}
