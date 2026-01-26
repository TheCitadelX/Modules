using CitadelX.Node.Abstractions;
using Microsoft.Extensions.Logging;

namespace CitadelX.SingboxNodeModule;

public sealed class SingboxNodeModule : INodeCoreModule
{
    private readonly AtomicFileWriter _fileWriter;
    private readonly ILoggerFactory _loggerFactory;

    public SingboxNodeModule(AtomicFileWriter fileWriter, ILoggerFactory loggerFactory)
    {
        _fileWriter = fileWriter;
        _loggerFactory = loggerFactory;
    }

    public string CoreId => "Singbox";
    public IReadOnlyList<string> Aliases => new[] { "sing-box", "singbox" };

    public INodeServer Create(ServerLaunchProfile profile)
    {
        return new SingboxNodeServer(profile, _fileWriter, _loggerFactory.CreateLogger<SingboxNodeServer>());
    }
}
