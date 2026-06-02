using System.Text;

namespace CitadelX.WireGuardNodeModule;

internal sealed class WireGuardConfigDocument
{
    private readonly List<string> _lines;

    private WireGuardConfigDocument(List<string> lines)
    {
        _lines = lines;
    }

    public static WireGuardConfigDocument Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("WireGuard config file does not exist.", path);
        }

        return new WireGuardConfigDocument(File.ReadAllLines(path).ToList());
    }

    public void UpsertPeer(WireGuardPeer peer)
    {
        RemovePeer(peer.UserId);
        if (_lines.Count > 0 && !string.IsNullOrWhiteSpace(_lines[^1]))
        {
            _lines.Add(string.Empty);
        }

        _lines.Add($"# CitadelX-UserId = {peer.UserId}");
        _lines.Add("[Peer]");
        _lines.Add($"PublicKey = {peer.PublicKey}");
        if (!string.IsNullOrWhiteSpace(peer.PresharedKey))
        {
            _lines.Add($"PresharedKey = {peer.PresharedKey}");
        }

        _lines.Add($"AllowedIPs = {peer.AllowedIps}");
        if (!string.IsNullOrWhiteSpace(peer.Endpoint))
        {
            _lines.Add($"Endpoint = {peer.Endpoint}");
        }

        if (!string.IsNullOrWhiteSpace(peer.PersistentKeepalive))
        {
            _lines.Add($"PersistentKeepalive = {peer.PersistentKeepalive}");
        }
    }

    public void RemovePeer(string userId)
    {
        var marker = $"# CitadelX-UserId = {userId}";
        for (var i = 0; i < _lines.Count; i++)
        {
            if (!string.Equals(_lines[i].Trim(), marker, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var end = i + 1;
            while (end < _lines.Count)
            {
                var trimmed = _lines[end].Trim();
                if (trimmed.StartsWith("# CitadelX-UserId =", StringComparison.OrdinalIgnoreCase)
                    || (trimmed.StartsWith("[", StringComparison.Ordinal) && !string.Equals(trimmed, "[Peer]", StringComparison.OrdinalIgnoreCase)))
                {
                    break;
                }

                end++;
            }

            _lines.RemoveRange(i, end - i);
            while (i < _lines.Count && string.IsNullOrWhiteSpace(_lines[i]))
            {
                _lines.RemoveAt(i);
            }

            return;
        }
    }

    public void RemovePeersNotIn(IReadOnlySet<string> allowedUserIds)
    {
        var ids = _lines
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("# CitadelX-UserId =", StringComparison.OrdinalIgnoreCase))
            .Select(line => line["# CitadelX-UserId =".Length..].Trim())
            .Where(id => !allowedUserIds.Contains(id))
            .ToArray();

        foreach (var id in ids)
        {
            RemovePeer(id);
        }
    }

    public IReadOnlyDictionary<string, string> GetUserIdsByPublicKey()
    {
        var peers = new Dictionary<string, string>(StringComparer.Ordinal);
        string? currentUserId = null;
        var inPeer = false;

        foreach (var rawLine in _lines)
        {
            var line = rawLine.Trim();
            if (line.StartsWith("# CitadelX-UserId =", StringComparison.OrdinalIgnoreCase))
            {
                currentUserId = line["# CitadelX-UserId =".Length..].Trim();
                inPeer = false;
                continue;
            }

            if (line.StartsWith("[", StringComparison.Ordinal))
            {
                inPeer = string.Equals(line, "[Peer]", StringComparison.OrdinalIgnoreCase);
                if (!inPeer)
                {
                    currentUserId = null;
                }

                continue;
            }

            if (!inPeer
                || string.IsNullOrWhiteSpace(currentUserId)
                || !line.StartsWith("PublicKey", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator < 0)
            {
                continue;
            }

            var publicKey = line[(separator + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(publicKey))
            {
                peers[publicKey] = currentUserId;
            }
        }

        return peers;
    }

    public string Serialize()
    {
        var sb = new StringBuilder();
        foreach (var line in _lines)
        {
            sb.AppendLine(line);
        }

        return sb.ToString();
    }
}
