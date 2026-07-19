using System.Runtime.InteropServices;

namespace DLNACast.Core.Audio;

internal static class ProcessLoopbackInterop
{
    public const uint AudioClientStreamFlagsLoopback = 0x0002_0000;
    private const ushort VariantTypeBlob = 65;
    private const string VirtualAudioDeviceProcessLoopback = "VAD\\Process_Loopback";

    public static async Task<IAudioClientNative> ActivateAsync(
        int processId,
        bool includeChildren,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<IAudioClientNative>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var initialized = false;
            try
            {
                var initializeResult = CoInitializeEx(IntPtr.Zero, 0); // COINIT_MULTITHREADED
                Marshal.ThrowExceptionForHR(initializeResult);
                initialized = initializeResult >= 0;
                var client = ActivateOnMtaThreadAsync(processId, includeChildren, cancellationToken)
                    .GetAwaiter().GetResult();
                completion.TrySetResult(client);
            }
            catch (OperationCanceledException)
            {
                completion.TrySetCanceled(cancellationToken);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
            finally
            {
                if (initialized) CoUninitialize();
            }
        })
        {
            IsBackground = true,
            Name = "DLNACast process-loopback activation"
        };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IAudioClientNative> ActivateOnMtaThreadAsync(
        int processId,
        bool includeChildren,
        CancellationToken cancellationToken)
    {
        var activation = new AudioClientActivationParameters
        {
            ActivationType = 1,
            ProcessLoopbackParameters = new ProcessLoopbackParameters
            {
                TargetProcessId = checked((uint)processId),
                ProcessLoopbackMode = includeChildren ? 0 : 1
            }
        };

        var activationPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<AudioClientActivationParameters>());
        var propVariantPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<PropVariant>());
        IActivateAudioInterfaceAsyncOperation? operation = null;
        var completion = new ActivationCompletionHandler();
        try
        {
            Marshal.StructureToPtr(activation, activationPointer, false);
            var propVariant = new PropVariant
            {
                VariantType = VariantTypeBlob,
                Blob = new Blob
                {
                    Size = Marshal.SizeOf<AudioClientActivationParameters>(),
                    Data = activationPointer
                }
            };
            Marshal.StructureToPtr(propVariant, propVariantPointer, false);

            var audioClientGuid = typeof(IAudioClientNative).GUID;
            ActivateAudioInterfaceAsync(
                VirtualAudioDeviceProcessLoopback,
                audioClientGuid,
                propVariantPointer,
                completion,
                out operation);

            var activated = await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            GC.KeepAlive(operation);
            return activated;
        }
        finally
        {
            if (operation is not null && Marshal.IsComObject(operation))
            {
                Marshal.FinalReleaseComObject(operation);
            }
            Marshal.FreeCoTaskMem(propVariantPointer);
            Marshal.FreeCoTaskMem(activationPointer);
        }
    }

    [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
    private static extern void ActivateAudioInterfaceAsync(
        [In, MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        [In, MarshalAs(UnmanagedType.LPStruct)] Guid interfaceId,
        IntPtr activationParameters,
        [In] IActivateAudioInterfaceCompletionHandler completionHandler,
        out IActivateAudioInterfaceAsyncOperation activationOperation);

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr reserved, uint coInit);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioClientActivationParameters
    {
        public int ActivationType;
        public ProcessLoopbackParameters ProcessLoopbackParameters;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessLoopbackParameters
    {
        public uint TargetProcessId;
        public int ProcessLoopbackMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Blob
    {
        public int Size;
        public IntPtr Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort VariantType;
        [FieldOffset(8)] public Blob Blob;
    }

    [ComImport]
    [Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateAudioInterfaceAsyncOperation
    {
        [PreserveSig] int GetActivateResult(out int activateResult, out IntPtr activatedInterface);
    }

    [Guid("41D949AB-9862-444A-80F6-C261334DA5EB")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [ComVisible(true)]
    private interface IActivateAudioInterfaceCompletionHandler
    {
        [PreserveSig] int ActivateCompleted(IActivateAudioInterfaceAsyncOperation activationOperation);
    }

    [ComImport]
    [Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAgileObject
    {
    }

    [ClassInterface(ClassInterfaceType.None)]
    [ComVisible(true)]
    private sealed class ActivationCompletionHandler : IActivateAudioInterfaceCompletionHandler, IAgileObject
    {
        private readonly TaskCompletionSource<IAudioClientNative> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IAudioClientNative> Task => _completion.Task;

        public int ActivateCompleted(IActivateAudioInterfaceAsyncOperation activationOperation)
        {
            try
            {
                var result = activationOperation.GetActivateResult(out var activationResult, out var interfacePointer);
                Marshal.ThrowExceptionForHR(result);
                Marshal.ThrowExceptionForHR(activationResult);
                if (interfacePointer == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Windows 没有返回进程音频接口。");
                }

                try
                {
                    var instance = (IAudioClientNative)Marshal.GetObjectForIUnknown(interfacePointer);
                    _completion.TrySetResult(instance);
                }
                finally
                {
                    Marshal.Release(interfacePointer);
                }

                return 0;
            }
            catch (Exception ex)
            {
                _completion.TrySetException(ex);
                return Marshal.GetHRForException(ex);
            }
        }
    }
}
