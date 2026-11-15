using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DropWheel.Conversions;
using DropWheel.Engines;

namespace DropWheel.Capture;

/// <summary>
/// Микрофон для записи экрана (D-RECORD): список устройств — у самого ffmpeg (DirectShow, те же имена, что он примет),
/// микрофон Windows по умолчанию — через Core Audio (имя из «Параметры → Звук → Ввод»). Выбор — <see cref="ScreenRecord.PickMicrophone"/>.
/// Всё здесь медленное (запуск ffmpeg) — вызывать не из потока интерфейса. Ошибки не бросает: не вышло — null.
/// </summary>
internal static class Microphones
{
    private static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Имя микрофона для ffmpeg или null (микрофонов нет, ffmpeg не запустился).</summary>
    public static string? Find(ToolLocator tools, string? preferred, Action<string>? log)
    {
        var devices = List(tools, log);
        var windowsDefault = WindowsDefault(log);
        var picked = ScreenRecord.PickMicrophone(devices, preferred, windowsDefault);
        log?.Invoke($"Микрофоны: {(devices.Count == 0 ? "нет" : string.Join(" | ", devices))}; " +
                    $"в Windows по умолчанию: {windowsDefault ?? "?"}; пишем: {picked ?? "без звука"}");
        return picked;
    }

    private static IReadOnlyList<string> List(ToolLocator tools, Action<string>? log)
    {
        try
        {
            var psi = new ProcessStartInfo(tools.Resolve(EngineKind.FFmpeg))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var a in ScreenRecord.ListDevicesArguments) psi.ArgumentList.Add(a);
            using var process = Process.Start(psi)!;
            var stderr = process.StandardError.ReadToEndAsync();
            _ = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(ListTimeout))
            {
                try { process.Kill(); } catch { /* уже вышел */ }
                log?.Invoke("Микрофоны: ffmpeg не ответил");
                return [];
            }
            return ScreenRecord.ParseAudioDevices(stderr.Result);
        }
        catch (Exception ex)
        {
            log?.Invoke("Микрофоны: не удалось получить список — " + ex.Message);
            return [];
        }
    }

    /// <summary>Имя микрофона Windows по умолчанию («Микрофон (Realtek(R) Audio)») или null.</summary>
    private static string? WindowsDefault(Action<string>? log)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IPropertyStore? store = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            if (enumerator.GetDefaultAudioEndpoint(DataFlowCapture, RoleConsole, out device) != 0 || device is null) return null;
            if (device.OpenPropertyStore(StgmRead, out store) != 0 || store is null) return null;
            var key = FriendlyNameKey;
            if (store.GetValue(ref key, out var value) != 0) return null;
            try
            {
                return value.Type == VtLpwstr && value.Pointer != IntPtr.Zero ? Marshal.PtrToStringUni(value.Pointer) : null;
            }
            finally { PropVariantClear(ref value); }
        }
        catch (Exception ex)
        {
            log?.Invoke("Микрофоны: не удалось узнать микрофон Windows — " + ex.Message);
            return null;
        }
        finally
        {
            if (store is not null) Marshal.ReleaseComObject(store);
            if (device is not null) Marshal.ReleaseComObject(device);
            if (enumerator is not null) Marshal.ReleaseComObject(enumerator);
        }
    }

    // ---------- Core Audio (mmdeviceapi.h) ----------

    private const int DataFlowCapture = 1; // eCapture
    private const int RoleConsole = 0;     // eConsole — «устройство по умолчанию» в параметрах звука
    private const int StgmRead = 0;
    private const ushort VtLpwstr = 31;

    /// <summary>PKEY_Device_FriendlyName.</summary>
    private static readonly PropertyKey FriendlyNameKey = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid fmtid, int pid)
    {
        public Guid FormatId = fmtid;
        public int PropertyId = pid;
    }

    /// <summary>PROPVARIANT: тип, три зарезервированных поля, затем значение (для строки — указатель).</summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject;

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, out IntPtr instance);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore properties);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetAt(int index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }
}
