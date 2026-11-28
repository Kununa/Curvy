using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Curvy.Core.Apo;

namespace Curvy.App.Audio;

/// <summary>An active playback device. <paramref name="HasEqualizerApo"/>: the EQ applies to it.</summary>
public sealed record AudioOutput(string Id, string Name, bool HasEqualizerApo);

/// <summary>The active playback devices and which one is the Windows default.</summary>
public sealed record AudioOutputs(IReadOnlyList<AudioOutput> Devices, string? DefaultId)
{
    public static readonly AudioOutputs Empty = new([], null);

    public AudioOutput? Default => Devices.FirstOrDefault(d => d.Id == DefaultId);
}

/// <summary>
/// Windows playback devices through Core Audio (MMDevice API): list them, read and set the default,
/// and get notified when that changes. Setting the default uses <c>IPolicyConfig</c>, the
/// undocumented interface Windows' own sound settings use (as do EarTrumpet and SoundSwitch); there
/// is no public API for it.
/// </summary>
public static partial class AudioDevices
{
    const int ERender = 0;
    const int DeviceStateActive = 1;
    const int RoleConsole = 0, RoleMultimedia = 1;

    static readonly PropertyKey FriendlyName = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
    static readonly StrategyBasedComWrappers Wrappers = new();

    /// <summary>Reads the devices now; <see cref="AudioOutputs.Empty"/> if Core Audio isn't available.</summary>
    public static AudioOutputs Read()
    {
        try
        {
            var enumerator = CreateEnumerator();
            var apo = ApoDevices.Installed();
            var collection = enumerator.EnumAudioEndpoints(ERender, DeviceStateActive);
            var devices = new List<AudioOutput>();
            for (uint i = 0, n = collection.GetCount(); i < n; i++)
            {
                var device = collection.Item(i);
                var id = device.GetId();
                devices.Add(new AudioOutput(id, NameOf(device) ?? "Unknown device",
                    ApoDevices.EndpointGuid(id) is { } g && apo.Contains(g)));
            }
            devices.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            return new AudioOutputs(devices, DefaultId(enumerator));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Curvy: couldn't read audio devices: {ex.Message}");
            return AudioOutputs.Empty;
        }
    }

    /// <summary>Makes the device the default for apps and media (like "Set as default" in Sound settings).</summary>
    public static bool SetDefault(string id)
    {
        try
        {
            var policy = Create<IPolicyConfig>(PolicyConfigClsid, typeof(IPolicyConfig).GUID);
            return policy.SetDefaultEndpoint(id, RoleConsole) >= 0 & policy.SetDefaultEndpoint(id, RoleMultimedia) >= 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Curvy: couldn't set the default audio device: {ex.Message}");
            return false;
        }
    }

    /// <summary>Calls <paramref name="changed"/> (on a background thread) when devices or the default change.</summary>
    public static IDisposable Watch(Action changed)
    {
        try
        {
            var enumerator = CreateEnumerator();
            var client = new NotificationClient(changed);
            enumerator.RegisterEndpointNotificationCallback(client);
            return new Registration(enumerator, client);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Curvy: couldn't watch audio devices: {ex.Message}");
            return new Registration(null, null);
        }
    }

    static string? DefaultId(IMMDeviceEnumerator enumerator)
    {
        // Fails with E_NOTFOUND when there's no playback device at all.
        return enumerator.GetDefaultAudioEndpoint(ERender, RoleMultimedia, out var device) >= 0 ? device.GetId() : null;
    }

    static string? NameOf(IMMDevice device)
    {
        var store = device.OpenPropertyStore(0); // STGM_READ
        var key = FriendlyName;
        store.GetValue(ref key, out var value);
        try
        {
            return value.Type == 31 /* VT_LPWSTR */ ? Marshal.PtrToStringUni(value.Pointer) : null;
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    static IMMDeviceEnumerator CreateEnumerator() =>
        Create<IMMDeviceEnumerator>(EnumeratorClsid, typeof(IMMDeviceEnumerator).GUID);

    static T Create<T>(Guid clsid, Guid iid)
    {
        Marshal.ThrowExceptionForHR(CoCreateInstance(clsid, IntPtr.Zero, 1 /* CLSCTX_INPROC_SERVER */, iid, out var ptr));
        try
        {
            return (T)Wrappers.GetOrCreateObjectForComInstance(ptr, CreateObjectFlags.UniqueInstance);
        }
        finally
        {
            Marshal.Release(ptr);
        }
    }

    static readonly Guid EnumeratorClsid = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    static readonly Guid PolicyConfigClsid = new("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9");

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, IntPtr outer, uint context, in Guid iid, out IntPtr ptr);

    [LibraryImport("ole32.dll")]
    private static partial int PropVariantClear(ref PropVariant value);

    sealed class Registration(IMMDeviceEnumerator? enumerator, IMMNotificationClient? client) : IDisposable
    {
        public void Dispose()
        {
            if (enumerator is not null && client is not null)
                try { enumerator.UnregisterEndpointNotificationCallback(client); } catch (COMException) { }
        }
    }

    [GeneratedComClass]
    internal sealed partial class NotificationClient(Action changed) : IMMNotificationClient
    {
        public void OnDeviceStateChanged(string id, uint state) => changed();
        public void OnDeviceAdded(string id) => changed();
        public void OnDeviceRemoved(string id) => changed();
        public void OnDefaultDeviceChanged(int flow, int role, string? id)
        {
            if (flow == ERender) changed();
        }
        public void OnPropertyValueChanged(string id, PropertyKey key) { }
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey(Guid formatId, uint propertyId)
{
    public Guid FormatId = formatId;
    public uint PropertyId = propertyId;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropVariant
{
    public ushort Type;
    ushort _reserved1, _reserved2, _reserved3;
    public IntPtr Pointer;
    IntPtr _pointer2;
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
internal partial interface IMMDeviceEnumerator
{
    IMMDeviceCollection EnumAudioEndpoints(int dataFlow, int stateMask);
    [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    IMMDevice GetDevice(string id);
    void RegisterEndpointNotificationCallback(IMMNotificationClient client);
    void UnregisterEndpointNotificationCallback(IMMNotificationClient client);
}

[GeneratedComInterface]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
internal partial interface IMMDeviceCollection
{
    uint GetCount();
    IMMDevice Item(uint index);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
internal partial interface IMMDevice
{
    IntPtr Activate(in Guid iid, uint context, IntPtr parameters);
    IPropertyStore OpenPropertyStore(uint access);
    string GetId();
    uint GetState();
}

[GeneratedComInterface]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
internal partial interface IPropertyStore
{
    uint GetCount();
    PropertyKey GetAt(uint index);
    void GetValue(ref PropertyKey key, out PropVariant value);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
internal partial interface IMMNotificationClient
{
    void OnDeviceStateChanged(string id, uint state);
    void OnDeviceAdded(string id);
    void OnDeviceRemoved(string id);
    void OnDefaultDeviceChanged(int flow, int role, string? id);
    void OnPropertyValueChanged(string id, PropertyKey key);
}

/// <summary>Undocumented (Windows 7+). Only SetDefaultEndpoint is used; the slots before it keep the vtable order.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
internal partial interface IPolicyConfig
{
    [PreserveSig] int GetMixFormat(string id, IntPtr format);
    [PreserveSig] int GetDeviceFormat(string id, int defaultFormat, IntPtr format);
    [PreserveSig] int ResetDeviceFormat(string id);
    [PreserveSig] int SetDeviceFormat(string id, IntPtr endpointFormat, IntPtr mixFormat);
    [PreserveSig] int GetProcessingPeriod(string id, int defaultPeriod, IntPtr defaultTime, IntPtr minimumTime);
    [PreserveSig] int SetProcessingPeriod(string id, IntPtr period);
    [PreserveSig] int GetShareMode(string id, IntPtr mode);
    [PreserveSig] int SetShareMode(string id, IntPtr mode);
    [PreserveSig] int GetPropertyValue(string id, int fxStore, IntPtr key, IntPtr value);
    [PreserveSig] int SetPropertyValue(string id, int fxStore, IntPtr key, IntPtr value);
    [PreserveSig] int SetDefaultEndpoint(string id, int role);
    [PreserveSig] int SetEndpointVisibility(string id, int visible);
}
