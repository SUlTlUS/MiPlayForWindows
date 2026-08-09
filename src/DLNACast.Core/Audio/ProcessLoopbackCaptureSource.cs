using System.Diagnostics;
using System.Runtime.InteropServices;
using DLNACast.Core.Abstractions;
using DLNACast.Core.Models;

namespace DLNACast.Core.Audio;

public sealed class ProcessLoopbackCaptureSource(CaptureSelection.Process selection) : IAudioCaptureSource
{
    private readonly CaptureSelection.Process _selection = selection;
    private CancellationTokenSource? _lifetime;
    private Task? _captureLoop;
    private IAudioClientNative? _audioClient;
    private IAudioCaptureClientNative? _captureClient;
    private readonly CaptureHealthTracker _health = new();

    public CaptureSelection Selection => _selection;
    public bool IsRunning => _captureLoop is { IsCompleted: false };
    public CaptureHealth Health => _health.Snapshot();
    public event EventHandler<Exception>? CaptureFailed;

    public async Task StartAsync(PcmFrameBuffer destination, CancellationToken cancellationToken)
    {
        if (Environment.OSVersion.Version.Build < 20_348)
        {
            throw new PlatformNotSupportedException("单进程音频捕获需要 Windows 11。");
        }

        if (_captureLoop is not null)
        {
            throw new InvalidOperationException("捕获已经启动。");
        }

        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _health.Reset();
        _audioClient = await ProcessLoopbackInterop.ActivateAsync(
            _selection.ProcessId,
            _selection.IncludeChildren,
            _lifetime.Token).ConfigureAwait(false);

        var format = WaveFormatEx.CreateFloatStereo();
        var formatPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<WaveFormatEx>());
        try
        {
            Marshal.StructureToPtr(format, formatPointer, false);
            Marshal.ThrowExceptionForHR(_audioClient.Initialize(
                0,
                ProcessLoopbackInterop.AudioClientStreamFlagsLoopback,
                200_000,
                0,
                formatPointer,
                IntPtr.Zero));
        }
        finally
        {
            Marshal.FreeCoTaskMem(formatPointer);
        }

        var captureClientGuid = typeof(IAudioCaptureClientNative).GUID;
        Marshal.ThrowExceptionForHR(_audioClient.GetService(ref captureClientGuid, out var captureService));
        _captureClient = (IAudioCaptureClientNative)captureService;
        Marshal.ThrowExceptionForHR(_audioClient.Start());

        var assembler = new PcmFrameAssembler(destination);
        _captureLoop = Task.Run(() => CaptureLoopAsync(assembler, _lifetime.Token), _lifetime.Token);
    }

    private async Task CaptureLoopAsync(PcmFrameAssembler assembler, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                EnsureProcessStillExists();
                Marshal.ThrowExceptionForHR(_captureClient!.GetNextPacketSize(out var packetFrames));
                while (packetFrames > 0)
                {
                    Marshal.ThrowExceptionForHR(_captureClient.GetBuffer(
                        out var data,
                        out var frameCount,
                        out var flags,
                        out _,
                        out _));
                    try
                    {
                        var pcm = ConvertFloatPacketToPcm(data, frameCount, (flags & 0x2) != 0);
                        _health.Record(pcm);
                        assembler.Push(pcm);
                    }
                    finally
                    {
                        Marshal.ThrowExceptionForHR(_captureClient.ReleaseBuffer(frameCount));
                    }

                    Marshal.ThrowExceptionForHR(_captureClient.GetNextPacketSize(out packetFrames));
                }

                await Task.Delay(5, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            CaptureFailed?.Invoke(this, ex);
        }
    }

    private void EnsureProcessStillExists()
    {
        using var process = Process.GetProcessById(_selection.ProcessId);
        if (process.HasExited)
        {
            throw new InvalidOperationException($"进程 {_selection.DisplayName} 已退出。");
        }
    }

    private static byte[] ConvertFloatPacketToPcm(IntPtr data, uint frameCount, bool silent)
    {
        var sampleCount = checked((int)frameCount * PcmFrameBuffer.Channels);
        var pcm = new byte[sampleCount * sizeof(short)];
        if (silent || data == IntPtr.Zero)
        {
            return pcm;
        }

        var samples = new float[sampleCount];
        Marshal.Copy(data, samples, 0, samples.Length);
        Span<short> output = MemoryMarshal.Cast<byte, short>(pcm.AsSpan());
        for (var i = 0; i < samples.Length; i++)
        {
            var clamped = Math.Clamp(samples[i], -1f, 1f);
            output[i] = (short)Math.Round(clamped * short.MaxValue);
        }

        return pcm;
    }

    public async Task StopAsync()
    {
        var lifetime = Interlocked.Exchange(ref _lifetime, null);
        lifetime?.Cancel();
        if (_captureLoop is not null)
        {
            try { await _captureLoop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            _captureLoop = null;
        }

        _audioClient?.Stop();

        ReleaseComObject(ref _captureClient);
        ReleaseComObject(ref _audioClient);
        lifetime?.Dispose();
    }

    private static void ReleaseComObject<T>(ref T? value) where T : class
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
        value = null;
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
internal struct WaveFormatEx
{
    public ushort FormatTag;
    public ushort Channels;
    public uint SamplesPerSecond;
    public uint AverageBytesPerSecond;
    public ushort BlockAlign;
    public ushort BitsPerSample;
    public ushort ExtraSize;

    public static WaveFormatEx CreateFloatStereo() => new()
    {
        FormatTag = 3,
        Channels = PcmFrameBuffer.Channels,
        SamplesPerSecond = PcmFrameBuffer.SampleRate,
        AverageBytesPerSecond = PcmFrameBuffer.SampleRate * PcmFrameBuffer.Channels * sizeof(float),
        BlockAlign = PcmFrameBuffer.Channels * sizeof(float),
        BitsPerSample = sizeof(float) * 8,
        ExtraSize = 0
    };
}

[ComImport]
[Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClientNative
{
    [PreserveSig] int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
    [PreserveSig] int GetBufferSize(out uint bufferFrames);
    [PreserveSig] int GetStreamLatency(out long latency);
    [PreserveSig] int GetCurrentPadding(out uint paddingFrames);
    [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closestMatch);
    [PreserveSig] int GetMixFormat(out IntPtr deviceFormat);
    [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
    [PreserveSig] int Start();
    [PreserveSig] int Stop();
    [PreserveSig] int Reset();
    [PreserveSig] int SetEventHandle(IntPtr eventHandle);
    [PreserveSig] int GetService(ref Guid interfaceId, [MarshalAs(UnmanagedType.IUnknown)] out object service);
}

[ComImport]
[Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioCaptureClientNative
{
    [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
    [PreserveSig] int ReleaseBuffer(uint frames);
    [PreserveSig] int GetNextPacketSize(out uint frames);
}
