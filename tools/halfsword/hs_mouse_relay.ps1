# Passthrough input: the player's raw mouse for Half Sword. Measured 2026-10-05 22:29 (tools\gabs\mouse_gain.ps1):
# Bannerlord's GetMouseMoveX/Y passes on only part of the device's motion, x 0.77 and y 0.56 of the raw counts, slow
# moves 0.47 and fast ones 0.73. Played directly, Unreal turns the raw counts into MouseX/MouseY, so HSLink takes them
# from here instead. Windows Raw Input with RIDEV_INPUTSINK: read-only, received without focus, never takes the focus.
# Rewrites %USERPROFILE%\halfsword-decomp\hslink_mouse.txt in place about every 10 ms:
#   seq sumRawX sumRawY END        (device counts summed since the start; y grows downward, as Windows reports it)
# Exits when the Half Sword process (-GamePid) is gone. Started by run-hs-link.ps1.
param([Parameter(Mandatory = $true)][int]$GamePid)
$ErrorActionPreference = 'Stop'
Add-Type -ReferencedAssemblies System.Windows.Forms -TypeDefinition @'
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

public class HsMouseRelay : Form
{
    [StructLayout(LayoutKind.Sequential)]
    struct RAWINPUTDEVICE { public ushort UsagePage; public ushort Usage; public uint Flags; public IntPtr Target; }
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);
    [DllImport("user32.dll")]
    static extern uint GetRawInputData(IntPtr hRawInput, uint command, IntPtr data, ref uint size, uint headerSize);
    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    long _x, _y, _seq;
    // Only moves made while Bannerlord has the focus count. Raw Input sinks see the mouse everywhere, and Bannerlord keeps
    // ticking without the focus (StopGameOnFocusLost=False): on 2026-10-05 23:19 the moves the user made in other
    // windows swung Willie's aim to the floor ("le coup part totalement par terre").
    bool _bannerlordFocused;
    IntPtr _lastForeground = IntPtr.Zero;
    readonly string _path;
    readonly int _gamePid;
    readonly Timer _timer = new Timer();
    DateTime _nextGameCheck = DateTime.MinValue;

    public HsMouseRelay(string path, int gamePid)
    {
        _path = path;
        _gamePid = gamePid;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.None;
        Opacity = 0;
        Width = 1;
        Height = 1;
        _timer.Interval = 10;
        _timer.Tick += OnTick;
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams p = base.CreateParams;
            p.ExStyle |= 0x08000000 | 0x00000080 | 0x00080000 | 0x00000020; // NOACTIVATE, TOOLWINDOW, LAYERED, TRANSPARENT
            return p;
        }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        RAWINPUTDEVICE[] d = new RAWINPUTDEVICE[1];
        d[0].UsagePage = 1;
        d[0].Usage = 2;
        d[0].Flags = 0x100; // RIDEV_INPUTSINK
        d[0].Target = Handle;
        if (!RegisterRawInputDevices(d, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE))))
        {
            throw new InvalidOperationException("RegisterRawInputDevices failed " + Marshal.GetLastWin32Error());
        }
        _timer.Start();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x00FF) // WM_INPUT
        {
            uint size = 0;
            uint header = (uint)(8 + 2 * IntPtr.Size);
            GetRawInputData(m.LParam, 0x10000003, IntPtr.Zero, ref size, header);
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                if (GetRawInputData(m.LParam, 0x10000003, buf, ref size, header) == size && Marshal.ReadInt32(buf, 0) == 0)
                {
                    int off = (int)header;
                    ushort flags = (ushort)Marshal.ReadInt16(buf, off);
                    if ((flags & 1) == 0 && BannerlordFocused()) // MOUSE_MOVE_RELATIVE, in the game
                    {
                        _x += Marshal.ReadInt32(buf, off + 12);
                        _y += Marshal.ReadInt32(buf, off + 16);
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }
        base.WndProc(ref m);
    }

    // The foreground window's process, looked up again only when the foreground window changes.
    bool BannerlordFocused()
    {
        IntPtr fg = GetForegroundWindow();
        if (fg != _lastForeground)
        {
            _lastForeground = fg;
            _bannerlordFocused = false;
            uint pid;
            GetWindowThreadProcessId(fg, out pid);
            try
            {
                _bannerlordFocused = Process.GetProcessById((int)pid).ProcessName.StartsWith("Bannerlord", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                // the process is gone or not readable: not Bannerlord
            }
        }
        return _bannerlordFocused;
    }

    // A relay started again continues the previous sums, so HSLink sees no jump.
    public void ContinueFrom(string path)
    {
        try
        {
            string[] p = File.ReadAllText(path).Split(' ');
            if (p.Length >= 4 && p[3].StartsWith("END"))
            {
                _seq = long.Parse(p[0]);
                _x = long.Parse(p[1]);
                _y = long.Parse(p[2]);
            }
        }
        catch (Exception)
        {
        }
    }

    void OnTick(object sender, EventArgs e)
    {
        if (DateTime.Now >= _nextGameCheck)
        {
            _nextGameCheck = DateTime.Now.AddSeconds(1);
            bool alive;
            try
            {
                alive = !Process.GetProcessById(_gamePid).HasExited;
            }
            catch (ArgumentException)
            {
                alive = false;
            }
            if (!alive)
            {
                _timer.Stop();
                Close();
                return;
            }
        }
        _seq++;
        byte[] line = Encoding.ASCII.GetBytes(_seq + " " + _x + " " + _y + " END\n");
        try
        {
            // In place: HSLink reads while it is written and skips a line without END.
            using (FileStream s = new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            {
                s.Write(line, 0, line.Length);
            }
        }
        catch (IOException)
        {
            // Half Sword reading at that instant: the next tick writes the same sums again.
        }
    }
}
'@
$path = Join-Path $env:USERPROFILE 'halfsword-decomp\hslink_mouse.txt'
$relay = New-Object HsMouseRelay -ArgumentList $path, $GamePid
$relay.ContinueFrom($path)
[System.Windows.Forms.Application]::Run($relay)
