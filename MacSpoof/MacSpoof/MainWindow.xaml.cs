using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
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
        private TrayIconManager? _trayManager;

        private readonly SolidColorBrush _runBrush = new(Windows.UI.Color.FromArgb(255, 109, 140, 255));
        private readonly SolidColorBrush _stopBrush = new(Windows.UI.Color.FromArgb(255, 224, 95, 95));

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
                _trayManager?.UpdateTooltip($"MacSpoof: {formattedMac}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to read current MAC: {ex.Message}");
                _lastMacAddress = "Unknown";
                CurrentMacTextBlock.Text = "Unknown";
                MacStatusTextBlock.Text = "UNAVAILABLE";
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
            RestoreButton.IsEnabled = hasAdapter && controlsAvailable;
            ClearCacheButton.IsEnabled = hasAdapter && controlsAvailable;
            ClearCacheCheckBox.IsEnabled = controlsAvailable;
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
            UpdateControlAvailability();
        }

        private async void ActionButton_Click(object sender, RoutedEventArgs e)
        {
            if (_cooldownSecondsLeft > 0 || _operationInProgress || _exitPending || !HasSelectedAdapter)
                return;

            int selectedIndex = ConfigurationComboBox.SelectedIndex;
            string? selectedStr = (ConfigurationComboBox.SelectedItem as Microsoft.UI.Xaml.Controls.ComboBoxItem)?.Content.ToString()?.Trim();
            bool isOnce = selectedIndex == 0 || string.Equals(selectedStr, "Once", StringComparison.OrdinalIgnoreCase);

            if (isOnce)
            {
                _isRunning = false;
                _rotateTimer.Stop();
                await ExecuteOnceWithCooldownAsync();
            }
            else if (!_isRunning)
            {
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
            ActionButton.Background = _runBrush;
            ActionButtonText.Text = "SPOOFING...";
            ActionButtonIcon.Glyph = "\uE895";

            await RandomizeMacAddressAsync();

            if (_exitPending || _isClosed)
                return;

            _cooldownSecondsLeft = 5;
            ActionButtonText.Text = $"COOLDOWN ({_cooldownSecondsLeft}s)";
            ActionButtonIcon.Glyph = "\uE823";
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
            ActionButtonIcon.Glyph = "\uE768";
            ActionButton.Background = _runBrush;
            UpdateControlAvailability();
        }

        private async Task StartLoopAsync(string? durationStr)
        {
            _isRunning = true;
            ActionButtonText.Text = "STOP ROTATION";
            ActionButtonIcon.Glyph = "\uE71A";
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
            ActionButtonIcon.Glyph = "\uE768";
            ActionButton.Background = _runBrush;
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
            int selectedIndex = ConfigurationComboBox.SelectedIndex;
            string? selectedStr = (ConfigurationComboBox.SelectedItem as Microsoft.UI.Xaml.Controls.ComboBoxItem)?.Content.ToString()?.Trim();
            bool isOnce = selectedIndex == 0 || string.Equals(selectedStr, "Once", StringComparison.OrdinalIgnoreCase);

            if (_isRunning)
            {
                if (isOnce)
                    StopLoop();
                else
                    _rotateTimer.Interval = ParseDuration(selectedStr);
            }
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
            ActionButtonIcon.Glyph = "\uE895";
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
