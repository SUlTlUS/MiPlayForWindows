using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DLNACast.Core.MiPlay;

/// <summary>
/// Waits for MiPlay media deadlines with a Windows high-resolution waitable
/// timer. Unlike Task.Delay, the timer is not rounded to the ordinary system
/// timer period after a long-running app becomes idle or backgrounded.
/// </summary>
internal sealed class MiPlayMediaPacingClock : IDisposable
{
    private const uint CreateWaitableTimerHighResolution = 0x0000_0002;
    private const uint TimerModifyState = 0x0000_0002;
    private const uint Synchronize = 0x0010_0000;
    private const uint Infinite = 0xffff_ffff;
    private const uint WaitObject0 = 0x0000_0000;
    private const uint WaitFailed = 0xffff_ffff;
    private const long HundredNanosecondsPerSecond = 10_000_000;

    private readonly SafeWaitHandle timer;

    private MiPlayMediaPacingClock(SafeWaitHandle timer)
    {
        this.timer = timer;
    }

    public static MiPlayMediaPacingClock Create()
    {
        var timer = CreateWaitableTimerEx(
            IntPtr.Zero,
            null,
            CreateWaitableTimerHighResolution,
            TimerModifyState | Synchronize);
        if (!timer.IsInvalid)
        {
            return new MiPlayMediaPacingClock(timer);
        }

        timer.Dispose();
        var error = Marshal.GetLastPInvokeError();
        throw new Win32Exception(
            error,
            "Windows did not create the high-resolution MiPlay media timer.");
    }

    public ValueTask WaitUntilAsync(
        long targetTimestamp,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var remainingTicks = targetTimestamp - Stopwatch.GetTimestamp();
        if (remainingTicks <= 0)
        {
            return ValueTask.CompletedTask;
        }

        var dueTime = ConvertStopwatchTicksToRelativeDueTime(remainingTicks);
        if (!SetWaitableTimer(
                timer,
                ref dueTime,
                periodMilliseconds: 0,
                IntPtr.Zero,
                IntPtr.Zero,
                resumeSystem: false))
        {
            throw new Win32Exception(
                Marshal.GetLastPInvokeError(),
                "Windows did not arm the MiPlay media timer.");
        }

        var waitResult = WaitForSingleObject(timer, Infinite);
        if (waitResult == WaitFailed)
        {
            throw new Win32Exception(
                Marshal.GetLastPInvokeError(),
                "Windows failed while waiting for the MiPlay media timer.");
        }
        if (waitResult != WaitObject0)
        {
            throw new InvalidOperationException(
                $"The MiPlay media timer returned unexpected wait result 0x{waitResult:X8}.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    internal static long ConvertStopwatchTicksToRelativeDueTime(long remainingTicks)
    {
        if (remainingTicks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(remainingTicks));
        }

        var dueTime = checked((long)Math.Ceiling(
            remainingTicks * (double)HundredNanosecondsPerSecond /
            Stopwatch.Frequency));
        return -Math.Max(1, dueTime);
    }

    public void Dispose() => timer.Dispose();

    [DllImport("kernel32.dll", EntryPoint = "CreateWaitableTimerExW", SetLastError = true)]
    private static extern SafeWaitHandle CreateWaitableTimerEx(
        IntPtr timerAttributes,
        [MarshalAs(UnmanagedType.LPWStr)] string? timerName,
        uint flags,
        uint desiredAccess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(
        SafeWaitHandle timer,
        ref long dueTime,
        int periodMilliseconds,
        IntPtr completionRoutine,
        IntPtr completionRoutineArgument,
        [MarshalAs(UnmanagedType.Bool)] bool resumeSystem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(
        SafeWaitHandle handle,
        uint milliseconds);
}
