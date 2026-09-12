using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Diagnostics;
using System.Threading.Tasks;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;

namespace MacSpoof
{
    public sealed partial class MainWindow : Window
    {
        private DispatcherTimer _rotateTimer;
        private DispatcherTimer _pollTimer;
        private DispatcherTimer _cooldownTimer;
        private int _cooldownSecondsLeft = 0;
        private bool _isRunning = false;
        private bool _operationInProgress;
        public bool IsNetworkOperationRunning => _operationInProgress;
        public string CurrentMacAddress => MacSpoofService.GetCurrentMacAddress(SelectedAdapterId);
        private string SelectedAdapterId => (AdapterComboBox.SelectedItem as NetworkInterface)?.Id ?? "";
        private TrayIconManager? _trayManager;

        private readonly SolidColorBrush _runBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 29, 99, 237));
        private readonly SolidColorBrush _stopBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 211, 47, 47));

        public MainWindow()
        {
            InitializeComponent();
            SetFixedSize();
            LoadBackgroundImage();
            AdapterComboBox.ItemsSource = MacSpoofService.GetAdapters();
            AdapterComboBox.SelectedIndex = 0;
            LoadCurrentMacAddress();

            _rotateTimer = new DispatcherTimer();
            _rotateTimer.Tick += RotateTimer_Tick;

            _cooldownTimer = new DispatcherTimer();
            _cooldownTimer.Interval = TimeSpan.FromSeconds(1);
            _cooldownTimer.Tick += CooldownTimer_Tick;

            _pollTimer = new DispatcherTimer();
            _pollTimer.Interval = TimeSpan.FromSeconds(2);
            _pollTimer.Tick += (s, e) => LoadCurrentMacAddress();
            _pollTimer.Start();

            // Initialize System Tray Icon
            try
            {
                _trayManager = new TrayIconManager(this);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to init tray icon: {ex.Message}");
            }

            AppWindow.Closing += (s, e) => { e.Cancel = _operationInProgress; };

            this.Closed += (s, e) =>
            {
                _rotateTimer.Stop();
                _pollTimer.Stop();
                _cooldownTimer.Stop();
                _trayManager?.Dispose();
            };
        }

        private void LoadBackgroundImage()
        {
            try
            {
                string bgPath = Path.Combine(AppContext.BaseDirectory, "Assets", "background.png");
                if (File.Exists(bgPath))
                {
                    BackgroundImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(bgPath));
                }
                else
                {
                    BackgroundImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/background.png"));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load background image: {ex.Message}");
            }
        }

        private void SetFixedSize()
        {
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            Microsoft.UI.WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
            
            var presenter = appWindow.Presenter as OverlappedPresenter;
            if (presenter != null)
            {
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
            }
            
            // Compact, streamlined widget size
            appWindow.Resize(new Windows.Graphics.SizeInt32(440, 720));
        }

        private void LoadCurrentMacAddress()
        {
            string formattedMac = MacSpoofService.GetCurrentMacAddress(SelectedAdapterId);
            CurrentMacTextBlock.Text = $"MAC: {formattedMac}";
            _trayManager?.UpdateTooltip($"MacSpoof: {formattedMac}");
        }

        private async Task RandomizeMacAddressAsync()
        {
            await RunNetworkOperationAsync(() => MacSpoofService.ChangeMacAsync(
                SelectedAdapterId, false, ClearCacheCheckBox.IsChecked == true));
        }

        private async Task RunNetworkOperationAsync(Func<Task<NetworkResult>> operation)
        {
            if (_operationInProgress) return;
            _operationInProgress = true;
            _rotateTimer.Stop();
            ActionButton.IsEnabled = false;
            AdapterComboBox.IsEnabled = false;
            ConfigurationComboBox.IsEnabled = false;
            RestoreButton.IsEnabled = false;
            ClearCacheButton.IsEnabled = false;
            StatusTextBlock.Text = "Working; the adapter may briefly disconnect...";
            try
            {
                var result = await operation();
                StatusTextBlock.Text = result.Message;
                if (!result.Success) StopLoop();
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
                ActionButton.IsEnabled = _cooldownSecondsLeft == 0;
                AdapterComboBox.IsEnabled = !_isRunning;
                ConfigurationComboBox.IsEnabled = true;
                RestoreButton.IsEnabled = true;
                ClearCacheButton.IsEnabled = true;
                if (_isRunning) _rotateTimer.Start();
            }
        }

        private async void RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            StopLoop();
            await RunNetworkOperationAsync(() => MacSpoofService.ChangeMacAsync(SelectedAdapterId, true, ClearCacheCheckBox.IsChecked == true));
        }

        private async void ClearCacheButton_Click(object sender, RoutedEventArgs e)
        {
            StopLoop();
            await RunNetworkOperationAsync(() => MacSpoofService.ClearCachesAsync(SelectedAdapterId));
        }

        private async void ActionButton_Click(object sender, RoutedEventArgs e)
        {
            if (_cooldownSecondsLeft > 0 || _operationInProgress) return;

            int selectedIndex = ConfigurationComboBox.SelectedIndex;
            string? selectedStr = (ConfigurationComboBox.SelectedItem as Microsoft.UI.Xaml.Controls.ComboBoxItem)?.Content.ToString()?.Trim();
            bool isOnce = selectedIndex == 0 || string.Equals(selectedStr, "Once", StringComparison.OrdinalIgnoreCase);

            if (isOnce)
            {
                _isRunning = false;
                _rotateTimer.Stop();
                await ExecuteOnceWithCooldownAsync();
            }
            else
            {
                if (!_isRunning)
                {
                    await StartLoopAsync(selectedStr);
                }
                else
                {
                    StopLoop();
                }
            }
        }

        public async void TriggerSpoofOnceFromTray()
        {
            if (_cooldownSecondsLeft > 0 || _operationInProgress) return;
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

            _cooldownSecondsLeft = 5;
            ActionButtonText.Text = $"COOLDOWN ({_cooldownSecondsLeft}s)";
            ActionButtonIcon.Glyph = "\uE823";
            _cooldownTimer.Start();
        }

        private void CooldownTimer_Tick(object? sender, object e)
        {
            _cooldownSecondsLeft--;
            if (_cooldownSecondsLeft > 0)
            {
                ActionButtonText.Text = $"COOLDOWN ({_cooldownSecondsLeft}s)";
            }
            else
            {
                _cooldownTimer.Stop();
                _cooldownSecondsLeft = 0;
                ActionButton.IsEnabled = !_operationInProgress;
                ActionButtonText.Text = "RUN";
                ActionButtonIcon.Glyph = "\uE768";
                ActionButton.Background = _runBrush;
            }
        }

        private async Task StartLoopAsync(string? durationStr)
        {
            _isRunning = true;
            ActionButtonText.Text = "STOP";
            ActionButtonIcon.Glyph = "\uE71A";
            ActionButton.Background = _stopBrush;

            _rotateTimer.Interval = ParseDuration(durationStr);
            await RandomizeMacAddressAsync();
        }

        private void StopLoop()
        {
            _isRunning = false;
            _rotateTimer.Stop();
            AdapterComboBox.IsEnabled = !_operationInProgress;
            ActionButtonText.Text = "RUN";
            ActionButtonIcon.Glyph = "\uE768";
            ActionButton.Background = _runBrush;
        }

        private async void RotateTimer_Tick(object? sender, object e)
        {
            await RandomizeMacAddressAsync();
        }

        private void ConfigurationComboBox_SelectionChanged(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
        {
            int selectedIndex = ConfigurationComboBox.SelectedIndex;
            string? selectedStr = (ConfigurationComboBox.SelectedItem as Microsoft.UI.Xaml.Controls.ComboBoxItem)?.Content.ToString()?.Trim();
            bool isOnce = selectedIndex == 0 || string.Equals(selectedStr, "Once", StringComparison.OrdinalIgnoreCase);

            if (_isRunning)
            {
                if (isOnce)
                {
                    StopLoop();
                }
                else
                {
                    _rotateTimer.Interval = ParseDuration(selectedStr);
                }
            }
        }

        private TimeSpan ParseDuration(string? durationStr)
        {
            if (durationStr == null) return TimeSpan.FromMinutes(15);
            
            var parts = durationStr.Split(' ');
            if (parts.Length == 2 && int.TryParse(parts[0], out int value))
            {
                if (parts[1].ToLower().Contains("second")) return TimeSpan.FromSeconds(value);
                if (parts[1].ToLower().Contains("minute")) return TimeSpan.FromMinutes(value);
                if (parts[1].ToLower().Contains("hour")) return TimeSpan.FromHours(value);
            }
            return TimeSpan.FromMinutes(15);
        }
    }
}
