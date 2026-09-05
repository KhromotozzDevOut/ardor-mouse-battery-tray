using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

[assembly: AssemblyTitle("ARDOR Mouse Battery Tray")]
[assembly: AssemblyDescription("Battery indicator and desktop overlay for supported ARDOR GAMING mice")]
[assembly: AssemblyCompany("ArdorBatteryTray contributors")]
[assembly: AssemblyProduct("ARDOR Mouse Battery Tray")]
[assembly: AssemblyCopyright("Copyright © 2026")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace ArdorBatteryTray
{
    internal static class Program
    {
        private const string MutexName = "Local\\ArdorChimeraBatteryTray";

        [STAThread]
        private static void Main(string[] args)
        {
            if (args != null && args.Length > 0 &&
                string.Equals(args[0], "--probe", StringComparison.OrdinalIgnoreCase))
            {
                ArdorHid.Verbose = true;
                BatteryReading reading = ArdorHid.Read();
                if (reading == null)
                {
                    Console.WriteLine("NO_RESPONSE");
                    Environment.ExitCode = 2;
                }
                else
                {
                    Console.WriteLine("BATTERY={0};CHARGING={1};WIRED={2};MODE={3}",
                        reading.Percent, reading.Charging ? 1 : 0,
                        reading.Wired ? 1 : 0, reading.Mode);
                }
                return;
            }

            bool created;
            using (var mutex = new Mutex(true, MutexName, out created))
            {
                if (!created)
                    return;

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (var context = new TrayContext())
                    Application.Run(context);
            }
        }
    }

    internal sealed class TrayContext : ApplicationContext, IDisposable
    {
        private const string StartupValueName = "ArdorChimeraBatteryTray";
        private const string StartupKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        private readonly NotifyIcon tray;
        private readonly Control dispatcher;
        private readonly System.Threading.Timer pollTimer;
        private readonly ToolStripMenuItem statusItem;
        private readonly ToolStripMenuItem startupItem;
        private readonly ToolStripMenuItem widgetToggleItem;
        private readonly ToolStripMenuItem moveWidgetItem;
        private readonly List<ToolStripMenuItem> widgetPositionItems = new List<ToolStripMenuItem>();
        private WidgetForm widget;
        private int polling;
        private bool disposed;
        private int lastPercent = -1;
        private bool lastCharging;
        private string lastMode = "";
        private string lastDeviceName = "ARDOR";
        private DateTime lastGoodReading = DateTime.MinValue;
        private int lastAlertLevel = -1;

        public TrayContext()
        {
            dispatcher = new Control();
            dispatcher.CreateControl();

            statusItem = new ToolStripMenuItem("Поиск совместимой мыши ARDOR…") { Enabled = false };
            startupItem = new ToolStripMenuItem("Запускать вместе с Windows")
            {
                CheckOnClick = false,
                Checked = IsStartupEnabled()
            };
            startupItem.Click += delegate { ToggleStartup(); };

            widgetToggleItem = new ToolStripMenuItem("Показывать виджет поверх окон")
            {
                CheckOnClick = false,
                Checked = WidgetSettings.Enabled
            };
            widgetToggleItem.Click += delegate { ToggleWidget(); };

            moveWidgetItem = new ToolStripMenuItem("Переместить виджет…")
            {
                Enabled = WidgetSettings.Enabled
            };
            moveWidgetItem.Click += delegate { ToggleMoveMode(); };

            var positionMenu = new ToolStripMenuItem("Положение виджета");
            positionMenu.DropDownItems.Add(CreatePositionItem("Сверху слева", "TopLeft"));
            positionMenu.DropDownItems.Add(CreatePositionItem("Сверху справа", "TopRight"));
            positionMenu.DropDownItems.Add(CreatePositionItem("Снизу слева", "BottomLeft"));
            positionMenu.DropDownItems.Add(CreatePositionItem("Снизу справа", "BottomRight"));
            positionMenu.DropDownItems.Add(new ToolStripSeparator());
            positionMenu.DropDownItems.Add(CreatePositionItem("Последнее свободное положение", "Custom"));

            var sizeMenu = new ToolStripMenuItem("Размер виджета");
            sizeMenu.DropDownItems.Add(CreateSizeItem("Маленький", 48));
            sizeMenu.DropDownItems.Add(CreateSizeItem("Средний", 72));
            sizeMenu.DropDownItems.Add(CreateSizeItem("Большой", 110));
            sizeMenu.DropDownItems.Add(CreateSizeItem("Очень большой", 160));

            var opacityMenu = new ToolStripMenuItem("Прозрачность виджета");
            opacityMenu.DropDownItems.Add(CreateOpacityItem("Непрозрачный", 100));
            opacityMenu.DropDownItems.Add(CreateOpacityItem("Слабая — 85%", 85));
            opacityMenu.DropDownItems.Add(CreateOpacityItem("Средняя — 65%", 65));
            opacityMenu.DropDownItems.Add(CreateOpacityItem("Сильная — 45%", 45));

            var refreshItem = new ToolStripMenuItem("Обновить сейчас");
            refreshItem.Click += delegate { QueuePoll(); };

            var openDriverItem = new ToolStripMenuItem("Открыть программу ARDOR");
            openDriverItem.Click += delegate { OpenDriver(); };

            var exitItem = new ToolStripMenuItem("Выход");
            exitItem.Click += delegate { ExitThread(); };

            var menu = new ContextMenuStrip();
            menu.Items.Add(statusItem);
            menu.Items.Add(refreshItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(widgetToggleItem);
            menu.Items.Add(moveWidgetItem);
            menu.Items.Add(positionMenu);
            menu.Items.Add(sizeMenu);
            menu.Items.Add(opacityMenu);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(startupItem);
            menu.Items.Add(openDriverItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            tray = new NotifyIcon
            {
                Visible = true,
                Text = "ARDOR: поиск совместимой мыши…",
                Icon = TrayIconFactory.Create(null, false, false),
                ContextMenuStrip = menu
            };
            tray.DoubleClick += delegate { ShowCurrentStatus(); };

            if (WidgetSettings.Enabled)
                ShowWidget();

            pollTimer = new System.Threading.Timer(delegate { QueuePoll(); }, null, 100, 30000);
        }

        private ToolStripMenuItem CreatePositionItem(string label, string value)
        {
            var item = new ToolStripMenuItem(label)
            {
                CheckOnClick = false,
                Checked = string.Equals(WidgetSettings.Position, value, StringComparison.OrdinalIgnoreCase)
            };
            item.Tag = value;
            item.Click += delegate
            {
                WidgetSettings.Position = value;
                UpdatePositionChecks();
                if (widget != null && !widget.IsDisposed)
                    widget.SetMoveMode(false, false);
                UpdateMoveMenu();
                ApplyWidgetSettings();
            };
            widgetPositionItems.Add(item);
            return item;
        }

        private ToolStripMenuItem CreateSizeItem(string label, int value)
        {
            var item = new ToolStripMenuItem(label)
            {
                CheckOnClick = false,
                Checked = WidgetSettings.Size == value
            };
            item.Click += delegate
            {
                WidgetSettings.Size = value;
                foreach (ToolStripMenuItem sibling in item.Owner.Items)
                    sibling.Checked = ReferenceEquals(sibling, item);
                ApplyWidgetSettings();
            };
            return item;
        }

        private ToolStripMenuItem CreateOpacityItem(string label, int value)
        {
            var item = new ToolStripMenuItem(label)
            {
                CheckOnClick = false,
                Checked = WidgetSettings.OpacityPercent == value
            };
            item.Click += delegate
            {
                WidgetSettings.OpacityPercent = value;
                foreach (ToolStripMenuItem sibling in item.Owner.Items)
                    sibling.Checked = ReferenceEquals(sibling, item);
                ApplyWidgetSettings();
            };
            return item;
        }

        private void ToggleWidget()
        {
            WidgetSettings.Enabled = !WidgetSettings.Enabled;
            widgetToggleItem.Checked = WidgetSettings.Enabled;
            moveWidgetItem.Enabled = WidgetSettings.Enabled;
            if (WidgetSettings.Enabled)
                ShowWidget();
            else if (widget != null)
            {
                widget.SetMoveMode(false, false);
                widget.Hide();
            }
        }

        private void ToggleMoveMode()
        {
            if (!WidgetSettings.Enabled)
            {
                WidgetSettings.Enabled = true;
                widgetToggleItem.Checked = true;
                moveWidgetItem.Enabled = true;
                ShowWidget();
            }

            if (widget == null || widget.IsDisposed)
                ShowWidget();

            bool finish = widget.IsMoveMode;
            widget.SetMoveMode(!finish, finish);
            UpdateMoveMenu();
            if (finish)
                UpdatePositionChecks();
        }

        private void UpdateMoveMenu()
        {
            bool moving = widget != null && !widget.IsDisposed && widget.IsMoveMode;
            moveWidgetItem.Text = moving ? "Закрепить виджет здесь" : "Переместить виджет…";
            moveWidgetItem.Checked = moving;
        }

        private void ShowWidget()
        {
            if (widget == null || widget.IsDisposed)
            {
                widget = new WidgetForm();
                widget.PositionChangedByUser += delegate
                {
                    UpdatePositionChecks();
                };
                widget.ConfirmMoveRequested += delegate
                {
                    widget.SetMoveMode(false, true);
                    UpdateMoveMenu();
                    UpdatePositionChecks();
                };
                widget.HideRequested += delegate
                {
                    WidgetSettings.Enabled = false;
                    widgetToggleItem.Checked = false;
                    widget.Hide();
                };
            }
            ApplyWidgetSettings();
            widget.SetReading(lastPercent >= 0 ? (int?)lastPercent : null, lastCharging, false, lastMode);
            widget.Show();
            widget.BringToFront();
            UpdateMoveMenu();
        }

        private void ApplyWidgetSettings()
        {
            if (widget == null || widget.IsDisposed)
            {
                if (WidgetSettings.Enabled)
                    ShowWidget();
                return;
            }
            widget.ApplySettings(WidgetSettings.Size, WidgetSettings.OpacityPercent, WidgetSettings.Position);
        }

        private void UpdatePositionChecks()
        {
            foreach (ToolStripMenuItem item in widgetPositionItems)
                item.Checked = string.Equals((string)item.Tag, WidgetSettings.Position,
                    StringComparison.OrdinalIgnoreCase);
        }

        private void QueuePoll()
        {
            if (disposed || Interlocked.Exchange(ref polling, 1) != 0)
                return;

            ThreadPool.QueueUserWorkItem(delegate
            {
                BatteryReading reading;
                try
                {
                    reading = ArdorHid.Read();
                }
                catch
                {
                    reading = null;
                }

                try
                {
                    if (!disposed && dispatcher.IsHandleCreated)
                        dispatcher.BeginInvoke((Action)(delegate { ApplyReading(reading); }));
                }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
                finally
                {
                    Interlocked.Exchange(ref polling, 0);
                }
            });
        }

        private void ApplyReading(BatteryReading reading)
        {
            if (disposed)
                return;

            if (reading != null)
            {
                lastPercent = reading.Percent;
                lastCharging = reading.Charging;
                lastMode = reading.Mode;
                lastDeviceName = string.IsNullOrEmpty(reading.DeviceName) ? "ARDOR" : reading.DeviceName;
                lastGoodReading = DateTime.Now;

                string suffix;
                if (reading.Charging)
                    suffix = "заряжается";
                else if (reading.Wired)
                    suffix = "по кабелю";
                else
                    suffix = "2,4 ГГц";

                string text = string.Format("{0}: {1}% — {2}", lastDeviceName, reading.Percent, suffix);
                statusItem.Text = text;
                tray.Text = LimitTooltip(text);
                ReplaceIcon(TrayIconFactory.Create(reading.Percent, reading.Charging, false));
                if (widget != null && !widget.IsDisposed)
                    widget.SetReading(reading.Percent, reading.Charging, false, suffix);
                MaybeAlert(reading);
                return;
            }

            bool hasFreshCache = lastPercent >= 0 &&
                                 lastGoodReading != DateTime.MinValue &&
                                 DateTime.Now - lastGoodReading < TimeSpan.FromMinutes(5);
            if (hasFreshCache)
            {
                string text = string.Format("{0}: {1}% — мышь спит", lastDeviceName, lastPercent);
                statusItem.Text = text;
                tray.Text = LimitTooltip(text);
                ReplaceIcon(TrayIconFactory.Create(lastPercent, lastCharging, true));
                if (widget != null && !widget.IsDisposed)
                    widget.SetReading(lastPercent, lastCharging, true, "мышь спит");
            }
            else
            {
                const string text = "ARDOR: совместимая мышь не отвечает";
                statusItem.Text = text;
                tray.Text = text;
                ReplaceIcon(TrayIconFactory.Create(null, false, false));
                if (widget != null && !widget.IsDisposed)
                    widget.SetReading(null, false, false, "нет ответа");
            }
        }

        private void MaybeAlert(BatteryReading reading)
        {
            if (reading.Charging || reading.Wired)
            {
                lastAlertLevel = -1;
                return;
            }

            int alertLevel = reading.Percent <= 5 ? 5 : reading.Percent <= 10 ? 10 : reading.Percent <= 20 ? 20 : -1;
            if (alertLevel < 0)
            {
                if (reading.Percent > 25)
                    lastAlertLevel = -1;
                return;
            }

            if (lastAlertLevel == alertLevel)
                return;

            lastAlertLevel = alertLevel;
            tray.BalloonTipTitle = "Низкий заряд мыши ARDOR";
            tray.BalloonTipText = string.Format("Осталось {0}%. Подключите мышь к зарядке.", reading.Percent);
            tray.BalloonTipIcon = alertLevel <= 5 ? ToolTipIcon.Error : ToolTipIcon.Warning;
            tray.ShowBalloonTip(5000);
        }

        private void ShowCurrentStatus()
        {
            tray.BalloonTipTitle = lastDeviceName;
            tray.BalloonTipText = statusItem.Text;
            tray.BalloonTipIcon = ToolTipIcon.Info;
            tray.ShowBalloonTip(3000);
        }

        private void ToggleStartup()
        {
            bool enable = !IsStartupEnabled();
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(StartupKeyPath))
                {
                    if (enable)
                        key.SetValue(StartupValueName, "\"" + Application.ExecutablePath + "\"");
                    else
                        key.DeleteValue(StartupValueName, false);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось изменить автозапуск:\n" + ex.Message,
                    "ARDOR Battery Tray", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            startupItem.Checked = IsStartupEnabled();
        }

        private static bool IsStartupEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupKeyPath))
                    return key != null && key.GetValue(StartupValueName) != null;
            }
            catch { return false; }
        }

        private static void OpenDriver()
        {
            try
            {
                string driverPath = FindDriverPath();
                if (!string.IsNullOrEmpty(driverPath))
                    Process.Start(driverPath);
                else
                    MessageBox.Show("Фирменная программа ARDOR не найдена. Индикатор заряда может работать без неё.", "ARDOR Battery Tray",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось открыть программу ARDOR:\n" + ex.Message, "ARDOR Battery Tray",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static string FindDriverPath()
        {
            string[] directCandidates =
            {
                @"C:\Program Files (x86)\ARDOR GAMING\Chimera\OemDrv.exe",
                @"C:\Program Files (x86)\ARDOR GAMING\Essence\OemDrv.exe",
                @"C:\Program Files (x86)\ARDOR GAMING\Phantom Wireless\OemDrv.exe",
                @"C:\Program Files (x86)\ARDOR GAMING\Prime Wireless\OemDrv.exe"
            };
            foreach (string candidate in directCandidates)
                if (File.Exists(candidate))
                    return candidate;

            string[] roots =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "ARDOR GAMING"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ARDOR GAMING")
            };
            foreach (string root in roots)
            {
                if (!Directory.Exists(root))
                    continue;
                try
                {
                    string[] found = Directory.GetFiles(root, "OemDrv.exe", SearchOption.AllDirectories);
                    if (found.Length > 0)
                        return found[0];
                    found = Directory.GetFiles(root, "Mouse Drive*.exe", SearchOption.AllDirectories);
                    if (found.Length > 0)
                        return found[0];
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }
            return null;
        }

        private void ReplaceIcon(Icon next)
        {
            Icon previous = tray.Icon;
            tray.Icon = next;
            if (previous != null)
                previous.Dispose();
        }

        private static string LimitTooltip(string value)
        {
            return value.Length <= 63 ? value : value.Substring(0, 63);
        }

        protected override void ExitThreadCore()
        {
            Dispose();
            base.ExitThreadCore();
        }

        public new void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            pollTimer.Dispose();
            if (widget != null)
                widget.Dispose();
            tray.Visible = false;
            if (tray.Icon != null)
                tray.Icon.Dispose();
            tray.Dispose();
            dispatcher.Dispose();
        }
    }

    internal sealed class BatteryReading
    {
        public string DeviceName;
        public int Percent;
        public bool Charging;
        public bool Wired;
        public string Mode;
    }

    internal static class WidgetSettings
    {
        private const string KeyPath = @"Software\ArdorChimeraBatteryTray";

        public static bool Enabled
        {
            get { return GetInt("WidgetEnabled", 0) != 0; }
            set { SetValue("WidgetEnabled", value ? 1 : 0); }
        }

        public static int Size
        {
            get
            {
                int value = GetInt("WidgetSize", 72);
                // Migrate sizes saved by the previous, panel-style widget.
                if (value == 80) return 48;
                if (value == 120) return 72;
                if (value == 170) return 110;
                if (value == 230) return 160;
                return value == 48 || value == 72 || value == 110 || value == 160 ? value : 72;
            }
            set { SetValue("WidgetSize", value); }
        }

        public static int OpacityPercent
        {
            get
            {
                int value = GetInt("WidgetOpacity", 85);
                return value == 100 || value == 85 || value == 65 || value == 45 ? value : 85;
            }
            set { SetValue("WidgetOpacity", value); }
        }

        public static string Position
        {
            get { return GetString("WidgetPosition", "TopRight"); }
            set { SetValue("WidgetPosition", value); }
        }

        public static int X
        {
            get { return GetInt("WidgetX", 30); }
            set { SetValue("WidgetX", value); }
        }

        public static int Y
        {
            get { return GetInt("WidgetY", 30); }
            set { SetValue("WidgetY", value); }
        }

        private static int GetInt(string name, int fallback)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(KeyPath))
                {
                    object value = key == null ? null : key.GetValue(name);
                    return value == null ? fallback : Convert.ToInt32(value);
                }
            }
            catch { return fallback; }
        }

        private static string GetString(string name, string fallback)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(KeyPath))
                {
                    object value = key == null ? null : key.GetValue(name);
                    string text = value as string;
                    return string.IsNullOrEmpty(text) ? fallback : text;
                }
            }
            catch { return fallback; }
        }

        private static void SetValue(string name, object value)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath))
                    key.SetValue(name, value);
            }
            catch { }
        }
    }

    internal sealed class WidgetForm : Form
    {
        private static readonly IntPtr HwndTopmost = new IntPtr(-1);
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpFrameChanged = 0x0020;
        private const uint SwpShowWindow = 0x0040;
        private const int WsExToolWindow = 0x00000080;
        private const int WsExTransparent = 0x00000020;
        private const int WsExNoActivate = 0x08000000;
        private const int GwlExStyle = -20;

        private int? percent;
        private bool charging;
        private bool sleeping;
        private string stateText = "поиск мыши";
        private bool dragging;
        private bool moveMode;
        private Point dragOffset;
        private readonly System.Windows.Forms.Timer keepAboveGamesTimer;
        private readonly ToolStripMenuItem confirmMoveItem;

        public event Action PositionChangedByUser;
        public event Action ConfirmMoveRequested;
        public event Action HideRequested;

        public bool IsMoveMode { get { return moveMode; } }

        public WidgetForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            DoubleBuffered = true;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            Cursor = Cursors.Default;
            Text = "ARDOR Mouse Battery";

            confirmMoveItem = new ToolStripMenuItem("Закрепить здесь");
            confirmMoveItem.Click += delegate
            {
                Action handler = ConfirmMoveRequested;
                if (handler != null)
                    handler();
            };
            var hideItem = new ToolStripMenuItem("Скрыть виджет");
            hideItem.Click += delegate
            {
                Action handler = HideRequested;
                if (handler != null)
                    handler();
            };
            var menu = new ContextMenuStrip();
            menu.Items.Add(confirmMoveItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(hideItem);
            ContextMenuStrip = menu;

            // Full-screen borderless games sometimes restore their own z-order after
            // focus changes. Reasserting HWND_TOPMOST keeps this non-activating overlay
            // above them without stealing keyboard or mouse focus from the game.
            keepAboveGamesTimer = new System.Windows.Forms.Timer { Interval = 500 };
            keepAboveGamesTimer.Tick += delegate { KeepAboveGames(); };
            keepAboveGamesTimer.Start();

            ApplySettings(WidgetSettings.Size, WidgetSettings.OpacityPercent, WidgetSettings.Position);
            SetMoveMode(false, false);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= WsExToolWindow | WsExNoActivate;
                if (!moveMode)
                    parameters.ExStyle |= WsExTransparent;
                return parameters;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        public void SetReading(int? value, bool isCharging, bool isSleeping, string status)
        {
            percent = value;
            charging = isCharging;
            sleeping = isSleeping;
            stateText = string.IsNullOrEmpty(status) ? "" : status;
            Invalidate();
        }

        public void ApplySettings(int width, int opacityPercent, string position)
        {
            int height = Math.Max(22, (int)Math.Round(width * 0.52));
            Size = new Size(width, height);
            Opacity = Math.Max(0.2, Math.Min(1.0, opacityPercent / 100.0));
            UpdateRoundedRegion();

            if (string.Equals(position, "Custom", StringComparison.OrdinalIgnoreCase))
                Location = EnsureVisible(new Point(WidgetSettings.X, WidgetSettings.Y));
            else
                Location = NamedLocation(position);
            TopMost = true;
            KeepAboveGames();
            Invalidate();
        }

        public void SetMoveMode(bool enabled, bool savePosition)
        {
            moveMode = enabled;
            dragging = false;
            Capture = false;
            Cursor = enabled ? Cursors.SizeAll : Cursors.Default;
            confirmMoveItem.Visible = enabled;
            ApplyClickThroughStyle();

            if (!enabled && savePosition)
            {
                WidgetSettings.X = Location.X;
                WidgetSettings.Y = Location.Y;
                WidgetSettings.Position = "Custom";
                Action changed = PositionChangedByUser;
                if (changed != null)
                    changed();
            }
            Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyClickThroughStyle();
        }

        private void ApplyClickThroughStyle()
        {
            if (!IsHandleCreated)
                return;
            int style = GetWindowLong(Handle, GwlExStyle);
            int updated = moveMode ? style & ~WsExTransparent : style | WsExTransparent;
            if (updated != style)
            {
                SetWindowLong(Handle, GwlExStyle, updated);
                SetWindowPos(Handle, HwndTopmost, 0, 0, 0, 0,
                    SwpNoMove | SwpNoSize | SwpNoActivate | SwpFrameChanged);
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            KeepAboveGames();
        }

        private void KeepAboveGames()
        {
            if (!Visible || IsDisposed || !IsHandleCreated)
                return;
            SetWindowPos(Handle, HwndTopmost, 0, 0, 0, 0,
                SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
        }

        private Point NamedLocation(string position)
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            const int margin = 18;
            bool right = position.EndsWith("Right", StringComparison.OrdinalIgnoreCase);
            bool bottom = position.StartsWith("Bottom", StringComparison.OrdinalIgnoreCase);
            int x = right ? area.Right - Width - margin : area.Left + margin;
            int y = bottom ? area.Bottom - Height - margin : area.Top + margin;
            return new Point(x, y);
        }

        private Point EnsureVisible(Point requested)
        {
            Rectangle candidate = new Rectangle(requested, Size);
            foreach (Screen screen in Screen.AllScreens)
            {
                Rectangle visible = Rectangle.Intersect(candidate, screen.WorkingArea);
                if (visible.Width >= Math.Min(32, Width) && visible.Height >= Math.Min(24, Height))
                    return requested;
            }
            return NamedLocation("TopRight");
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            UpdateRoundedRegion();
        }

        private void UpdateRoundedRegion()
        {
            Region old = Region;
            Region = null;
            if (old != null)
                old.Dispose();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            Color accent;
            if (!percent.HasValue)
                accent = Color.FromArgb(115, 125, 135);
            else if (sleeping)
                accent = Color.FromArgb(95, 125, 145);
            else if (charging)
                accent = Color.FromArgb(45, 160, 245);
            else if (percent.Value <= 10)
                accent = Color.FromArgb(240, 55, 55);
            else if (percent.Value <= 20)
                accent = Color.FromArgb(245, 155, 30);
            else
                accent = Color.FromArgb(35, 205, 95);

            string main = percent.HasValue ? percent.Value + "%" : "—";
            float mainSize = Math.Max(15f, Height * 0.72f);
            RectangleF mainArea = new RectangleF(0, 0, Width, Height);

            using (var mainFont = new Font("Segoe UI", mainSize, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var shadowBrush = new SolidBrush(Color.FromArgb(230, 0, 0, 0)))
            using (var mainBrush = new SolidBrush(accent))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                RectangleF shadowArea = new RectangleF(1, 1, Width, Height);
                g.DrawString(main, mainFont, shadowBrush, shadowArea, format);
                g.DrawString(main, mainFont, mainBrush, mainArea, format);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!moveMode || e.Button != MouseButtons.Left)
                return;
            dragging = true;
            dragOffset = e.Location;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging)
                return;
            Point cursor = Cursor.Position;
            Location = new Point(cursor.X - dragOffset.X, cursor.Y - dragOffset.Y);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!dragging || e.Button != MouseButtons.Left)
                return;
            dragging = false;
            Capture = false;
        }

        protected override void WndProc(ref Message message)
        {
            const int WmNcHitTest = 0x0084;
            const int HtTransparent = -1;
            if (message.Msg == WmNcHitTest && !moveMode)
            {
                message.Result = new IntPtr(HtTransparent);
                return;
            }
            base.WndProc(ref message);
        }

        private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
        {
            int diameter = Math.Max(2, radius * 2);
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                keepAboveGamesTimer.Stop();
                keepAboveGamesTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter,
            int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr window, int index);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr window, int index, int newValue);
    }

    internal static class TrayIconFactory
    {
        private static readonly Dictionary<char, string[]> Digits = new Dictionary<char, string[]>
        {
            { '0', new[] { "111", "101", "101", "101", "111" } },
            { '1', new[] { "010", "110", "010", "010", "111" } },
            { '2', new[] { "111", "001", "111", "100", "111" } },
            { '3', new[] { "111", "001", "111", "001", "111" } },
            { '4', new[] { "101", "101", "111", "001", "001" } },
            { '5', new[] { "111", "100", "111", "001", "111" } },
            { '6', new[] { "111", "100", "111", "101", "111" } },
            { '7', new[] { "111", "001", "010", "010", "010" } },
            { '8', new[] { "111", "101", "111", "101", "111" } },
            { '9', new[] { "111", "101", "111", "001", "111" } }
        };

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        public static Icon Create(int? percent, bool charging, bool sleeping)
        {
            // Draw directly at 16x16: Windows no longer has to shrink anti-aliased
            // text, so all digits remain pixel-sharp in the notification area.
            using (var bitmap = new Bitmap(16, 16, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.None;
                g.PixelOffsetMode = PixelOffsetMode.None;
                g.Clear(Color.Transparent);

                Color fill;
                if (!percent.HasValue)
                    fill = Color.FromArgb(105, 105, 105);
                else if (sleeping)
                    fill = Color.FromArgb(90, 110, 125);
                else if (charging)
                    fill = Color.FromArgb(35, 145, 235);
                else if (percent.Value <= 10)
                    fill = Color.FromArgb(220, 45, 45);
                else if (percent.Value <= 20)
                    fill = Color.FromArgb(235, 145, 25);
                else
                    fill = Color.FromArgb(25, 175, 75);

                using (var outline = new SolidBrush(Color.FromArgb(245, 240, 240, 240)))
                using (var brush = new SolidBrush(fill))
                {
                    g.FillRectangle(outline, 0, 1, 16, 14);
                    g.FillRectangle(brush, 1, 2, 14, 12);
                }

                if (percent.HasValue)
                {
                    DrawPixelNumber(g, percent.Value.ToString());
                }
                else
                {
                    using (var dash = new SolidBrush(Color.White))
                        g.FillRectangle(dash, 4, 7, 8, 2);
                }

                IntPtr hIcon = bitmap.GetHicon();
                try
                {
                    using (Icon temporary = Icon.FromHandle(hIcon))
                        return (Icon)temporary.Clone();
                }
                finally
                {
                    DestroyIcon(hIcon);
                }
            }
        }

        private static void DrawPixelNumber(Graphics g, string label)
        {
            int scaleX = label.Length >= 3 ? 1 : label.Length == 2 ? 2 : 3;
            int scaleY = 2;
            int gap = label.Length >= 3 ? 1 : 2;
            int digitWidth = 3 * scaleX;
            int totalWidth = label.Length * digitWidth + (label.Length - 1) * gap;
            int x = (16 - totalWidth) / 2;
            int y = 3;

            using (var shadow = new SolidBrush(Color.FromArgb(135, 0, 0, 0)))
            using (var white = new SolidBrush(Color.White))
            {
                foreach (char character in label)
                {
                    string[] rows = Digits[character];
                    for (int row = 0; row < rows.Length; row++)
                    {
                        for (int column = 0; column < 3; column++)
                        {
                            if (rows[row][column] != '1')
                                continue;
                            Rectangle pixel = new Rectangle(x + column * scaleX, y + row * scaleY,
                                scaleX, scaleY);
                            if (scaleX > 1)
                                g.FillRectangle(shadow, pixel.X + 1, pixel.Y + 1, pixel.Width, pixel.Height);
                            g.FillRectangle(white, pixel);
                        }
                    }
                    x += digitWidth + gap;
                }
            }
        }
    }

    internal static class ArdorHid
    {
        public static bool Verbose;
        private const ushort InputUsagePage = 0xFF01;
        private const ushort FeatureUsagePage = 0xFF02;
        private const ushort NordicUsage = 0x0002;
        private const uint DigcfPresent = 0x00000002;
        private const uint DigcfDeviceInterface = 0x00000010;
        private const uint GenericRead = 0x80000000;
        private const uint GenericWrite = 0x40000000;
        private const uint FileShareRead = 0x00000001;
        private const uint FileShareWrite = 0x00000002;
        private const uint OpenExisting = 3;
        private const uint FileFlagOverlapped = 0x40000000;
        private const int ErrorNoMoreItems = 259;
        private const int ErrorIoPending = 997;
        private const uint WaitObject0 = 0;
        private const uint WaitTimeout = 258;
        private static readonly IntPtr InvalidHandleValue = new IntPtr(-1);

        private enum ProtocolKind
        {
            SplitFeature,
            NordicDirect
        }

        private sealed class DeviceProfile
        {
            public string Name;
            public ushort VendorId;
            public ushort WiredProductId;
            public ushort WirelessProductId;
            public ProtocolKind Protocol;
        }

        // These IDs come from the official ARDOR configuration packages. Several
        // older models deliberately share one generic CompX USB identity, so they
        // cannot be distinguished reliably without changing device settings.
        private static readonly DeviceProfile[] Profiles =
        {
            new DeviceProfile
            {
                Name = "ARDOR CompX",
                VendorId = 0x25A7,
                WiredProductId = 0xFA7B,
                WirelessProductId = 0xFA7C,
                Protocol = ProtocolKind.SplitFeature
            },
            new DeviceProfile
            {
                Name = "ARDOR Nordic/CompX",
                VendorId = 0x3554,
                WiredProductId = 0xF511,
                WirelessProductId = 0xF53C,
                Protocol = ProtocolKind.NordicDirect
            },
            new DeviceProfile
            {
                Name = "ARDOR Nordic/CompX",
                VendorId = 0x3554,
                WiredProductId = 0xF59A,
                WirelessProductId = 0xF53C,
                Protocol = ProtocolKind.NordicDirect
            },
            new DeviceProfile
            {
                Name = "ARDOR Nordic/CompX",
                VendorId = 0x3554,
                WiredProductId = 0xF52E,
                WirelessProductId = 0xF52D,
                Protocol = ProtocolKind.NordicDirect
            }
        };

        public static BatteryReading Read()
        {
            List<HidEndpoint> endpoints = EnumerateEndpoints();

            foreach (DeviceProfile profile in Profiles)
            {
                // A live cable should win over a receiver that may still expose a
                // cached value. Without the cable, fall back to the 2.4 GHz dongle.
                BatteryReading wired = Query(endpoints, profile, profile.WiredProductId, true);
                if (wired != null)
                    return wired;
                BatteryReading wireless = Query(endpoints, profile, profile.WirelessProductId, false);
                if (wireless != null)
                    return wireless;
            }
            return null;
        }

        private static BatteryReading Query(List<HidEndpoint> endpoints, DeviceProfile profile,
            ushort productId, bool wired)
        {
            return profile.Protocol == ProtocolKind.NordicDirect
                ? QueryNordicDirect(endpoints, profile, productId, wired)
                : QuerySplitFeature(endpoints, profile, productId, wired);
        }

        private static BatteryReading QuerySplitFeature(List<HidEndpoint> endpoints,
            DeviceProfile profile, ushort productId, bool wired)
        {
            HidEndpoint input = null;
            HidEndpoint feature = null;
            foreach (HidEndpoint endpoint in endpoints)
            {
                if (endpoint.VendorId != profile.VendorId || endpoint.ProductId != productId)
                    continue;
                if (endpoint.UsagePage == InputUsagePage && endpoint.InputLength >= 17)
                    input = endpoint;
                if (endpoint.UsagePage == FeatureUsagePage && endpoint.FeatureLength >= 17)
                    feature = endpoint;
            }
            Log("PID " + productId.ToString("X4") + ": input=" + (input != null) + ", feature=" + (feature != null));
            if (input == null || feature == null)
                return null;

            using (SafeFileHandle inputHandle = Open(input.Path, GenericRead | GenericWrite, FileFlagOverlapped))
            using (SafeFileHandle featureHandle = Open(feature.Path, GenericRead | GenericWrite, 0))
            {
                Log("Open input invalid=" + (inputHandle == null || inputHandle.IsInvalid) +
                    ", feature invalid=" + (featureHandle == null || featureHandle.IsInvalid) +
                    ", win32=" + Marshal.GetLastWin32Error());
                if (inputHandle == null || inputHandle.IsInvalid || featureHandle == null || featureHandle.IsInvalid)
                    return null;

                byte[] packet = new byte[17];
                packet[0] = 0x08; // Feature report ID used by the CompX controller.
                packet[1] = 0x04; // Read battery status.
                int sum = 0;
                for (int i = 0; i < packet.Length - 1; i++)
                    sum += packet[i];
                packet[16] = (byte)((0x55 - sum) & 0xFF);

                if (!HidD_SetFeature(featureHandle, packet, packet.Length))
                {
                    Log("HidD_SetFeature failed: " + Marshal.GetLastWin32Error());
                    return null;
                }
                Log("TX: " + BitConverter.ToString(packet));

                DateTime deadline = DateTime.UtcNow.AddMilliseconds(800);
                while (DateTime.UtcNow < deadline)
                {
                    int remaining = Math.Max(20, (int)(deadline - DateTime.UtcNow).TotalMilliseconds);
                    byte[] response = ReadReport(inputHandle, 17, remaining);
                    if (response == null)
                    {
                        Log("Read timeout/error: " + Marshal.GetLastWin32Error());
                        break;
                    }
                    Log("RX: " + BitConverter.ToString(response));
                    if (response.Length < 8 || response[0] != 0x09 || response[1] != 0x04)
                        continue;
                    if (!ChecksumIsValid(response))
                        continue;

                    int percent = response[6];
                    if (percent < 0 || percent > 100)
                        continue;

                    return new BatteryReading
                    {
                        DeviceName = profile.Name,
                        Percent = percent,
                        Charging = response[7] != 0,
                        Wired = wired,
                        Mode = wired ? "USB" : "2.4G"
                    };
                }
            }
            return null;
        }

        private static BatteryReading QueryNordicDirect(List<HidEndpoint> endpoints,
            DeviceProfile profile, ushort productId, bool wired)
        {
            foreach (HidEndpoint endpoint in endpoints)
            {
                if (endpoint.VendorId != profile.VendorId || endpoint.ProductId != productId ||
                    endpoint.UsagePage != FeatureUsagePage || endpoint.Usage != NordicUsage ||
                    endpoint.InputLength < 17 || endpoint.OutputLength < 17)
                    continue;

                using (SafeFileHandle handle = Open(endpoint.Path,
                    GenericRead | GenericWrite, FileFlagOverlapped))
                {
                    if (handle == null || handle.IsInvalid)
                        continue;

                    byte[] packet = CreateBatteryPacket();
                    if (!WriteReport(handle, packet, 500))
                    {
                        Log("WriteFile failed for " + profile.Name + ": " + Marshal.GetLastWin32Error());
                        continue;
                    }
                    Log("TX " + profile.Name + ": " + BitConverter.ToString(packet));

                    DateTime deadline = DateTime.UtcNow.AddMilliseconds(800);
                    while (DateTime.UtcNow < deadline)
                    {
                        byte[] response = ReadReport(handle, endpoint.InputLength,
                            Math.Max(20, (int)(deadline - DateTime.UtcNow).TotalMilliseconds));
                        if (response == null)
                            break;
                        Log("RX " + profile.Name + ": " + BitConverter.ToString(response));
                        if (response.Length < 17 || (response[0] != 0x08 && response[0] != 0x09) ||
                            response[1] != 0x04 || !ChecksumIsValid(response))
                            continue;

                        int percent = response[6];
                        if (percent > 100)
                            continue;
                        bool cableState = response[7] != 0;
                        return new BatteryReading
                        {
                            DeviceName = profile.Name,
                            Percent = percent,
                            Charging = cableState && percent < 100,
                            Wired = wired || cableState,
                            Mode = wired || cableState ? "USB" : "2.4G"
                        };
                    }
                }
            }
            return null;
        }

        private static byte[] CreateBatteryPacket()
        {
            byte[] packet = new byte[17];
            packet[0] = 0x08;
            packet[1] = 0x04;
            int sum = 0;
            for (int i = 0; i < packet.Length - 1; i++)
                sum += packet[i];
            packet[16] = (byte)((0x55 - sum) & 0xFF);
            return packet;
        }

        private static bool ChecksumIsValid(byte[] report)
        {
            int sum = 0;
            for (int i = 0; i < report.Length; i++)
                sum += report[i];
            return (sum & 0xFF) == 0x55;
        }

        private static bool WriteReport(SafeFileHandle handle, byte[] buffer, int timeoutMs)
        {
            GCHandle pinned = default(GCHandle);
            IntPtr eventHandle = IntPtr.Zero;
            try
            {
                pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                eventHandle = CreateEvent(IntPtr.Zero, true, false, null);
                if (eventHandle == IntPtr.Zero)
                    return false;

                var overlapped = new NativeOverlappedData { EventHandle = eventHandle };
                bool started = WriteFile(handle, pinned.AddrOfPinnedObject(),
                    (uint)buffer.Length, IntPtr.Zero, ref overlapped);
                if (!started && Marshal.GetLastWin32Error() != ErrorIoPending)
                    return false;

                uint wait = WaitForSingleObject(eventHandle, (uint)timeoutMs);
                if (wait == WaitTimeout)
                {
                    CancelIoEx(handle, ref overlapped);
                    WaitForSingleObject(eventHandle, 200);
                    return false;
                }
                if (wait != WaitObject0)
                    return false;

                uint written;
                return GetOverlappedResult(handle, ref overlapped, out written, false) &&
                    written == buffer.Length;
            }
            finally
            {
                if (eventHandle != IntPtr.Zero)
                    CloseHandle(eventHandle);
                if (pinned.IsAllocated)
                    pinned.Free();
            }
        }

        private static byte[] ReadReport(SafeFileHandle handle, int length, int timeoutMs)
        {
            byte[] buffer = new byte[length];
            GCHandle pinned = default(GCHandle);
            IntPtr eventHandle = IntPtr.Zero;
            try
            {
                pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                eventHandle = CreateEvent(IntPtr.Zero, true, false, null);
                if (eventHandle == IntPtr.Zero)
                    return null;

                var overlapped = new NativeOverlappedData { EventHandle = eventHandle };
                bool started = ReadFile(handle, pinned.AddrOfPinnedObject(), (uint)length, IntPtr.Zero, ref overlapped);
                if (!started && Marshal.GetLastWin32Error() != ErrorIoPending)
                    return null;

                uint wait = WaitForSingleObject(eventHandle, (uint)timeoutMs);
                if (wait == WaitTimeout)
                {
                    CancelIoEx(handle, ref overlapped);
                    WaitForSingleObject(eventHandle, 200);
                    return null;
                }
                if (wait != WaitObject0)
                    return null;

                uint read;
                if (!GetOverlappedResult(handle, ref overlapped, out read, false) || read == 0)
                    return null;

                if (read == buffer.Length)
                    return buffer;
                byte[] shortened = new byte[read];
                Buffer.BlockCopy(buffer, 0, shortened, 0, (int)read);
                return shortened;
            }
            finally
            {
                if (eventHandle != IntPtr.Zero)
                    CloseHandle(eventHandle);
                if (pinned.IsAllocated)
                    pinned.Free();
            }
        }

        private static List<HidEndpoint> EnumerateEndpoints()
        {
            var result = new List<HidEndpoint>();
            Guid hidGuid;
            HidD_GetHidGuid(out hidGuid);
            IntPtr infoSet = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero,
                DigcfPresent | DigcfDeviceInterface);
            if (infoSet == InvalidHandleValue)
                return result;

            try
            {
                for (uint index = 0; ; index++)
                {
                    var interfaceData = new DeviceInterfaceData();
                    interfaceData.Size = Marshal.SizeOf(typeof(DeviceInterfaceData));
                    if (!SetupDiEnumDeviceInterfaces(infoSet, IntPtr.Zero, ref hidGuid, index, ref interfaceData))
                    {
                        if (Marshal.GetLastWin32Error() == ErrorNoMoreItems)
                            break;
                        continue;
                    }

                    uint required;
                    SetupDiGetDeviceInterfaceDetail(infoSet, ref interfaceData, IntPtr.Zero, 0,
                        out required, IntPtr.Zero);
                    if (required == 0)
                        continue;

                    IntPtr detail = Marshal.AllocHGlobal((int)required);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetail(infoSet, ref interfaceData, detail, required,
                            out required, IntPtr.Zero))
                            continue;

                        // DevicePath begins immediately after the 4-byte cbSize field.
                        // On x64 cbSize is 8 because of native alignment, but the first
                        // UTF-16 character is still at byte offset 4.
                        IntPtr pathPointer = IntPtr.Add(detail, 4);
                        string path = Marshal.PtrToStringUni(pathPointer);

                        // Older CompX-based ARDOR mice expose the battery response on
                        // Col05 and accept the matching feature command on Col07.
                        string lowerPath = path == null ? "" : path.ToLowerInvariant();
                        ushort pathProductId = lowerPath.Contains("pid_fa7b")
                            ? (ushort)0xFA7B
                            : lowerPath.Contains("pid_fa7c") ? (ushort)0xFA7C : (ushort)0;
                        if (lowerPath.Contains("vid_25a7") && pathProductId != 0 &&
                            (lowerPath.Contains("&col05#") || lowerPath.Contains("&col07#")))
                        {
                            Log("Matched path: " + path);
                            result.Add(new HidEndpoint
                            {
                                Path = path,
                                VendorId = 0x25A7,
                                ProductId = pathProductId,
                                UsagePage = lowerPath.Contains("&col05#") ? InputUsagePage : FeatureUsagePage,
                                InputLength = lowerPath.Contains("&col05#") ? (ushort)17 : (ushort)0,
                                FeatureLength = lowerPath.Contains("&col07#") ? (ushort)17 : (ushort)0
                            });
                            continue;
                        }

                        HidEndpoint endpoint = InspectEndpoint(path);
                        if (endpoint != null && IsSupported(endpoint.VendorId, endpoint.ProductId))
                            result.Add(endpoint);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detail);
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(infoSet);
            }
            Log("Matched endpoints: " + result.Count);
            return result;
        }

        private static bool IsSupported(ushort vendorId, ushort productId)
        {
            foreach (DeviceProfile profile in Profiles)
            {
                if (profile.VendorId == vendorId &&
                    (profile.WiredProductId == productId || profile.WirelessProductId == productId))
                    return true;
            }
            return false;
        }

        private static void Log(string text)
        {
            if (Verbose)
                Console.WriteLine(text);
        }

        private static HidEndpoint InspectEndpoint(string path)
        {
            using (SafeFileHandle handle = Open(path, 0, 0))
            {
                if (handle == null || handle.IsInvalid)
                    return null;

                var attributes = new HidAttributes();
                attributes.Size = Marshal.SizeOf(typeof(HidAttributes));
                if (!HidD_GetAttributes(handle, ref attributes))
                    return null;

                IntPtr preparsed;
                if (!HidD_GetPreparsedData(handle, out preparsed))
                    return null;
                try
                {
                    HidCaps caps;
                    if (HidP_GetCaps(preparsed, out caps) < 0)
                        return null;
                    return new HidEndpoint
                    {
                        Path = path,
                        VendorId = attributes.VendorId,
                        ProductId = attributes.ProductId,
                        UsagePage = caps.UsagePage,
                        Usage = caps.Usage,
                        InputLength = caps.InputReportByteLength,
                        OutputLength = caps.OutputReportByteLength,
                        FeatureLength = caps.FeatureReportByteLength
                    };
                }
                finally
                {
                    HidD_FreePreparsedData(preparsed);
                }
            }
        }

        private static SafeFileHandle Open(string path, uint access, uint flags)
        {
            SafeFileHandle handle = CreateFile(path, access, FileShareRead | FileShareWrite,
                IntPtr.Zero, OpenExisting, flags, IntPtr.Zero);
            return handle;
        }

        private sealed class HidEndpoint
        {
            public string Path;
            public ushort VendorId;
            public ushort ProductId;
            public ushort UsagePage;
            public ushort Usage;
            public ushort InputLength;
            public ushort OutputLength;
            public ushort FeatureLength;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DeviceInterfaceData
        {
            public int Size;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HidAttributes
        {
            public int Size;
            public ushort VendorId;
            public ushort ProductId;
            public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HidCaps
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeOverlappedData
        {
            public IntPtr Internal;
            public IntPtr InternalHigh;
            public uint Offset;
            public uint OffsetHigh;
            public IntPtr EventHandle;
        }

        [DllImport("hid.dll")]
        private static extern void HidD_GetHidGuid(out Guid guid);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool HidD_GetAttributes(SafeFileHandle handle, ref HidAttributes attributes);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

        [DllImport("hid.dll")]
        private static extern int HidP_GetCaps(IntPtr preparsedData, out HidCaps caps);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool HidD_SetFeature(SafeFileHandle handle, byte[] reportBuffer, int reportBufferLength);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator,
            IntPtr parent, uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr infoSet, IntPtr deviceInfoData,
            ref Guid interfaceClassGuid, uint memberIndex, ref DeviceInterfaceData interfaceData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr infoSet,
            ref DeviceInterfaceData interfaceData, IntPtr detailData, uint detailDataSize,
            out uint requiredSize, IntPtr deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr infoSet);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess,
            uint shareMode, IntPtr securityAttributes, uint creationDisposition,
            uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ReadFile(SafeFileHandle file, IntPtr buffer, uint bytesToRead,
            IntPtr bytesRead, ref NativeOverlappedData overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WriteFile(SafeFileHandle file, IntPtr buffer, uint bytesToWrite,
            IntPtr bytesWritten, ref NativeOverlappedData overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetOverlappedResult(SafeFileHandle file,
            ref NativeOverlappedData overlapped, out uint bytesTransferred, bool wait);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CancelIoEx(SafeFileHandle file, ref NativeOverlappedData overlapped);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateEvent(IntPtr eventAttributes, bool manualReset,
            bool initialState, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
