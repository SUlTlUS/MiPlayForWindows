using DLNACast.Core.Models;

namespace DLNACast.Core.Dlna;

public static class ProtocolInfoMatcher
{
    public static bool SupportsMimeType(string? sinkProtocolInfo, string mimeType)
    {
        if (string.IsNullOrWhiteSpace(sinkProtocolInfo))
        {
            // Some renderers fail GetProtocolInfo even though they accept HTTP media.
            return true;
        }

        foreach (var entry in sinkProtocolInfo.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fields = entry.Split(':', 4, StringSplitOptions.TrimEntries);
            if (fields.Length < 3)
            {
                continue;
            }

            var protocolMatches = fields[0] is "*" || fields[0].Equals("http-get", StringComparison.OrdinalIgnoreCase);
            var mimeMatches = fields[2] is "*" || fields[2].Equals(mimeType, StringComparison.OrdinalIgnoreCase);
            if (protocolMatches && mimeMatches)
            {
                return true;
            }
        }

        return false;
    }

    public static IReadOnlyList<StreamProfile> SelectProfiles(string? sinkProtocolInfo, bool allowMp3Fallback)
    {
        var profiles = new List<StreamProfile>(2);
        var supportsWave = SupportsMimeType(sinkProtocolInfo, "audio/wav") ||
                           SupportsMimeType(sinkProtocolInfo, "audio/x-wav");
        var supportsMp3 = SupportsMimeType(sinkProtocolInfo, "audio/mpeg");

        if (supportsWave)
        {
            profiles.Add(StreamProfile.PcmWave);
        }

        if (allowMp3Fallback && supportsMp3)
        {
            profiles.Add(StreamProfile.Mp3Cbr320);
        }

        if (profiles.Count == 0)
        {
            throw new NotSupportedException("音箱的 UPnP protocolInfo 未声明支持 PCM/WAV 或 320 kbps MP3。");
        }

        return profiles;
    }
}
