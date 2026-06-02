using CitadelX.Node.Abstractions;
using Microsoft.Extensions.Logging;

namespace CitadelX.SingboxExtendedNodeModule;

public sealed class SingboxExtendedNodeModule : INodeCoreModule
{
    private readonly AtomicFileWriter _fileWriter;
    private readonly ILoggerFactory _loggerFactory;

    public SingboxExtendedNodeModule(AtomicFileWriter fileWriter, ILoggerFactory loggerFactory)
    {
        _fileWriter = fileWriter;
        _loggerFactory = loggerFactory;
    }

    public string CoreId => "SingboxExtended";
    public IReadOnlyList<string> Aliases => new[] { "singbox-extended", "sing-box-extended" };

    public INodeServer Create(ServerLaunchProfile profile)
    {
        return new SingboxNodeServer(profile, _fileWriter, _loggerFactory.CreateLogger<SingboxNodeServer>());
    }
}
