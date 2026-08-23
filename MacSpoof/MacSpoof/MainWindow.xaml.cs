using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Diagnostics;
using System.Threading.Tasks;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;

namespace MacSpoof
{
    public sealed partial class MainWindow : Window
    {
        private DispatcherTimer _rotateTimer;
        private DispatcherTimer _pollTimer;
        private bool _isRunning = false;

        public MainWindow()
        {
            InitializeComponent();
            SetFixedSize();
            LoadBackgroundImage();
            LoadCurrentMacAddress();

            _rotateTimer = new DispatcherTimer();
            _rotateTimer.Tick += RotateTimer_Tick;

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
                // Use pure native C# MAC spoofer
                bool success = await MacSpoofService.SpoofActiveAdapterAsync();

                // Wait a moment for network adapter to reinitialize
                await Task.Delay(2000);

                // Reload the MAC address on UI to reflect changes
                LoadCurrentMacAddress();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error changing MAC address: {ex.Message}");
            }
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isRunning)
            {
                _isRunning = true;
                StartButtonText.Text = "STOP";
                StartButtonIcon.Glyph = "\uE71A"; // Stop icon 

                // Perform immediate spoof
                await RandomizeMacAddressAsync();

                if (AutoRotateSwitch.IsOn)
                {
                    string? selectedStr = (DurationComboBox.SelectedItem as Microsoft.UI.Xaml.Controls.ComboBoxItem)?.Content.ToString();
                    _rotateTimer.Interval = ParseDuration(selectedStr);
                    _rotateTimer.Start();
                }
            }
            else
            {
                _isRunning = false;
                _rotateTimer.Stop();
                StartButtonText.Text = "START";
                StartButtonIcon.Glyph = "\uE768"; // Play icon
            }
        }

        private async void RotateTimer_Tick(object? sender, object e)
        {
            await RandomizeMacAddressAsync();
        }

        private void AutoRotateSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isRunning)
            {
                if (AutoRotateSwitch.IsOn)
                {
                    string? selectedStr = (DurationComboBox.SelectedItem as Microsoft.UI.Xaml.Controls.ComboBoxItem)?.Content.ToString();
                    _rotateTimer.Interval = ParseDuration(selectedStr);
                    _rotateTimer.Start();
                }
                else
                {
                    _rotateTimer.Stop();
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
