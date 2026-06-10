using CitadelX.Node.Abstractions;

namespace CitadelX.SingboxNodeModule;

public static class SingboxLogClassifier
{
    public static string? ResolveExitMessage(bool shouldBeRunning, int? exitCode)
    {
        if (!shouldBeRunning)
        {
            return null;
        }

        return exitCode.HasValue ? $"exited code={exitCode.Value}" : "exited";
    }

    public static bool IsBenignShutdownMessage(string? text)
    {
        var cleanText = RollingServerLog.StripTerminalFormatting(text);
        return cleanText.Contains(
                   "sing-box did not closed properly: close v2ray server:",
                   StringComparison.OrdinalIgnoreCase)
               && cleanText.Contains(
                   "use of closed network connection",
                   StringComparison.OrdinalIgnoreCase);
    }

    public static string ResolveStream(string physicalStream, string? text)
    {
        if (!string.Equals(physicalStream, "stderr", StringComparison.OrdinalIgnoreCase))
        {
            return physicalStream;
        }

        var cleanText = RollingServerLog.StripTerminalFormatting(text).TrimStart();
        return StartsWithErrorLevel(cleanText) ? "stderr" : "stdout";
    }

    private static bool StartsWithErrorLevel(string text)
        => text.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)
           || text.StartsWith("FATAL", StringComparison.OrdinalIgnoreCase)
           || text.StartsWith("PANIC", StringComparison.OrdinalIgnoreCase);
}
