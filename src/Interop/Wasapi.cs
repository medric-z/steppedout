using System.Runtime.InteropServices;

namespace SteppedOut;

// Just enough of the Windows Core Audio API to ask "is any program producing sound right now" and
// to mute the speakers while you are away.
// The interfaces are declared by hand so there is nothing to trust but this file. Method order in
// each interface must match the COM vtable exactly; methods this tool never calls are kept as
// placeholders to hold their slots.
static class Wasapi
{
    const int ClsctxAll = 0x17;
    const int DeviceStateActive = 0x1;
    const int Render = 0;
    const int Multimedia = 1;
    const int NotFound = unchecked((int)0x80070490);
    const int MaxCachedNames = 64;

    static readonly Dictionary<uint, string> ProcessNames = new();

    // A program producing sound: its process, its name for the log and the Why window, and how loud.
    public readonly record struct Session(int ProcessId, string Process, float Peak);

    // Every active session above the threshold across every active output device that can be
    // heard, except the system sounds. A call app's own sound is told apart from music by its
    // process, so each session is listed, not only the loudest.
    public static IReadOnlyList<Session> Sessions(float threshold)
    {
        var found = new List<Session>();
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
                        if (!Silent(device)) ScanDevice(device, threshold, found);
                    }
                    catch (COMException)
                    {
                        // A device unplugged or switched off in the middle of the scan fails here. The
                        // other devices still count.
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
        return found;
    }

    // Mutes the default output unless it is already muted. The result unmutes that same device
    // later, even if a different one has become the default in the meantime.
    public static MutedOutput? MuteDefaultOutput()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            Check(enumerator.GetDefaultAudioEndpoint(Render, Multimedia, out IMMDevice device));
            try
            {
                Check(device.GetId(out string id));
                var volume = EndpointVolume(device);
                Check(volume.GetMute(out bool alreadyMuted));
                if (alreadyMuted)
                {
                    Marshal.ReleaseComObject(volume);
                    return null;
                }
                Check(volume.SetMute(true, IntPtr.Zero));
                return new MutedOutput(id, () =>
                {
                    try
                    {
                        Check(volume.SetMute(false, IntPtr.Zero));
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(volume);
                    }
                });
            }
            finally
            {
                Marshal.ReleaseComObject(device);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
    }

    // For a mute an earlier run left behind, or one whose connection broke while you were away:
    // unmutes the device with this id if it is still muted. False when it was not muted, or when
    // Windows no longer knows the device at all. Throws while the device is switched off or
    // unplugged, so it is kept to try again, without reaching for a device that is not there.
    public static bool UnmuteIfMuted(string deviceId)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            int found = enumerator.GetDevice(deviceId, out IMMDevice device);
            if (found == NotFound) return false;
            Check(found);
            try
            {
                Check(device.GetState(out int state));
                if (state != DeviceStateActive) throw new COMException("not connected");
                var volume = EndpointVolume(device);
                try
                {
                    Check(volume.GetMute(out bool muted));
                    if (!muted) return false;
                    Check(volume.SetMute(false, IntPtr.Zero));
                    return true;
                }
                finally
                {
                    Marshal.ReleaseComObject(volume);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(device);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
    }

    // A session's meter reads the sound before the device's volume and mute are applied, so music
    // on muted speakers would still count as playing. Muted, or turned all the way down, nothing on
    // the device can be heard. A device whose volume cannot be read still counts, so a film on it
    // still holds the lock.
    static bool Silent(IMMDevice device)
    {
        IAudioEndpointVolume volume;
        try
        {
            volume = EndpointVolume(device);
        }
        catch (COMException)
        {
            return false;
        }
        try
        {
            if (volume.GetMute(out bool muted) == 0 && muted) return true;
            return volume.GetMasterVolumeLevelScalar(out float level) == 0 && level <= 0f;
        }
        finally
        {
            Marshal.ReleaseComObject(volume);
        }
    }

    static IAudioEndpointVolume EndpointVolume(IMMDevice device)
    {
        Guid iid = typeof(IAudioEndpointVolume).GUID;
        Check(device.Activate(ref iid, ClsctxAll, IntPtr.Zero, out object volumeObject));
        return (IAudioEndpointVolume)volumeObject;
    }

    public sealed class MutedOutput
    {
        readonly Action unmute;

        internal MutedOutput(string deviceId, Action unmute)
        {
            DeviceId = deviceId;
            this.unmute = unmute;
        }

        // Windows' id for the device, which stays the same across restarts.
        public string DeviceId { get; }

        // Through the connection the mute was made on. That connection breaks if the device goes
        // away in the meantime, and stays broken even once the device is back.
        public void Unmute() => unmute();
    }

    static void ScanDevice(IMMDevice device, float threshold, List<Session> found)
    {
        Guid iid = typeof(IAudioSessionManager2).GUID;
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
                        Inspect(session, threshold, found);
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

    static void Inspect(IAudioSessionControl session, float threshold, List<Session> found)
    {
        Check(session.GetState(out AudioSessionState state));
        if (state != AudioSessionState.Active) return;

        var meter = (IAudioMeterInformation)session;
        Check(meter.GetPeakValue(out float peak));
        if (peak < threshold) return;

        var control = (IAudioSessionControl2)session;
        // S_OK here means "this is the system sounds session"; a notification ding is not a video.
        if (control.IsSystemSoundsSession() == 0) return;
        Check(control.GetProcessId(out uint pid));
        found.Add(new Session((int)pid, ProcessName(pid), peak));
    }

    // Only for naming: the name of an id that has since been reused is wrong for a moment, and
    // nothing decides by it. Matching a session to the call app reads the process afresh. Sessions
    // runs on a reading thread of its own (Bounded), one at a time for each watcher, so the cache is
    // shared under a lock.
    static string ProcessName(uint pid)
    {
        lock (ProcessNames)
        {
            if (ProcessNames.TryGetValue(pid, out string? cached)) return cached;
        }
        string name = Native.ProcessName(pid);
        lock (ProcessNames)
        {
            if (ProcessNames.Count > MaxCachedNames) ProcessNames.Clear();
            ProcessNames[pid] = name;
        }
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
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
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
        // The marshaler frees the string Windows allocates.
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
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

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, IntPtr eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, IntPtr eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, IntPtr eventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, IntPtr eventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, IntPtr eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
