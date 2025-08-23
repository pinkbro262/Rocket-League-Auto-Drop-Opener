using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
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

        private const double OPEN_DELAY_SECONDS = 8.0; // fixed opener delay

        private CancellationTokenSource? _cts;
        private HwndSource? _hwndSource;
        private DispatcherTimer? _probeTimer;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var helper = new WindowInteropHelper(this);
            _hwndSource = HwndSource.FromHwnd(helper.Handle);
            _hwndSource.AddHook(WndProc);

            // register initial hotkeys
            RebindHotkeys();

            // periodic presence check (status badge)
            _probeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _probeTimer.Tick += (s, _) => UpdatePresenceBadge();
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

        // hotkeys
        private void RebindHotkeys()
        {
            var helper = new WindowInteropHelper(this);
            try
            {
                UnregisterHotKey(helper.Handle, HOTKEY_ID_START);
                UnregisterHotKey(helper.Handle, HOTKEY_ID_STOP);
            }
            catch { /* ignore */ }

            RegisterGlobalHotkey(HOTKEY_ID_START, 0, KeyNameToVk(StartKeyBox.Text));
            RegisterGlobalHotkey(HOTKEY_ID_STOP, 0, KeyNameToVk(StopKeyBox.Text));
        }

        private void RegisterGlobalHotkey(int id, uint modifiers, uint vk)
        {
            var helper = new WindowInteropHelper(this);
            if (!RegisterHotKey(helper.Handle, id, modifiers, vk))
                Status($"Hotkey {id} failed (Admin required?).");
        }

        // buttons
        private void StartBtn_Click(object sender, RoutedEventArgs e)
        {
            RebindHotkeys();
            StartLoop();
        }

        private void StopBtn_Click(object sender, RoutedEventArgs e)
        {
            StopLoop();
        }

        // main loop
        private void StartLoop()
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                Status("Already running.");
                return;
            }

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            var procName = NormalizeProcName(GameProcBox.Text.Trim()); // RocketLeague from RocketLeague.exe

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
                    await Task.Delay(TimeSpan.FromSeconds(OPEN_DELAY_SECONDS), token).ConfigureAwait(false);

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

        // process lookup
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

        // status badge
        private void UpdatePresenceBadge()
        {
            var procName = NormalizeProcName(GameProcBox.Text.Trim());
            var (ok, _) = TryGetGameWindowByProcess(procName);

            if (ok)
            {
                StatusBadge.Background = new SolidColorBrush(Color.FromRgb(0x33, 0xAA, 0x66)); // green
                StatusText.Text = "Status: Rocket League detected ✅";
            }
            else
            {
                StatusBadge.Background = new SolidColorBrush(Color.FromRgb(0xAA, 0x33, 0x33)); // red
                StatusText.Text = "Status: no window found ❌";
            }
        }

        private void Status(string text)
        {
            Dispatcher.Invoke(() => StatusText.Text = $"Status: {text}");
        }

        // global hotkey messages
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
                    Application.Current.Shutdown(); // exit on stop hotkey
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        // hotkey capture
        private string _lastPlaceholder = "";

        private void HotkeyBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox tb)
            {
                _lastPlaceholder = tb.Text;
                tb.Text = "Press any key…";
                tb.SelectAll();
            }
        }

        private void HotkeyBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox tb)
            {
                if (tb.Text == "Press any key…" || string.IsNullOrWhiteSpace(tb.Text))
                    tb.Text = _lastPlaceholder;
                RebindHotkeys();
            }
        }

        private void StartKeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            var key = ExtractKey(e);
            StartKeyBox.Text = key;
            StartKeyBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        }

        private void StopKeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            var key = ExtractKey(e);
            StopKeyBox.Text = key;
            StopKeyBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        }

        private static string ExtractKey(KeyEventArgs e)
        {
            var k = (e.Key == Key.System) ? e.SystemKey : e.Key;
            return k switch
            {
                Key.Return => "ENTER",
                Key.Left => "LEFT",
                Key.Right => "RIGHT",
                Key.Up => "UP",
                Key.Down => "DOWN",
                _ => k.ToString().ToUpperInvariant()
            };
        }

        private static uint KeyNameToVk(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return (uint)VK.F1;
            name = name.Trim().ToUpperInvariant();

            if (name.StartsWith("F") && int.TryParse(name[1..], out int fn) && fn >= 1 && fn <= 24)
                return (uint)((int)VK.F1 + (fn - 1));

            return name switch
            {
                "ENTER" => (uint)VK.RETURN,
                "LEFT" => (uint)VK.LEFT,
                "RIGHT" => (uint)VK.RIGHT,
                "UP" => (uint)VK.UP,
                "DOWN" => (uint)VK.DOWN,
                "ESC" or "ESCAPE" => (uint)VK.ESCAPE,
                _ => (uint)VK.F1
            };
        }

        private static void SendKeystroke(VK key)
        {
            var inputs = new INPUT[2];
            inputs[0] = new INPUT { type = 1, U = new InputUnion { ki = new KEYBDINPUT { wVk = key, dwFlags = 0 } } };
            inputs[1] = new INPUT { type = 1, U = new InputUnion { ki = new KEYBDINPUT { wVk = key, dwFlags = 2 } } }; // KEYUP
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        // WinAPI structs
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