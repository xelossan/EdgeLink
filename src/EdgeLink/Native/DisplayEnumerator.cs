using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using EdgeLink.Core;
using Microsoft.Win32;

namespace EdgeLink.Native;

/// <summary>
/// Lists the active monitors through QueryDisplayConfig only. Position, size and identity all come
/// from the same display path, so they can never be attributed to the wrong monitor, and all
/// coordinates are physical pixels regardless of scaling.
/// </summary>
internal static class DisplayEnumerator {
    private const uint QDC_ONLY_ACTIVE_PATHS = 0x2;
    private const uint MODE_INFO_TYPE_SOURCE = 1;
    private const uint GET_TARGET_NAME = 2;
    private const uint GET_ADAPTER_NAME = 4;
    private const int ERROR_INSUFFICIENT_BUFFER = 122;
    private const uint EdidIdsValid = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PATH_SOURCE_INFO {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PATH_TARGET_INFO {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public uint refreshNumerator;
        public uint refreshDenominator;
        public uint scanLineOrdering;
        public int targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PATH_INFO {
        public PATH_SOURCE_INFO sourceInfo;
        public PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    // the mode union is 48 bytes; only the source-mode member is read here
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct MODE_INFO {
        [FieldOffset(0)] public uint infoType;
        [FieldOffset(4)] public uint id;
        [FieldOffset(8)] public LUID adapterId;
        [FieldOffset(16)] public uint sourceWidth;
        [FieldOffset(20)] public uint sourceHeight;
        [FieldOffset(24)] public uint sourcePixelFormat;
        [FieldOffset(28)] public int sourceX;
        [FieldOffset(32)] public int sourceY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DEVICE_INFO_HEADER {
        public uint type;
        public uint size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TARGET_DEVICE_NAME {
        public DEVICE_INFO_HEADER header;
        public uint flags;
        public uint outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string monitorFriendlyDeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string monitorDevicePath;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ADAPTER_NAME {
        public DEVICE_INFO_HEADER header;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string adapterDevicePath;
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] PATH_INFO[] paths, ref uint modeCount, [Out] MODE_INFO[] modes, IntPtr topologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref TARGET_DEVICE_NAME request);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref ADAPTER_NAME request);

    public static List<DetectedMonitor> Enumerate()
    {
        PATH_INFO[] paths;
        MODE_INFO[] modes;
        uint pathCount, modeCount;

        // the display set can change between the two calls; retry while the buffer is too small
        for (var attempt = 0;; attempt++) {
            var error = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out pathCount, out modeCount);
            if (error != 0) throw new Win32Exception(error);

            paths = new PATH_INFO[pathCount];
            modes = new MODE_INFO[modeCount];
            error = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            if (error == 0) break;
            if (error != ERROR_INSUFFICIENT_BUFFER || attempt >= 4) throw new Win32Exception(error);
        }

        var result = new List<DetectedMonitor>();
        var seenSources = new HashSet<(uint, int, uint)>();

        for (var i = 0; i < pathCount; i++) {
            var path = paths[i];
            if (path.sourceInfo.modeInfoIdx >= modeCount) continue;

            var mode = modes[path.sourceInfo.modeInfoIdx];
            if (mode.infoType != MODE_INFO_TYPE_SOURCE || mode.sourceWidth == 0 || mode.sourceHeight == 0) continue;

            // duplicated ("cloned") displays share one desktop area; one entry is enough for it
            if (!seenSources.Add((path.sourceInfo.adapterId.LowPart, path.sourceInfo.adapterId.HighPart, path.sourceInfo.id))) continue;

            var target = new TARGET_DEVICE_NAME {
                header = {
                    type = GET_TARGET_NAME,
                    size = (uint) Marshal.SizeOf<TARGET_DEVICE_NAME>(),
                    adapterId = path.targetInfo.adapterId,
                    id = path.targetInfo.id
                }
            };
            var hasTarget = DisplayConfigGetDeviceInfo(ref target) == 0;

            var adapter = new ADAPTER_NAME {
                header = {
                    type = GET_ADAPTER_NAME,
                    size = (uint) Marshal.SizeOf<ADAPTER_NAME>(),
                    adapterId = path.targetInfo.adapterId,
                    id = path.targetInfo.id
                }
            };
            var hasAdapter = DisplayConfigGetDeviceInfo(ref adapter) == 0;

            var devicePath = hasTarget ? target.monitorDevicePath ?? "" : "";
            var edidValid = hasTarget && (target.flags & EdidIdsValid) != 0;

            result.Add(new DetectedMonitor(new MonitorFingerprint {
                DevicePath = devicePath,
                FriendlyName = hasTarget ? target.monitorFriendlyDeviceName ?? "" : "",
                AdapterPath = hasAdapter ? adapter.adapterDevicePath ?? "" : "",
                EdidManufacturer = edidValid ? target.edidManufactureId : 0,
                EdidProduct = edidValid ? target.edidProductCodeId : 0,
                Serial = edidValid ? ReadEdidSerial(devicePath) : "",
                X = mode.sourceX,
                Y = mode.sourceY,
                Width = (int) mode.sourceWidth,
                Height = (int) mode.sourceHeight
            }));
        }

        return result;
    }

    /// <summary>
    /// Reads the serial number from the EDID block Windows caches in the registry for the monitor.
    /// A device path looks like \\?\DISPLAY#PHLC247#5&amp;6abf803&amp;1&amp;UID264#{guid}.
    /// </summary>
    private static string ReadEdidSerial(string devicePath)
    {
        try {
            var parts = devicePath.Split('#');
            if (parts.Length < 3) return "";

            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\DISPLAY\{parts[1]}\{parts[2]}\Device Parameters");
            if (key?.GetValue("EDID") is not byte[] edid || edid.Length < 128) return "";

            // a display descriptor tagged 0xFF carries the serial as text
            for (var offset = 54; offset <= 108; offset += 18) {
                if (edid[offset] != 0 || edid[offset + 1] != 0 || edid[offset + 2] != 0 || edid[offset + 3] != 0xFF) continue;

                var text = Encoding.ASCII.GetString(edid, offset + 5, 13);
                var end = text.IndexOf('\n');
                text = (end >= 0 ? text[..end] : text).Trim(' ', '\0');
                if (text.Length > 0) return text;
            }

            // 0 and 0x01010101 are what manufacturers write when they do not set a serial
            var number = BitConverter.ToUInt32(edid, 12);
            return number is 0 or 0x01010101 ? "" : number.ToString();
        }
        catch (Exception) {
            return "";
        }
    }
}
