using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace RocketDropWpf
{
    public partial class MainWindow : Window
    {
        // WinAPI
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        private const int HOTKEY_ID_START = 1001;
        private const int HOTKEY_ID_STOP = 1002;

        // Fixed hotkeys: F4 start, F5 stop
        private const uint START_KEY = (uint)VK.F4;
        private const uint STOP_KEY = (uint)VK.F5;

        // 8.0s normal, 0.4s mit Plugin
        private double _openDelaySeconds = 8.0;

        private CancellationTokenSource? _cts;
        private HwndSource? _hwndSource;
        private DispatcherTimer? _probeTimer;

        // BakkesMod-Pfade
        private readonly string _appDir;
        private readonly string _assetsPluginDir;
        private readonly string _bakkesRoot;
        private readonly string _bakkesExe;
        private readonly string _bakkesPluginsDir;
        private readonly string _bakkesCfgDir;
        private readonly string _pluginsCfgPath;

        private const string OurPluginName = "PinkBroDisableCrateAnim"; // eigene DLL (optional)
        private const string FallbackPluginName = "DisableCrateAnim";       // Originalname

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;

            _appDir = AppDomain.CurrentDomain.BaseDirectory;
            _assetsPluginDir = Path.Combine(_appDir, "Assets", "Plugins");
            _bakkesRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "bakkesmod");
            _bakkesExe = Path.Combine(_bakkesRoot, "BakkesMod", "BakkesMod.exe");
            _bakkesPluginsDir = Path.Combine(_bakkesRoot, "bakkesmod", "plugins");
            _bakkesCfgDir = Path.Combine(_bakkesRoot, "bakkesmod", "cfg");
            _pluginsCfgPath = Path.Combine(_bakkesCfgDir, "plugins.cfg");
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var helper = new WindowInteropHelper(this);
            _hwndSource = HwndSource.FromHwnd(helper.Handle);
            _hwndSource.AddHook(WndProc);

            RebindHotkeys();

            _probeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _probeTimer.Tick += (s, _) =>
            {
                UpdatePresenceBadge();
                UpdateIntegrationStatus();
            };
            _probeTimer.Start();

            Status("Ready.");
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            try
            {
                var helper = new WindowInteropHelper(this);
                UnregisterHotKey(helper.Handle, HOTKEY_ID_START);
                UnregisterHotKey(helper.Handle, HOTKEY_ID_STOP);
            }
            catch { /* ignore */ }

            _cts?.Cancel();
            _probeTimer?.Stop();
        }

        // ---------------- Hotkeys ----------------
        private void RebindHotkeys()
        {
            var helper = new WindowInteropHelper(this);
            try
            {
                UnregisterHotKey(helper.Handle, HOTKEY_ID_START);
                UnregisterHotKey(helper.Handle, HOTKEY_ID_STOP);
            }
            catch { /* ignore */ }

            RegisterGlobalHotkey(HOTKEY_ID_START, 0, START_KEY);
            RegisterGlobalHotkey(HOTKEY_ID_STOP, 0, STOP_KEY);
        }

        private void RegisterGlobalHotkey(int id, uint modifiers, uint vk)
        {
            var helper = new WindowInteropHelper(this);
            if (!RegisterHotKey(helper.Handle, id, modifiers, vk))
                Status($"Hotkey {id} failed (Admin required?).");
        }

        // ---------------- Main loop ----------------
        private void StartLoop()
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                Status("Already running.");
                return;
            }

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            var procName = NormalizeProcName(GameProcBox.Text.Trim()); // "RocketLeague" aus "RocketLeague.exe"
            Status("Running…");

            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    var (ok, hWnd) = TryGetGameWindowByProcess(procName);
                    if (!ok)
                    {
                        await Task.Delay(1000, token).ConfigureAwait(false);
                        continue;
                    }

                    SetForegroundWindow(hWnd);
                    await Task.Delay(100, token).ConfigureAwait(false);

                    SendKeystroke(VK.RETURN);
                    await Task.Delay(300, token).ConfigureAwait(false);

                    SendKeystroke(VK.LEFT);
                    await Task.Delay(300, token).ConfigureAwait(false);

                    SendKeystroke(VK.RETURN);
                    await Task.Delay(TimeSpan.FromSeconds(_openDelaySeconds), token).ConfigureAwait(false);

                    SendKeystroke(VK.RETURN);
                    await Task.Delay(50, token).ConfigureAwait(false);
                }
            }, token);
        }

        private void StopLoop()
        {
            _cts?.Cancel();
            _cts = null;
            Status("Stopped.");
        }

        // ---------------- Process lookup ----------------
        private static string NormalizeProcName(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "RocketLeague";
            return input.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? input[..^4]
                : input;
        }

        private (bool ok, IntPtr hWnd) TryGetGameWindowByProcess(string procNameNoExt)
        {
            try
            {
                var procs = Process.GetProcessesByName(procNameNoExt);
                foreach (var p in procs)
                {
                    if (p.MainWindowHandle != IntPtr.Zero)
                        return (true, p.MainWindowHandle);
                }
            }
            catch { /* ignore */ }
            return (false, IntPtr.Zero);
        }

        // ---------------- Status badge ----------------
        private void UpdatePresenceBadge()
        {
            var procName = NormalizeProcName(GameProcBox.Text.Trim());
            var (ok, _) = TryGetGameWindowByProcess(procName);

            if (ok)
            {
                // weich/grün
                StatusBadge.Background = new SolidColorBrush(Color.FromArgb(0x88, 0x33, 0xAA, 0x66));
                StatusText.Text = "Status: Rocket League detected ✅";
            }
            else
            {
                // weich/rot
                StatusBadge.Background = new SolidColorBrush(Color.FromArgb(0x88, 0xC2, 0x3B, 0x3B));
                StatusText.Text = "Status: no window found ❌";
            }
        }

        private void Status(string text)
        {
            Dispatcher.Invoke(() => StatusText.Text = $"Status: {text}");
        }

        // ---------------- WndProc (global hotkeys) ----------------
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (id == HOTKEY_ID_START)
                {
                    RebindHotkeys();
                    StartLoop();
                    handled = true;
                }
                else if (id == HOTKEY_ID_STOP)
                {
                    StopLoop();
                    Application.Current.Shutdown(); // exit on stop
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        // ---------------- BakkesMod Integration ----------------
        private void UpdateIntegrationStatus()
        {
            if (BakkesStatusText == null || PluginLineText == null) return;

            bool hasBakkes = Process.GetProcessesByName("BakkesMod").Length > 0;
            BakkesStatusText.Text = hasBakkes ? "BakkesMod: running ✅" : "BakkesMod: not running ❌";

            string dllOur = Path.Combine(_bakkesPluginsDir, $"{OurPluginName}.dll");
            string dllOrig = Path.Combine(_bakkesPluginsDir, $"{FallbackPluginName}.dll");
            bool hasPlugin = File.Exists(dllOur) || File.Exists(dllOrig);

            // Auto delay switch
            _openDelaySeconds = hasPlugin ? 4.0 : 8.0;

            // Eine kompakte Zeile inkl. Delay
            PluginLineText.Text = hasPlugin
                ? "Plugin: found  —  reduced delay ~4 s"
                : "Plugin: not found  —  delay ~8 s";
        }

        private void InstallBakkesMod_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (File.Exists(_bakkesExe))
                {
                    Status("BakkesMod already installed.");
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://bakkesmod.com",
                    UseShellExecute = true
                });
                Status("Opened BakkesMod website. Install it, then return.");
            }
            catch (Exception ex)
            {
                Status($"BakkesMod install/open failed: {ex.Message}");
            }
        }

        private void InstallPlugin_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(_bakkesPluginsDir);
                Directory.CreateDirectory(_bakkesCfgDir);

                string srcOur = Path.Combine(_assetsPluginDir, $"{OurPluginName}.dll");
                string srcOrig = Path.Combine(_assetsPluginDir, $"{FallbackPluginName}.dll");
                string chosenSrc = File.Exists(srcOur) ? srcOur : (File.Exists(srcOrig) ? srcOrig : "");

                if (string.IsNullOrEmpty(chosenSrc))
                {
                    Status("No plugin DLL found in Assets/Plugins.");
                    return;
                }

                string dstName = Path.GetFileName(chosenSrc);
                string dst = Path.Combine(_bakkesPluginsDir, dstName);
                File.Copy(chosenSrc, dst, overwrite: true);

                string pluginBase = Path.GetFileNameWithoutExtension(dstName);
                EnsurePluginsCfgLoad(pluginBase);

                Status($"Installed/updated plugin '{pluginBase}'.");
                UpdateIntegrationStatus();
            }
            catch (Exception ex)
            {
                Status($"Plugin install failed: {ex.Message}");
            }
        }

        private void EnsurePluginsCfgLoad(string pluginBaseName)
        {
            try
            {
                if (!File.Exists(_pluginsCfgPath))
                    File.WriteAllText(_pluginsCfgPath, "");

                var lines = File.ReadAllLines(_pluginsCfgPath);
                string loadLine = $"plugin load {pluginBaseName}";
                foreach (var l in lines)
                    if (l.Trim().Equals(loadLine, StringComparison.OrdinalIgnoreCase))
                        return;

                using var sw = new StreamWriter(_pluginsCfgPath, append: true, Encoding.UTF8);
                sw.WriteLine(loadLine);
            }
            catch { /* ignore */ }
        }

        // ---------------- Eingabesimulation ----------------
        private static void SendKeystroke(VK key)
        {
            var inputs = new INPUT[2];
            inputs[0] = new INPUT
            {
                type = 1, // INPUT_KEYBOARD
                U = new InputUnion { ki = new KEYBDINPUT { wVk = key, dwFlags = 0 } }
            };
            inputs[1] = new INPUT
            {
                type = 1, // INPUT_KEYBOARD
                U = new InputUnion { ki = new KEYBDINPUT { wVk = key, dwFlags = 2 } } // KEYEVENTF_KEYUP
            };
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        // ---------------- WinAPI structs ----------------
        private enum VK : ushort
        {
            LEFT = 0x25, UP = 0x26, RIGHT = 0x27, DOWN = 0x28,
            RETURN = 0x0D, ESCAPE = 0x1B,
            F1 = 0x70, F2 = 0x71, F3 = 0x72, F4 = 0x73, F5 = 0x74,
            F6 = 0x75, F7 = 0x76, F8 = 0x77, F9 = 0x78, F10 = 0x79,
            F11 = 0x7A, F12 = 0x7B, F13 = 0x7C, F14 = 0x7D, F15 = 0x7E,
            F16 = 0x7F, F17 = 0x80, F18 = 0x81, F19 = 0x82, F20 = 0x83,
            F21 = 0x84, F22 = 0x85, F23 = 0x86, F24 = 0x87
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT { public int type; public InputUnion U; }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx, dy, mouseData, dwFlags, time; public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public VK wVk; public ushort wScan; public int dwFlags; public int time; public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT { public int uMsg; public short wParamL, wParamH; }
    }
}
