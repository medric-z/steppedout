using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SteppedOut;

// Just enough of the Windows Core Audio API to ask "is any program producing sound right now".
// The interfaces are declared by hand so there is nothing to trust but this file. Method order in
// each interface must match the COM vtable exactly; methods this tool never calls are kept as
// placeholders to hold their slots.
static class Wasapi
{
    const int ClsctxAll = 0x17;
    const int DeviceStateActive = 0x1;
    const int Render = 0;

    static readonly Guid AudioSessionManager2Iid = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
    static readonly Dictionary<uint, string> processNames = new();

    public readonly record struct Playing(string Process, float Peak);

    // The loudest active session above the threshold across every active output device, or null.
    public static Playing? LoudestSession(float threshold)
    {
        Playing? loudest = null;
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            Check(enumerator.EnumAudioEndpoints(Render, DeviceStateActive, out IMMDeviceCollection devices));
            try
            {
                Check(devices.GetCount(out uint count));
                for (uint i = 0; i < count; i++)
                {
                    Check(devices.Item(i, out IMMDevice device));
                    try
                    {
                        ScanDevice(device, threshold, ref loudest);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(device);
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(devices);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
        return loudest;
    }

    static void ScanDevice(IMMDevice device, float threshold, ref Playing? loudest)
    {
        Guid iid = AudioSessionManager2Iid;
        Check(device.Activate(ref iid, ClsctxAll, IntPtr.Zero, out object managerObject));
        var manager = (IAudioSessionManager2)managerObject;
        try
        {
            Check(manager.GetSessionEnumerator(out IAudioSessionEnumerator sessions));
            try
            {
                Check(sessions.GetCount(out int count));
                for (int i = 0; i < count; i++)
                {
                    Check(sessions.GetSession(i, out IAudioSessionControl session));
                    try
                    {
                        Inspect(session, threshold, ref loudest);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(session);
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(sessions);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(manager);
        }
    }

    static void Inspect(IAudioSessionControl session, float threshold, ref Playing? loudest)
    {
        Check(session.GetState(out AudioSessionState state));
        if (state != AudioSessionState.Active) return;

        var meter = (IAudioMeterInformation)session;
        Check(meter.GetPeakValue(out float peak));
        if (peak < threshold || (loudest is Playing p && peak <= p.Peak)) return;

        var control = (IAudioSessionControl2)session;
        // S_OK here means "this is the system sounds session"; a notification ding is not a video.
        if (control.IsSystemSoundsSession() == 0) return;
        Check(control.GetProcessId(out uint pid));
        loudest = new Playing(ProcessName(pid), peak);
    }

    static string ProcessName(uint pid)
    {
        if (processNames.TryGetValue(pid, out string? cached)) return cached;
        if (processNames.Count > 64) processNames.Clear();
        string name;
        try
        {
            name = Process.GetProcessById((int)pid).ProcessName;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            name = "pid " + pid;
        }
        processNames[pid] = name;
        return name;
    }

    static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);

    enum AudioSessionState
    {
        Inactive = 0,
        Active = 1,
        Expired = 2,
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumerator
    {
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice(IntPtr id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr properties);
        [PreserveSig] int GetId(out IntPtr id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionManager2
    {
        // IAudioSessionManager
        [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, uint streamFlags, out IAudioSessionControl control);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, uint streamFlags, out IntPtr volume);

        // IAudioSessionManager2
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator enumerator);
        [PreserveSig] int RegisterSessionNotification(IntPtr notification);
        [PreserveSig] int UnregisterSessionNotification(IntPtr notification);
        [PreserveSig] int RegisterDuckNotification(IntPtr sessionId, IntPtr notification);
        [PreserveSig] int UnregisterDuckNotification(IntPtr notification);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, out IAudioSessionControl session);
    }

    [ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl
    {
        [PreserveSig] int GetState(out AudioSessionState state);
        [PreserveSig] int GetDisplayName(out IntPtr name);
        [PreserveSig] int SetDisplayName(IntPtr name, IntPtr eventContext);
        [PreserveSig] int GetIconPath(out IntPtr path);
        [PreserveSig] int SetIconPath(IntPtr path, IntPtr eventContext);
        [PreserveSig] int GetGroupingParam(out Guid grouping);
        [PreserveSig] int SetGroupingParam(IntPtr grouping, IntPtr eventContext);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr notification);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr notification);
    }

    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl2
    {
        // IAudioSessionControl again: .NET COM interop does not inherit slots, so the base
        // interface's methods are repeated to keep the vtable aligned.
        [PreserveSig] int GetState(out AudioSessionState state);
        [PreserveSig] int GetDisplayName(out IntPtr name);
        [PreserveSig] int SetDisplayName(IntPtr name, IntPtr eventContext);
        [PreserveSig] int GetIconPath(out IntPtr path);
        [PreserveSig] int SetIconPath(IntPtr path, IntPtr eventContext);
        [PreserveSig] int GetGroupingParam(out Guid grouping);
        [PreserveSig] int SetGroupingParam(IntPtr grouping, IntPtr eventContext);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr notification);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr notification);

        // IAudioSessionControl2
        [PreserveSig] int GetSessionIdentifier(out IntPtr id);
        [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
        [PreserveSig] int GetProcessId(out uint processId);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
        [PreserveSig] int GetMeteringChannelCount(out uint channels);
        [PreserveSig] int GetChannelsPeakValues(uint count, IntPtr peaks);
        [PreserveSig] int QueryHardwareSupport(out uint mask);
    }
}
