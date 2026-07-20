namespace DLNACast.Core.MiPlay;

/// <summary>
/// Offline model of com.xiaomi.continuity.ServiceName in Mi Connect Service
/// 5.1.251.10. This mirrors the APK parser/formatter shape for static
/// diagnostics only; it is not an outbound validation rule for S12 probes.
/// </summary>
public sealed record MiPlayContinuityServiceName(string? PackageName, string Name)
{
    public string ToMergedString() =>
        PackageName is null ? $":{Name}" : $"{PackageName}:{Name}";

    public static bool TryParseApkMergedString(
        string? value,
        out MiPlayContinuityServiceName? serviceName)
    {
        serviceName = null;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var parts = SplitLikeJavaStringSplitColon(value);
        if (parts.Length == 0)
        {
            return false;
        }

        if (parts.Length == 1)
        {
            if (parts[0].Contains(':', StringComparison.Ordinal))
            {
                return false;
            }

            serviceName = new MiPlayContinuityServiceName(null, parts[0]);
            return true;
        }

        if (parts[0].Contains(':', StringComparison.Ordinal) ||
            parts[1].Contains(':', StringComparison.Ordinal))
        {
            return false;
        }

        serviceName = new MiPlayContinuityServiceName(parts[0], parts[1]);
        return true;
    }

    private static string[] SplitLikeJavaStringSplitColon(string value)
    {
        var parts = value.Split(':');
        var length = parts.Length;
        while (length > 0 && parts[length - 1].Length == 0)
        {
            length--;
        }

        if (length == parts.Length)
        {
            return parts;
        }

        if (length == 0)
        {
            return [];
        }

        var trimmed = new string[length];
        Array.Copy(parts, trimmed, length);
        return trimmed;
    }
}
