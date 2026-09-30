using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

ApplicationConfiguration.Initialize();
Application.Run(new MainForm());

class MainForm : Form
{
    const int WM_DEVICECHANGE = 0x0219;
    readonly ListView list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
    readonly System.Windows.Forms.Timer debounce = new() { Interval = 600 };

    public MainForm()
    {
        Width = 820; Height = 380;
        foreach (var (name, w) in new[] { ("Device", 280), ("VID:PID", 90), ("Speed", 170), ("Note", 260) })
            list.Columns.Add(name, w);
        Controls.Add(list);
        debounce.Tick += (_, _) => { debounce.Stop(); Reload(); };
        Shown += (_, _) => Reload();
    }

    protected override void WndProc(ref Message m)
    {
        // Windows broadcasts this whenever a device is plugged in or removed
        if (m.Msg == WM_DEVICECHANGE) { debounce.Stop(); debounce.Start(); }
        base.WndProc(ref m);
    }

    void Reload()
    {
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var d in Usb.Enumerate())
        {
            var it = list.Items.Add(d.Name);
            it.SubItems.Add(d.Id);
            it.SubItems.Add(d.Speed);
            it.SubItems.Add(d.Note);
            if (d.Note.StartsWith("⚠")) it.ForeColor = Color.DarkOrange;
        }
        list.EndUpdate();
        Text = $"USB Speed - {list.Items.Count} device(s) - updated {DateTime.Now:T}";
    }
}

record UsbDev(string Name, string Id, string Speed, string Note);

static class Usb
{
    static readonly Guid HubGuid = new("f18a0e88-c30c-11d0-8815-00a0c906bed8"); // GUID_DEVINTERFACE_USB_HUB
    const uint IOCTL_NODE_INFO = 0x220408;  // IOCTL_USB_GET_NODE_INFORMATION
    const uint IOCTL_CONN_EX   = 0x220448;  // IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX
    const uint IOCTL_CONN_V2   = 0x22045C;  // IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX_V2
    const uint IOCTL_DESC      = 0x220410;  // IOCTL_USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION

    public static List<UsbDev> Enumerate()
    {
        var result = new List<UsbDev>();
        var guid = HubGuid;
        IntPtr set = SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, 0x12); // PRESENT | DEVICEINTERFACE
        if (set == (IntPtr)(-1)) return result;
        try
        {
            for (uint i = 0; ; i++)
            {
                var ifd = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref ifd)) break;

                SetupDiGetDeviceInterfaceDetail(set, ref ifd, IntPtr.Zero, 0, out int need, IntPtr.Zero);
                IntPtr buf = Marshal.AllocHGlobal(need);
                try
                {
                    Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6); // cbSize quirk of this struct
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref ifd, buf, need, out _, IntPtr.Zero)) continue;
                    ScanHub(Marshal.PtrToStringUni(buf + 4)!, result);
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return result;
    }

    static void ScanHub(string path, List<UsbDev> result)
    {
        using var h = CreateFile(path, 0x40000000, 2, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (h.IsInvalid) return;

        var node = new byte[128];
        if (!Ioctl(h, IOCTL_NODE_INFO, node)) return;
        int ports = node[6]; // bNumberOfPorts

        for (uint p = 1; p <= ports; p++)
        {
            var c = new byte[35]; // USB_NODE_CONNECTION_INFORMATION_EX (packed)
            BitConverter.GetBytes(p).CopyTo(c, 0);
            if (!Ioctl(h, IOCTL_CONN_EX, c) || BitConverter.ToInt32(c, 31) != 1) continue; // 1 = device connected

            ushort vid = BitConverter.ToUInt16(c, 12), pid = BitConverter.ToUInt16(c, 14);
            byte iProduct = c[19], speed = c[23];
            bool isHub = c[24] != 0;

            // V2 tells us what the device is *capable* of vs. what it's *running at*
            var v2 = new byte[16];
            BitConverter.GetBytes(p).CopyTo(v2, 0);
            BitConverter.GetBytes(16).CopyTo(v2, 4);
            uint flags = Ioctl(h, IOCTL_CONN_V2, v2) ? BitConverter.ToUInt32(v2, 12) : 0;
            bool opSS = (flags & 1) != 0, capSS = (flags & 2) != 0, opSSP = (flags & 4) != 0, capSSP = (flags & 8) != 0;

            string speedText = opSSP ? "SuperSpeed+ (10 Gbps+)" : opSS ? "SuperSpeed (5 Gbps)" : speed switch
            {
                0 => "Low (1.5 Mbps)",
                1 => "Full (12 Mbps)",
                2 => "High (480 Mbps)",
                3 => "SuperSpeed (5 Gbps)",
                _ => "Unknown"
            };

            string note = "";
            if (capSS && !opSS) note = "⚠ Supports USB 3 but running slower - try another port/cable";
            else if (capSSP && !opSSP) note = "⚠ Supports 10 Gbps but running at 5 Gbps";

            string name = GetString(h, p, iProduct) ?? "USB device";
            if (isHub) name += " (hub)";
            result.Add(new UsbDev(name, $"{vid:X4}:{pid:X4}", speedText, note));
        }
    }

    // Reads the product-name string descriptor from the device on a given port
    static string? GetString(SafeFileHandle h, uint port, byte index)
    {
        if (index == 0) return null;
        var b = new byte[12 + 255];
        BitConverter.GetBytes(port).CopyTo(b, 0);
        b[4] = 0x80; b[5] = 6; // GET_DESCRIPTOR
        BitConverter.GetBytes((ushort)((3 << 8) | index)).CopyTo(b, 6); // string descriptor
        BitConverter.GetBytes((ushort)0x0409).CopyTo(b, 8);             // English (US)
        BitConverter.GetBytes((ushort)255).CopyTo(b, 10);
        if (!Ioctl(h, IOCTL_DESC, b)) return null;
        int len = b[12];
        return len > 2 ? Encoding.Unicode.GetString(b, 14, len - 2) : null;
    }

    static bool Ioctl(SafeFileHandle h, uint code, byte[] buf) =>
        DeviceIoControl(h, code, buf, buf.Length, buf, buf.Length, out _, IntPtr.Zero);

    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr SetupDiGetClassDevs(ref Guid g, string? enumerator, IntPtr hwnd, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid g, uint idx, ref SP_DEVICE_INTERFACE_DATA d);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiGetDeviceInterfaceDetailW")]
    static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA d, IntPtr detail, int size, out int required, IntPtr devInfo);
    [DllImport("setupapi.dll")]
    static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(SafeFileHandle h, uint code, byte[] inBuf, int inLen, byte[] outBuf, int outLen, out int ret, IntPtr ov);
}
