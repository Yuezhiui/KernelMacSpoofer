using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

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

        private readonly SolidColorBrush _paperBrush =
            new(Windows.UI.Color.FromArgb(255, 253, 252, 248));
        private readonly SolidColorBrush _selectedPaperBrush =
            new(Windows.UI.Color.FromArgb(255, 244, 243, 239));
        private readonly SolidColorBrush _inkBrush =
            new(Windows.UI.Color.FromArgb(255, 17, 17, 17));
        private readonly SolidColorBrush _mutedBrush =
            new(Windows.UI.Color.FromArgb(255, 154, 154, 150));

        private enum SpoofMode
        {
            Once,
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
            SelectMode(SpoofMode.Once, updateStatus: false);

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

            var light = Windows.UI.Color.FromArgb(255, 249, 248, 244);
            var ink = Windows.UI.Color.FromArgb(255, 17, 17, 17);
            var muted = Windows.UI.Color.FromArgb(255, 110, 110, 108);
            var hover = Windows.UI.Color.FromArgb(255, 237, 236, 232);

            appWindow.TitleBar.BackgroundColor = light;
            appWindow.TitleBar.InactiveBackgroundColor = light;
            appWindow.TitleBar.ForegroundColor = ink;
            appWindow.TitleBar.InactiveForegroundColor = muted;
            appWindow.TitleBar.ButtonBackgroundColor = light;
            appWindow.TitleBar.ButtonInactiveBackgroundColor = light;
            appWindow.TitleBar.ButtonForegroundColor = ink;
            appWindow.TitleBar.ButtonInactiveForegroundColor = muted;
            appWindow.TitleBar.ButtonHoverBackgroundColor = hover;
            appWindow.TitleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(255, 220, 219, 215);
            appWindow.TitleBar.ButtonHoverForegroundColor = ink;
            appWindow.TitleBar.ButtonPressedForegroundColor = ink;
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
                _trayManager?.UpdateTooltip($"MacSpoof: {formattedMac}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to read current MAC: {ex.Message}");
                _lastMacAddress = "Unknown";
                CurrentMacTextBlock.Text = "Unknown";
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
            CustomModeButton.IsEnabled = controlsAvailable || _isRunning;
            RestoreButton.IsEnabled = hasAdapter && controlsAvailable;
            ClearCacheButton.IsEnabled = hasAdapter && controlsAvailable;
            ClearCacheCheckBox.IsEnabled = controlsAvailable;
            CopyMacButton.IsEnabled = _lastMacAddress != "Unknown";
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

            _operationInProgress = true;
            _rotateTimer.Stop();
            UpdateControlAvailability();
            StatusTextBlock.Text = "Working; the adapter may briefly disconnect...";

            try
            {
                NetworkResult result = await operation();
                StatusTextBlock.Text = result.Message;
                if (!result.Success)
                    StopLoop();
                LoadCurrentMacAddress();
                UpdateAdapterInformation();
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = ex.Message;
                StopLoop();
            }
            finally
            {
                _operationInProgress = false;
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

        private void CopyMacButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_lastMacAddress) || _lastMacAddress == "Unknown")
                return;

            try
            {
                var data = new DataPackage();
                data.SetText(_lastMacAddress);
                Clipboard.SetContent(data);
                Clipboard.Flush();
                StatusTextBlock.Text = "MAC address copied to clipboard.";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Could not copy MAC address: " + ex.Message;
            }
        }

        private async void ActionButton_Click(object sender, RoutedEventArgs e)
        {
            if (_cooldownSecondsLeft > 0 || _operationInProgress || _exitPending || !HasSelectedAdapter)
                return;

            if (_selectedMode == SpoofMode.Once)
            {
                _isRunning = false;
                _rotateTimer.Stop();
                await ExecuteOnceWithCooldownAsync();
                return;
            }

            if (!_isRunning)
            {
                string? selectedStr =
                    (ConfigurationComboBox.SelectedItem as Microsoft.UI.Xaml.Controls.ComboBoxItem)
                    ?.Content.ToString()?.Trim();
                await StartLoopAsync(selectedStr);
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
            ActionButtonText.Text = "STOP CUSTOM";
            ActionButton.Background = _selectedPaperBrush;
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
            {
                LoadCurrentMacAddress();
                UpdateAdapterInformation();
            }
        }

        private void ConfigurationComboBox_SelectionChanged(
            object sender,
            Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
        {
            string? selectedStr =
                (ConfigurationComboBox.SelectedItem as Microsoft.UI.Xaml.Controls.ComboBoxItem)
                ?.Content.ToString()?.Trim();

            if (_isRunning && _selectedMode == SpoofMode.Custom)
                _rotateTimer.Interval = ParseDuration(selectedStr);
        }

        private void OnceModeButton_Click(object sender, RoutedEventArgs e) => SelectMode(SpoofMode.Once);

        private void CustomModeButton_Click(object sender, RoutedEventArgs e) => SelectMode(SpoofMode.Custom);

        private void SelectMode(SpoofMode mode, bool updateStatus = true)
        {
            if (_isRunning && mode != _selectedMode)
                StopLoop();

            _selectedMode = mode;

            bool onceSelected = mode == SpoofMode.Once;
            OnceModeButton.Background = onceSelected ? _selectedPaperBrush : _paperBrush;
            CustomModeButton.Background = onceSelected ? _paperBrush : _selectedPaperBrush;
            OnceModeButton.BorderThickness = onceSelected ? new Thickness(1.9) : new Thickness(1.5);
            CustomModeButton.BorderThickness = onceSelected ? new Thickness(1.5) : new Thickness(1.9);
            var transparentBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
            OnceRadioDot.Fill = onceSelected ? _inkBrush : transparentBrush;
            CustomRadioDot.Fill = onceSelected ? transparentBrush : _inkBrush;
            if (!updateStatus)
                return;

            StatusTextBlock.Text = onceSelected
                ? "Once mode selected. RUN SPOOF changes the selected adapter one time."
                : "Custom mode selected. Choose an interval, then RUN SPOOF to start rotation.";
        }

        private void RestorePrimaryActionBrush()
        {
            if (Content is FrameworkElement root && root.Resources["SoftPaperBrush"] is Brush brush)
                ActionButton.Background = brush;
        }

        private void UpdateAdapterInformation()
        {
            if (AdapterComboBox.SelectedItem is not NetworkInterface nic)
            {
                AdapterNameTextBlock.Text = "—";
                AdapterStatusTextBlock.Text = "Unavailable";
                AdapterStatusDot.Fill = _mutedBrush;
                AdapterSpeedTextBlock.Text = "—";
                AdapterDriverTextBlock.Text = "Windows managed";
                return;
            }

            AdapterNameTextBlock.Text = nic.Name;
            bool connected = nic.OperationalStatus == OperationalStatus.Up;
            AdapterStatusTextBlock.Text = connected ? "Connected" : nic.OperationalStatus.ToString();
            AdapterStatusDot.Fill = connected ? _inkBrush : _mutedBrush;
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
            StatusTextBlock.Text =
                "Finishing the current network operation, then MacSpoof will exit automatically.";
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
