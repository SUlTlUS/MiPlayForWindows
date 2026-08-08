namespace DLNACast.Core.MiPlay;

/// <summary>
/// One generated AAC-LC 48 kHz stereo silent access unit, normalized to the
/// MPEG-2 ADTS header used by the rooted-phone capture. This is intended only
/// for a short bounded transport validation, not as user media.
/// </summary>
public static class MiPlayAacSilenceAccessUnit
{
    private const string Hex = "FFF94C8001BFFC211004608C1C";

    public static byte[] Create() => Convert.FromHexString(Hex);
}
