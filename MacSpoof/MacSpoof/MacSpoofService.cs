using System;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace MacSpoof
{
    /// <summary>
    /// Pure C# native Windows MAC Address Spoofing and Network Management Service.
    /// Operates directly through Windows Registry and Network Subsystems without third-party dependencies.
    /// </summary>
    public static class MacSpoofService
    {
        private const string NetworkClassRegistryKey = 
            @"SYSTEM\CurrentControlSet\Control\Class\{4D36E972-E325-11CE-BFC1-08002BE10318}";

        private static readonly char[] ValidSecondNibble = { '2', '6', 'A', 'E' };

        /// <summary>
        /// Generates a valid locally administered unicast MAC address compliant with Windows Wi-Fi / Ethernet drivers.
        /// </summary>
        public static string GenerateRandomMacAddress()
        {
            byte[] bytes = new byte[6];
            RandomNumberGenerator.Fill(bytes);

            char firstChar = "02468ACEF"[Random.Shared.Next(9)];
            char secondChar = ValidSecondNibble[Random.Shared.Next(ValidSecondNibble.Length)];

            string remainingHex = Convert.ToHexString(bytes.AsSpan(1));
            return $"{firstChar}{secondChar}{remainingHex}";
        }

        /// <summary>
        /// Formats a 12-character hex MAC string into standard colon notation (XX:XX:XX:XX:XX:XX).
        /// </summary>
        public static string FormatMacAddress(string rawMac)
        {
            if (string.IsNullOrEmpty(rawMac)) return "Unknown";
            string clean = new string(rawMac.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            if (clean.Length != 12) return rawMac;
            return string.Join(":", Enumerable.Range(0, 6).Select(i => clean.Substring(i * 2, 2)));
        }

        /// <summary>
        /// Retrieves the currently active network interface (Wi-Fi or Ethernet).
        /// </summary>
        public static NetworkInterface? GetActiveInterface()
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up &&
                              nic.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                              nic.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .OrderByDescending(nic => nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                .ThenByDescending(nic => nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                .FirstOrDefault();
        }

        /// <summary>
        /// Gets the current MAC address formatted as XX:XX:XX:XX:XX:XX.
        /// </summary>
        public static string GetCurrentMacAddress()
        {
            var nic = GetActiveInterface();
            if (nic != null)
            {
                var bytes = nic.GetPhysicalAddress().GetAddressBytes();
                if (bytes != null && bytes.Length == 6)
                {
                    return string.Join(":", bytes.Select(b => b.ToString("X2")));
                }
            }
            return "Unknown";
        }

        /// <summary>
        /// Spoofs the MAC address for the active adapter in the Windows Registry and restarts the adapter.
        /// </summary>
        public static async Task<bool> SpoofActiveAdapterAsync(string? newMac = null)
        {
            var nic = GetActiveInterface();
            if (nic == null) return false;

            string targetMac = newMac ?? GenerateRandomMacAddress();
            string cleanMac = new string(targetMac.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

            return await Task.Run(() =>
            {
                try
                {
                    using var baseKey = Registry.LocalMachine.OpenSubKey(NetworkClassRegistryKey, true);
                    if (baseKey == null) return false;

                    string? matchedSubKey = null;
                    foreach (var subKeyName in baseKey.GetSubKeyNames())
                    {
                        if (subKeyName.Length != 4 || !int.TryParse(subKeyName, out _))
                            continue;

                        using var subKey = baseKey.OpenSubKey(subKeyName);
                        if (subKey == null) continue;

                        var netCfgId = subKey.GetValue("NetCfgInstanceId")?.ToString();
                        var driverDesc = subKey.GetValue("DriverDesc")?.ToString();

                        if (string.Equals(netCfgId, nic.Id, StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrEmpty(driverDesc) && !string.IsNullOrEmpty(nic.Description) &&
                             (driverDesc.Contains(nic.Description, StringComparison.OrdinalIgnoreCase) ||
                              nic.Description.Contains(driverDesc, StringComparison.OrdinalIgnoreCase))))
                        {
                            matchedSubKey = subKeyName;
                            break;
                        }
                    }

                    if (matchedSubKey == null) return false;

                    using (var targetKey = baseKey.OpenSubKey(matchedSubKey, true))
                    {
                        if (targetKey == null) return false;
                        targetKey.SetValue("NetworkAddress", cleanMac, RegistryValueKind.String);
                    }

                    // Restart adapter to apply changes
                    RestartAdapter(nic.Name);
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error spoofing MAC address: {ex.Message}");
                    return false;
                }
            });
        }

        /// <summary>
        /// Restarts a network adapter by name to apply registry changes.
        /// </summary>
        public static void RestartAdapter(string adapterName)
        {
            try
            {
                var psiDisable = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = $"interface set interface \"{adapterName}\" disable",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                var p1 = Process.Start(psiDisable);
                p1?.WaitForExit(5000);

                var psiEnable = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = $"interface set interface \"{adapterName}\" enable",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                var p2 = Process.Start(psiEnable);
                p2?.WaitForExit(5000);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RestartAdapter error: {ex.Message}");
            }
        }
    }
}