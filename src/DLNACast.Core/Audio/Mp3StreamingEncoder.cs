using NAudio.Lame;
using NAudio.Wave;

namespace DLNACast.Core.Audio;

internal sealed class Mp3StreamingEncoder(PcmFrameBuffer frames, CancellationToken cancellationToken) : IAsyncDisposable
{
    private readonly PcmFrameBuffer _frames = frames;
    private readonly CancellationToken _cancellationToken = cancellationToken;

    public async Task EncodeToAsync(Stream output)
    {
        var waveFormat = new WaveFormat(
            PcmFrameBuffer.SampleRate,
            PcmFrameBuffer.BitsPerSample,
            PcmFrameBuffer.Channels);
        using var encoder = new LameMP3FileWriter(output, waveFormat, 320);
        while (!_cancellationToken.IsCancellationRequested)
        {
            var frame = await _frames.ReadFrameOrSilenceAsync(_cancellationToken).ConfigureAwait(false);
            encoder.Write(frame, 0, frame.Length);
            encoder.Flush();
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

}
