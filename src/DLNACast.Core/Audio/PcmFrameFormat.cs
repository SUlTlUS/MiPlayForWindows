namespace DLNACast.Core.Audio;

public sealed record PcmFrameFormat
{
    public static PcmFrameFormat Default { get; } = new(
        sampleRate: PcmFrameBuffer.SampleRate,
        samplesPerFrame: PcmFrameBuffer.SampleRate * PcmFrameBuffer.FrameMilliseconds / 1000);

    public static PcmFrameFormat MiPlayAac { get; } = new(
        sampleRate: 48_000,
        samplesPerFrame: 1_024);

    public PcmFrameFormat(int sampleRate, int samplesPerFrame)
    {
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (samplesPerFrame <= 0) throw new ArgumentOutOfRangeException(nameof(samplesPerFrame));

        SampleRate = sampleRate;
        SamplesPerFrame = samplesPerFrame;
    }

    public int SampleRate { get; }
    public int SamplesPerFrame { get; }
    public int Channels => PcmFrameBuffer.Channels;
    public int BitsPerSample => PcmFrameBuffer.BitsPerSample;
    public int BytesPerSample => BitsPerSample / 8;
    public int BytesPerFrame => checked(SamplesPerFrame * Channels * BytesPerSample);
    public double FrameDurationMilliseconds => SamplesPerFrame * 1_000d / SampleRate;
}
