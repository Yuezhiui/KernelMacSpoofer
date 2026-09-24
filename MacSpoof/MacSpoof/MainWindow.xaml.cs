using System;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using Microsoft.Win32;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace MacSpoof
{
    public sealed partial class MainWindow : Window
    {
        private readonly DispatcherTimer _rotateTimer;
        private readonly DispatcherTimer _pollTimer;
        private readonly DispatcherTimer _cooldownTimer;
        private readonly DispatcherTimer _exitTimer;

        private int _cooldownSecondsLeft;
        private bool _isRunning;
        private bool _operationInProgress;
        private bool _refreshingAdapters;
        private bool _isWindowVisible = true;
        private bool _exitPending;
        private bool _allowClose;
        private bool _isClosed;
        private string _lastMacAddress = "Unknown";
        private SpoofMode _selectedMode = SpoofMode.Once;
        private TrayIconManager? _trayManager;
        private const string StartupRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string SettingsKey = @"Software\MacSpoof";
        private const string StartupValueName = "MacSpoof";
        private const string SpoofAtStartupValueName = "SpoofAtStartup";

        private readonly SolidColorBrush _tileBrush = new(Windows.UI.Color.FromArgb(255, 13, 24, 36));
        private readonly SolidColorBrush _selectedTileBrush = new(Windows.UI.Color.FromArgb(255, 16, 47, 86));
        private readonly SolidColorBrush _tileBorderBrush = new(Windows.UI.Color.FromArgb(255, 44, 64, 84));
        private readonly SolidColorBrush _selectedTileBorderBrush = new(Windows.UI.Color.FromArgb(255, 62, 157, 255));
        private readonly SolidColorBrush _stopBrush = new(Windows.UI.Color.FromArgb(255, 224, 95, 95));
        private readonly SolidColorBrush _readyBrush = new(Windows.UI.Color.FromArgb(255, 44, 240, 112));
        private readonly SolidColorBrush _workingBrush = new(Windows.UI.Color.FromArgb(255, 54, 199, 255));
        private readonly SolidColorBrush _mutedBrush = new(Windows.UI.Color.FromArgb(255, 115, 134, 159));

        private enum SpoofMode
        {
            Once,
            Restart,
            Rotate,
            Custom
        }

        public bool IsNetworkOperationRunning => _operationInProgress;
        public string CurrentMacAddress => _lastMacAddress;
        private string SelectedAdapterId => (AdapterComboBox.SelectedItem as NetworkInterface)?.Id ?? string.Empty;
        private bool HasSelectedAdapter => AdapterComboBox.SelectedItem is NetworkInterface;

        public MainWindow()
        {
            InitializeComponent();
            SetFixedSize();

            _rotateTimer = new DispatcherTimer();
            _rotateTimer.Tick += RotateTimer_Tick;

            _cooldownTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _cooldownTimer.Tick += CooldownTimer_Tick;

            _pollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _pollTimer.Tick += PollTimer_Tick;

            _exitTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _exitTimer.Tick += ExitTimer_Tick;

            RefreshAdapters(preserveSelection: false);
            SelectMode(IsSpoofAtStartupEnabled() ? SpoofMode.Restart : SpoofMode.Once, updateStatus: false);
            StartupCheckBox.IsChecked = IsStartupEnabled();

            try
            {
                _trayManager = new TrayIconManager(this);
                LoadCurrentMacAddress();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize tray icon: {ex.Message}");
                StatusTextBlock.Text = "System tray integration is unavailable. The main window remains fully usable.";
            }

            AppWindow.Closing += AppWindow_Closing;
            Closed += MainWindow_Closed;
            _pollTimer.Start();

            DispatcherQueue.TryEnqueue(() => MainScrollViewer.ChangeView(null, 0, null, true));

            if (IsStartupLaunch() && IsSpoofAtStartupEnabled())
            {
                DispatcherQueue.TryEnqueue(async () =>
                {
                    await Task.Delay(800);
                    if (!_isClosed && HasSelectedAdapter)
                        await ExecuteOnceWithCooldownAsync();
                });
            }
        }

        private void SetFixedSize()
        {
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
            }

            appWindow.Resize(new Windows.Graphics.SizeInt32(440, 720));
            appWindow.TitleBar.BackgroundColor = Windows.UI.Color.FromArgb(255, 11, 15, 20);
            appWindow.TitleBar.InactiveBackgroundColor = Windows.UI.Color.FromArgb(255, 11, 15, 20);
            appWindow.TitleBar.ForegroundColor = Windows.UI.Color.FromArgb(255, 238, 242, 247);
            appWindow.TitleBar.InactiveForegroundColor = Windows.UI.Color.FromArgb(255, 126, 137, 151);
        }

        private void RefreshAdapters(bool preserveSelection)
        {
            string previousId = preserveSelection ? SelectedAdapterId : string.Empty;

            try
            {
                NetworkInterface[] adapters = MacSpoofService.GetAdapters();
                NetworkInterface? preferred = adapters.FirstOrDefault(adapter => adapter.Id == previousId)
                    ?? adapters.FirstOrDefault();

                _refreshingAdapters = true;
                try
                {
                    AdapterComboBox.ItemsSource = adapters;
                    AdapterComboBox.SelectedItem = preferred;
                }
                finally
                {
                    _refreshingAdapters = false;
                }

                LoadCurrentMacAddress();
                UpdateAdapterInformation();
                UpdateControlAvailability();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to refresh adapters: {ex.Message}");
                StatusTextBlock.Text = "Could not refresh network adapters: " + ex.Message;
                UpdateControlAvailability();
            }
        }

        private void LoadCurrentMacAddress()
        {
            try
            {
                string formattedMac = HasSelectedAdapter
                    ? MacSpoofService.GetCurrentMacAddress(SelectedAdapterId)
                    : "Unknown";

                _lastMacAddress = formattedMac;
                CurrentMacTextBlock.Text = formattedMac;
                MacStatusTextBlock.Text = formattedMac == "Unknown" ? "UNAVAILABLE" : "ACTIVE";
                CurrentAdapterDescriptionTextBlock.Text = (AdapterComboBox.SelectedItem as NetworkInterface)?.Description ?? "Network adapter";
                SetReadyState(formattedMac == "Unknown" ? "CHECK" : "READY",
                    formattedMac == "Unknown" ? "Adapter unavailable" : "System prepared",
                    formattedMac == "Unknown" ? _mutedBrush : _readyBrush);
                _trayManager?.UpdateTooltip($"MacSpoof: {formattedMac}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to read current MAC: {ex.Message}");
                _lastMacAddress = "Unknown";
                CurrentMacTextBlock.Text = "Unknown";
                MacStatusTextBlock.Text = "UNAVAILABLE";
                SetReadyState("CHECK", "Adapter unavailable", _mutedBrush);
                _trayManager?.UpdateTooltip("MacSpoof: unavailable");
            }
        }

        private void UpdateControlAvailability()
        {
            bool hasAdapter = HasSelectedAdapter;
            bool controlsAvailable = !_operationInProgress && !_exitPending;

            ActionButton.IsEnabled = hasAdapter && controlsAvailable && _cooldownSecondsLeft == 0;
            AdapterComboBox.IsEnabled = controlsAvailable && !_isRunning;
            RefreshAdapterButton.IsEnabled = controlsAvailable && !_isRunning;
            ConfigurationComboBox.IsEnabled = controlsAvailable;
            OnceModeButton.IsEnabled = controlsAvailable && !_isRunning;
            RestartModeButton.IsEnabled = controlsAvailable && !_isRunning;
            RotateModeButton.IsEnabled = controlsAvailable || _isRunning;
            CustomModeButton.IsEnabled = controlsAvailable || _isRunning;
            RestoreButton.IsEnabled = hasAdapter && controlsAvailable;
            ClearCacheButton.IsEnabled = hasAdapter && controlsAvailable;
            ClearCacheCheckBox.IsEnabled = controlsAvailable;
            StartupCheckBox.IsEnabled = controlsAvailable;
        }

        private async Task RandomizeMacAddressAsync()
        {
            await RunNetworkOperationAsync(() => MacSpoofService.ChangeMacAsync(
                SelectedAdapterId, false, ClearCacheCheckBox.IsChecked == true));
        }

        private async Task RunNetworkOperationAsync(Func<Task<NetworkResult>> operation)
        {
            if (_operationInProgress || _exitPending || !HasSelectedAdapter)
                return;

            bool operationSucceeded = false;
            _operationInProgress = true;
            _rotateTimer.Stop();
            UpdateControlAvailability();
            StatusTextBlock.Text = "Working; the adapter may briefly disconnect...";
            SetReadyState("WORKING", "Applying network change", _workingBrush);

            try
            {
                NetworkResult result = await operation();
                operationSucceeded = result.Success;
                StatusTextBlock.Text = result.Message;
                if (!result.Success)
                    StopLoop();
                LoadCurrentMacAddress();
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = ex.Message;
                StopLoop();
            }
            finally
            {
                _operationInProgress = false;
                SetReadyState(
                    !HasSelectedAdapter || !operationSucceeded ? "CHECK" : "READY",
                    !HasSelectedAdapter ? "Select an adapter" :
                    operationSucceeded ? "System prepared" : "Attention required",
                    !HasSelectedAdapter || !operationSucceeded ? _mutedBrush : _readyBrush);
                UpdateControlAvailability();
                if (_isRunning && !_exitPending)
                    _rotateTimer.Start();
            }
        }

        private async void RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            StopLoop();
            await RunNetworkOperationAsync(() => MacSpoofService.ChangeMacAsync(
                SelectedAdapterId, true, ClearCacheCheckBox.IsChecked == true));
        }

        private async void ClearCacheButton_Click(object sender, RoutedEventArgs e)
        {
            StopLoop();
            await RunNetworkOperationAsync(() => MacSpoofService.ClearCachesAsync(SelectedAdapterId));
        }

        private void RefreshAdapterButton_Click(object sender, RoutedEventArgs e)
        {
            if (_operationInProgress || _isRunning || _exitPending)
                return;

            string selectedId = SelectedAdapterId;
            RefreshAdapters(preserveSelection: true);
            StatusTextBlock.Text = HasSelectedAdapter
                ? (SelectedAdapterId == selectedId && !string.IsNullOrEmpty(selectedId)
                    ? "Adapter list refreshed; selection preserved."
                    : "Adapter list refreshed.")
                : "No Ethernet or Wi-Fi adapters are currently available.";
        }

        private void AdapterComboBox_SelectionChanged(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
        {
            if (_refreshingAdapters)
                return;

            LoadCurrentMacAddress();
            UpdateAdapterInformation();
            UpdateControlAvailability();
        }

        private async void ActionButton_Click(object sender, RoutedEventArgs e)
        {
            if (_cooldownSecondsLeft > 0 || _operationInProgress || _exitPending || !HasSelectedAdapter)
                return;

            string? selectedStr = (ConfigurationComboBox.SelectedItem as Microsoft.UI.Xaml.Controls.ComboBoxItem)?.Content.ToString()?.Trim();

            if (_selectedMode is SpoofMode.Once or SpoofMode.Restart)
            {
                _isRunning = false;
                _rotateTimer.Stop();
                await ExecuteOnceWithCooldownAsync();
            }
            else if (!_isRunning)
            {
                await StartLoopAsync(_selectedMode == SpoofMode.Rotate ? "15 Minutes" : selectedStr);
            }
            else
            {
                StopLoop();
            }
        }

        public async Task TriggerSpoofOnceFromTrayAsync()
        {
            if (_cooldownSecondsLeft > 0 || _operationInProgress || _exitPending || !HasSelectedAdapter)
                return;

            await ExecuteOnceWithCooldownAsync();
        }

        private async Task ExecuteOnceWithCooldownAsync()
        {
            _isRunning = false;
            _rotateTimer.Stop();

            ActionButton.IsEnabled = false;
            ActionButtonText.Text = "SPOOFING...";

            await RandomizeMacAddressAsync();

            if (_exitPending || _isClosed)
                return;

            _cooldownSecondsLeft = 5;
            ActionButtonText.Text = $"COOLDOWN ({_cooldownSecondsLeft}s)";
            _cooldownTimer.Start();
            UpdateControlAvailability();
        }

        private void CooldownTimer_Tick(object? sender, object e)
        {
            _cooldownSecondsLeft--;
            if (_cooldownSecondsLeft > 0)
            {
                ActionButtonText.Text = $"COOLDOWN ({_cooldownSecondsLeft}s)";
                return;
            }

            _cooldownTimer.Stop();
            _cooldownSecondsLeft = 0;
            ActionButtonText.Text = "RUN SPOOF";
            RestorePrimaryActionBrush();
            UpdateControlAvailability();
        }

        private async Task StartLoopAsync(string? durationStr)
        {
            _isRunning = true;
            ActionButtonText.Text = "STOP ROTATION";
            ActionButton.Background = _stopBrush;
            _rotateTimer.Interval = ParseDuration(durationStr);
            UpdateControlAvailability();

            await RandomizeMacAddressAsync();
        }

        private void StopLoop()
        {
            _isRunning = false;
            _rotateTimer.Stop();
            ActionButtonText.Text = "RUN SPOOF";
            RestorePrimaryActionBrush();
            UpdateControlAvailability();
        }

        private async void RotateTimer_Tick(object? sender, object e)
        {
            await RandomizeMacAddressAsync();
        }

        private void PollTimer_Tick(object? sender, object e)
        {
            if (_isWindowVisible && !_exitPending)
                LoadCurrentMacAddress();
        }

        private void ConfigurationComboBox_SelectionChanged(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
        {
            string? selectedStr = (ConfigurationComboBox.SelectedItem as Microsoft.UI.Xaml.Controls.ComboBoxItem)?.Content.ToString()?.Trim();

            if (_isRunning && _selectedMode == SpoofMode.Custom)
                _rotateTimer.Interval = ParseDuration(selectedStr);
        }

        private void OnceModeButton_Click(object sender, RoutedEventArgs e) => SelectMode(SpoofMode.Once);

        private void RestartModeButton_Click(object sender, RoutedEventArgs e) => SelectMode(SpoofMode.Restart);

        private void RotateModeButton_Click(object sender, RoutedEventArgs e) => SelectMode(SpoofMode.Rotate);

        private void CustomModeButton_Click(object sender, RoutedEventArgs e) => SelectMode(SpoofMode.Custom);

        private void SelectMode(SpoofMode mode, bool updateStatus = true)
        {
            if (_isRunning && mode != _selectedMode)
                StopLoop();

            _selectedMode = mode;

            StyleModeButton(OnceModeButton, mode == SpoofMode.Once);
            StyleModeButton(RestartModeButton, mode == SpoofMode.Restart);
            StyleModeButton(RotateModeButton, mode == SpoofMode.Rotate);
            StyleModeButton(CustomModeButton, mode == SpoofMode.Custom);

            CustomIntervalPanel.Visibility = mode == SpoofMode.Custom ? Visibility.Visible : Visibility.Collapsed;

            if (mode == SpoofMode.Restart)
            {
                SetSpoofAtStartupEnabled(true);
                if (StartupCheckBox.IsChecked != true)
                {
                    StartupCheckBox.IsChecked = true;
                    SetStartupEnabled(true);
                }
            }
            else
            {
                SetSpoofAtStartupEnabled(false);
            }

            if (!updateStatus)
                return;

            StatusTextBlock.Text = mode switch
            {
                SpoofMode.Once => "Once mode selected. RUN SPOOF changes the selected adapter one time.",
                SpoofMode.Restart => "Every Restart enabled. MacSpoof will launch with Windows and apply one spoof on startup.",
                SpoofMode.Rotate => "Rotate mode selected. MAC changes every 15 minutes until stopped.",
                SpoofMode.Custom => "Custom rotation selected. Choose an interval, then run the rotation.",
                _ => "Ready."
            };
        }

        private void StyleModeButton(Microsoft.UI.Xaml.Controls.Button button, bool selected)
        {
            button.Background = selected ? _selectedTileBrush : _tileBrush;
            button.BorderBrush = selected ? _selectedTileBorderBrush : _tileBorderBrush;
            button.BorderThickness = selected ? new Thickness(1.5) : new Thickness(1);
        }

        private void RestorePrimaryActionBrush()
        {
            if (Content is FrameworkElement root && root.Resources["PrimaryActionBrush"] is Brush brush)
                ActionButton.Background = brush;
        }

        private void SetReadyState(string title, string subtitle, Brush dotBrush)
        {
            ReadyStatusTextBlock.Text = title;
            ReadySubTextBlock.Text = subtitle;
            ReadyDotEllipse.Fill = dotBrush;
        }

        private void UpdateAdapterInformation()
        {
            if (AdapterComboBox.SelectedItem is not NetworkInterface nic)
            {
                AdapterNameTextBlock.Text = "—";
                AdapterDescriptionTextBlock.Text = "—";
                AdapterStatusTextBlock.Text = "Unavailable";
                AdapterStatusDot.Fill = _mutedBrush;
                AdapterSpeedTextBlock.Text = "—";
                AdapterDriverTextBlock.Text = "Windows managed";
                CurrentAdapterDescriptionTextBlock.Text = "Network adapter";
                return;
            }

            AdapterNameTextBlock.Text = nic.Name;
            AdapterDescriptionTextBlock.Text = nic.Description;
            CurrentAdapterDescriptionTextBlock.Text = nic.Description;

            bool connected = nic.OperationalStatus == OperationalStatus.Up;
            AdapterStatusTextBlock.Text = connected ? "Connected" : nic.OperationalStatus.ToString();
            AdapterStatusDot.Fill = connected ? _readyBrush : _mutedBrush;
            AdapterSpeedTextBlock.Text = FormatLinkSpeed(nic.Speed);
            AdapterDriverTextBlock.Text = MacSpoofService.GetAdapterDriverVersion(nic.Id);
        }

        private static string FormatLinkSpeed(long bitsPerSecond)
        {
            if (bitsPerSecond <= 0) return "—";
            if (bitsPerSecond >= 1_000_000_000)
                return $"{bitsPerSecond / 1_000_000_000d:0.#} Gbps";
            if (bitsPerSecond >= 1_000_000)
                return $"{bitsPerSecond / 1_000_000d:0.#} Mbps";
            if (bitsPerSecond >= 1_000)
                return $"{bitsPerSecond / 1_000d:0.#} Kbps";
            return $"{bitsPerSecond} bps";
        }

        private void NetworkNavButton_Click(object sender, RoutedEventArgs e)
        {
            StatusTextBlock.Text = "Network controls are active.";
        }

        private void SettingsNavButton_Click(object sender, RoutedEventArgs e)
        {
            StatusTextBlock.Text = "Settings are available directly in the configuration and startup controls on this screen.";
        }

        private void AboutNavButton_Click(object sender, RoutedEventArgs e)
        {
            StatusTextBlock.Text = "MacSpoof v1.3.0 · Native WinUI 3 network identity utility.";
        }

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://github.com/Yuezhiui/KernelMacSpoofer")
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Could not open help: " + ex.Message;
            }
        }

        private void StartupCheckBox_Click(object sender, RoutedEventArgs e)
        {
            bool enabled = StartupCheckBox.IsChecked == true;
            try
            {
                SetStartupEnabled(enabled);
                if (!enabled && _selectedMode == SpoofMode.Restart)
                {
                    SetSpoofAtStartupEnabled(false);
                    SelectMode(SpoofMode.Once, updateStatus: false);
                }

                StatusTextBlock.Text = enabled
                    ? "MacSpoof will open automatically when you sign in to Windows."
                    : "Automatic startup disabled.";
            }
            catch (Exception ex)
            {
                StartupCheckBox.IsChecked = IsStartupEnabled();
                StatusTextBlock.Text = "Could not update startup setting: " + ex.Message;
            }
        }

        private static bool IsStartupEnabled()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(StartupRunKey);
                return key?.GetValue(StartupValueName) is string value && !string.IsNullOrWhiteSpace(value);
            }
            catch
            {
                return false;
            }
        }

        private static void SetStartupEnabled(bool enabled)
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(StartupRunKey);
            if (!enabled)
            {
                key.DeleteValue(StartupValueName, false);
                return;
            }

            string executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("MacSpoof executable path is unavailable.");
            key.SetValue(StartupValueName, $"\"{executable}\" --startup", RegistryValueKind.String);
        }

        private static bool IsSpoofAtStartupEnabled()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(SettingsKey);
                return Convert.ToInt32(key?.GetValue(SpoofAtStartupValueName, 0)) == 1;
            }
            catch
            {
                return false;
            }
        }

        private static void SetSpoofAtStartupEnabled(bool enabled)
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(SettingsKey);
            key.SetValue(SpoofAtStartupValueName, enabled ? 1 : 0, RegistryValueKind.DWord);
        }

        private static bool IsStartupLaunch() =>
            Environment.GetCommandLineArgs().Any(arg => arg.Equals("--startup", StringComparison.OrdinalIgnoreCase));

        internal void OnWindowHidden()
        {
            if (_isClosed)
                return;

            _isWindowVisible = false;
            _pollTimer.Stop();
        }

        internal void OnWindowShown()
        {
            if (_isClosed)
                return;

            _isWindowVisible = true;
            if (_exitPending)
                return;

            if (!_operationInProgress)
                RefreshAdapters(preserveSelection: true);

            MainScrollViewer.ChangeView(null, 0, null, true);
            _pollTimer.Start();
        }

        internal void RequestExit()
        {
            if (_isClosed)
                return;

            if (_operationInProgress)
            {
                BeginPendingExit();
                return;
            }

            _allowClose = true;
            Close();
        }

        private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            if (_allowClose)
                return;

            if (_operationInProgress)
            {
                args.Cancel = true;
                BeginPendingExit();
                return;
            }

            _allowClose = true;
        }

        private void BeginPendingExit()
        {
            if (_exitPending)
                return;

            _exitPending = true;
            StopLoop();
            _pollTimer.Stop();
            _cooldownTimer.Stop();
            _cooldownSecondsLeft = 0;
            ActionButtonText.Text = "FINISHING...";
            StatusTextBlock.Text = "Finishing the current network operation, then MacSpoof will exit automatically.";
            _trayManager?.UpdateTooltip("MacSpoof: finishing operation before exit");
            UpdateControlAvailability();
            _exitTimer.Start();
        }

        private void ExitTimer_Tick(object? sender, object e)
        {
            if (_operationInProgress)
                return;

            _exitTimer.Stop();
            _allowClose = true;
            Close();
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            _isClosed = true;
            _rotateTimer.Stop();
            _pollTimer.Stop();
            _cooldownTimer.Stop();
            _exitTimer.Stop();

            _rotateTimer.Tick -= RotateTimer_Tick;
            _pollTimer.Tick -= PollTimer_Tick;
            _cooldownTimer.Tick -= CooldownTimer_Tick;
            _exitTimer.Tick -= ExitTimer_Tick;
            AppWindow.Closing -= AppWindow_Closing;
            Closed -= MainWindow_Closed;

            _trayManager?.Dispose();
            _trayManager = null;
        }

        private static TimeSpan ParseDuration(string? durationStr)
        {
            if (durationStr == null)
                return TimeSpan.FromMinutes(15);

            string[] parts = durationStr.Split(' ');
            if (parts.Length == 2 && int.TryParse(parts[0], out int value))
            {
                if (parts[1].Contains("second", StringComparison.OrdinalIgnoreCase))
                    return TimeSpan.FromSeconds(value);
                if (parts[1].Contains("minute", StringComparison.OrdinalIgnoreCase))
                    return TimeSpan.FromMinutes(value);
                if (parts[1].Contains("hour", StringComparison.OrdinalIgnoreCase))
                    return TimeSpan.FromHours(value);
            }

            return TimeSpan.FromMinutes(15);
        }
    }
}
