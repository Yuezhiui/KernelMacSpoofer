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

        private readonly SolidColorBrush _runBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 29, 99, 237));
        private readonly SolidColorBrush _stopBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 211, 47, 47));

        public MainWindow()
        {
            InitializeComponent();
            SetFixedSize();
            LoadBackgroundImage();
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
            
            appWindow.Resize(new Windows.Graphics.SizeInt32(450, 700));
        }

        private void LoadCurrentMacAddress()
        {
            string formattedMac = MacSpoofService.GetCurrentMacAddress();
            CurrentMacTextBlock.Text = $"MAC: {formattedMac}";
        }

        private async Task RandomizeMacAddressAsync()
        {
            try
            {
                bool success = await MacSpoofService.SpoofActiveAdapterAsync();
                await Task.Delay(2000);
                LoadCurrentMacAddress();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error changing MAC address: {ex.Message}");
            }
        }

        private async void ActionButton_Click(object sender, RoutedEventArgs e)
        {
            string? selectedStr = (ConfigurationComboBox.SelectedItem as Microsoft.UI.Xaml.Controls.ComboBoxItem)?.Content.ToString();

            if (string.Equals(selectedStr, "Once", StringComparison.OrdinalIgnoreCase))
            {
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

        private async Task ExecuteOnceWithCooldownAsync()
        {
            ActionButton.IsEnabled = false;
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
                ActionButton.IsEnabled = true;
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

            await RandomizeMacAddressAsync();

            _rotateTimer.Interval = ParseDuration(durationStr);
            _rotateTimer.Start();
        }

        private void StopLoop()
        {
            _isRunning = false;
            _rotateTimer.Stop();
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
            string? selectedStr = (ConfigurationComboBox.SelectedItem as Microsoft.UI.Xaml.Controls.ComboBoxItem)?.Content.ToString();

            if (_isRunning)
            {
                if (string.Equals(selectedStr, "Once", StringComparison.OrdinalIgnoreCase))
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
