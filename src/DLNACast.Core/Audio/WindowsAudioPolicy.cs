using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace DLNACast.Core.Audio;

internal static class WindowsAudioPolicy
{
    private const string AudioPolicyRuntimeClass = "Windows.Media.Internal.AudioPolicyConfig";
    private const string MmDevicePrefix = @"\\?\SWD#MMDEVAPI#";
    private const string RenderInterfaceSuffix = "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";

    public static void SetDefaultEndpoint(string endpointId, Role role)
    {
        var client = (IPolicyConfig)(object)new PolicyConfigClient();
        try
        {
            Marshal.ThrowExceptionForHR(client.SetDefaultEndpoint(endpointId, ToNativeRole(role)));
        }
        finally
        {
            Marshal.FinalReleaseComObject(client);
        }
    }

    public static string? GetPersistedDefaultEndpoint(int processId, Role role)
    {
        var factory = CreateAudioPolicyFactory();
        var hstring = IntPtr.Zero;
        try
        {
            var getEndpoint = GetVtableDelegate<GetPersistedDefaultAudioEndpointDelegate>(factory, 26);
            Marshal.ThrowExceptionForHR(getEndpoint(
                factory,
                checked((uint)processId),
                NativeDataFlow.Render,
                ToNativeRole(role),
                out hstring));
            var deviceId = HStringToString(hstring);
            return string.IsNullOrWhiteSpace(deviceId) ? null : deviceId;
        }
        finally
        {
            if (hstring != IntPtr.Zero) WindowsDeleteString(hstring);
            Marshal.Release(factory);
        }
    }

    public static void SetPersistedDefaultEndpoint(int processId, Role role, string? endpointId)
    {
        var factory = CreateAudioPolicyFactory();
        var hstring = IntPtr.Zero;
        try
        {
            if (!string.IsNullOrWhiteSpace(endpointId))
            {
                var deviceInterfaceId = endpointId.StartsWith(MmDevicePrefix, StringComparison.OrdinalIgnoreCase)
                    ? endpointId
                    : $"{MmDevicePrefix}{endpointId}{RenderInterfaceSuffix}";
                WindowsCreateString(deviceInterfaceId, checked((uint)deviceInterfaceId.Length), out hstring);
            }

            var setEndpoint = GetVtableDelegate<SetPersistedDefaultAudioEndpointDelegate>(factory, 25);
            Marshal.ThrowExceptionForHR(setEndpoint(
                factory,
                checked((uint)processId),
                NativeDataFlow.Render,
                ToNativeRole(role),
                hstring));
        }
        finally
        {
            if (hstring != IntPtr.Zero) WindowsDeleteString(hstring);
            Marshal.Release(factory);
        }
    }

    private static IntPtr CreateAudioPolicyFactory()
    {
        var iid = new Guid("AB3D4648-E242-459F-B02F-541C70306324");
        var classId = IntPtr.Zero;
        var factoryPointer = IntPtr.Zero;
        try
        {
            WindowsCreateString(
                AudioPolicyRuntimeClass,
                checked((uint)AudioPolicyRuntimeClass.Length),
                out classId);
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(classId, ref iid, out factoryPointer));
            var result = factoryPointer;
            factoryPointer = IntPtr.Zero;
            return result;
        }
        finally
        {
            if (factoryPointer != IntPtr.Zero) Marshal.Release(factoryPointer);
            if (classId != IntPtr.Zero) WindowsDeleteString(classId);
        }
    }

    private static TDelegate GetVtableDelegate<TDelegate>(IntPtr instance, int slot)
        where TDelegate : Delegate
    {
        var vtable = Marshal.ReadIntPtr(instance);
        var function = Marshal.ReadIntPtr(vtable, checked(slot * IntPtr.Size));
        return Marshal.GetDelegateForFunctionPointer<TDelegate>(function);
    }

    private static string? HStringToString(IntPtr hstring)
    {
        if (hstring == IntPtr.Zero) return null;
        var buffer = WindowsGetStringRawBuffer(hstring, out var length);
        return buffer == IntPtr.Zero ? null : Marshal.PtrToStringUni(buffer, checked((int)length));
    }

    private static NativeRole ToNativeRole(Role role) => role switch
    {
        Role.Console => NativeRole.Console,
        Role.Multimedia => NativeRole.Multimedia,
        Role.Communications => NativeRole.Communications,
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(
        IntPtr activatableClassId,
        ref Guid iid,
        out IntPtr factory);

    [DllImport("combase.dll", PreserveSig = false)]
    private static extern void WindowsCreateString(
        [MarshalAs(UnmanagedType.LPWStr)] string source,
        uint length,
        out IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern IntPtr WindowsGetStringRawBuffer(IntPtr hstring, out uint length);

    private enum NativeDataFlow
    {
        Render = 0,
        Capture = 1,
        All = 2
    }

    private enum NativeRole
    {
        Console = 0,
        Multimedia = 1,
        Communications = 2
    }

    [ComImport]
    [Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    private sealed class PolicyConfigClient { }

    [ComImport]
    [Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        int Unused1();
        int Unused2();
        int Unused3();
        int Unused4();
        int Unused5();
        int Unused6();
        int Unused7();
        int Unused8();
        int Unused9();
        int Unused10();
        [PreserveSig]
        int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string endpointId, NativeRole role);
        int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string endpointId, short visible);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetPersistedDefaultAudioEndpointDelegate(
        IntPtr instance,
        uint processId,
        NativeDataFlow flow,
        NativeRole role,
        IntPtr deviceId);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetPersistedDefaultAudioEndpointDelegate(
        IntPtr instance,
        uint processId,
        NativeDataFlow flow,
        NativeRole role,
        out IntPtr deviceId);
}
