using System;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace MacSpoof
{
    public record NetworkResult(bool Success, string Message);

    public static class MacSpoofService
    {
        private const string NetworkClassRegistryKey = @"SYSTEM\CurrentControlSet\Control\Class\{4D36E972-E325-11CE-BFC1-08002BE10318}";
        private static readonly SemaphoreSlim OperationLock = new(1, 1);

        public static string GenerateRandomMacAddress()
        {
            byte[] bytes = RandomNumberGenerator.GetBytes(6);
            bytes[0] = (byte)((bytes[0] | 2) & 0xFE);
            return Convert.ToHexString(bytes);
        }

        public static string NormalizeMacAddress(string mac)
        {
            string clean = mac.Replace(":", "").Replace("-", "").Trim().ToUpperInvariant();
            if (clean.Length != 12 || !clean.All(Uri.IsHexDigit))
                throw new ArgumentException("Use six hexadecimal bytes for the MAC address.");
            if ((Convert.ToByte(clean[..2], 16) & 3) != 2)
                throw new ArgumentException("Use a locally administered unicast MAC address.");
            return clean;
        }

        public static string FormatMacAddress(string rawMac) => rawMac.Length == 12
            ? string.Join(":", Enumerable.Range(0, 6).Select(i => rawMac.Substring(i * 2, 2))) : "Unknown";

        public static NetworkInterface[] GetAdapters() => NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.NetworkInterfaceType is NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet)
            .OrderByDescending(n => n.OperationalStatus == OperationalStatus.Up)
            .ThenByDescending(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211).ToArray();

        private static NetworkInterface? FindAdapter(string id) => GetAdapters().FirstOrDefault(n => n.Id == id);
        public static string GetCurrentMacAddress(string id) => FormatMacAddress(FindAdapter(id)?.GetPhysicalAddress().ToString() ?? "");

        private static RegistryKey OpenAdapterKey(string id)
        {
            using var root = Registry.LocalMachine.OpenSubKey(NetworkClassRegistryKey)
                ?? throw new InvalidOperationException("Network adapter registry is unavailable.");
            foreach (string name in root.GetSubKeyNames())
            {
                if (name.Length != 4 || !int.TryParse(name, out _)) continue;
                using var key = root.OpenSubKey(name);
                if (Guid.TryParse(key?.GetValue("NetCfgInstanceId")?.ToString(), out var candidate)
                    && Guid.TryParse(id, out var target) && candidate == target)
                    return root.OpenSubKey(name, true) ?? throw new InvalidOperationException("Cannot edit this adapter.");
            }
            throw new InvalidOperationException("No registry entry matches the selected adapter.");
        }

        public static async Task<NetworkResult> ChangeMacAsync(string id, bool restore, bool clearCache, string? newMac = null)
        {
            if (!await OperationLock.WaitAsync(0)) return new(false, "Another network operation is still running.");
            try
            {
                var nic = FindAdapter(id) ?? throw new InvalidOperationException("Selected adapter is unavailable.");
                string? target = restore ? null : NormalizeMacAddress(newMac ?? GenerateRandomMacAddress());
                bool wasConnected = nic.OperationalStatus == OperationalStatus.Up;
                using var key = OpenAdapterKey(id);
                object? previous = key.GetValue("NetworkAddress");
                var previousKind = previous == null ? RegistryValueKind.String : key.GetValueKind("NetworkAddress");
                try
                {
                    if (restore) key.DeleteValue("NetworkAddress", false);
                    else key.SetValue("NetworkAddress", target!, RegistryValueKind.String);
                    await RestartAdapterAsync(nic.Name);
                    if (!await WaitForAdapterAsync(id, target, wasConnected))
                        throw new InvalidOperationException("The driver did not apply the MAC or the adapter did not reconnect within 45 seconds.");
                }
                catch (Exception changeError)
                {
                    try
                    {
                        if (previous == null) key.DeleteValue("NetworkAddress", false);
                        else key.SetValue("NetworkAddress", previous, previousKind);
                        await RestartAdapterAsync(nic.Name);
                        bool recovered = await WaitForAdapterAsync(id, null, wasConnected);
                        return new(false, $"{changeError.Message} Previous setting restored. " +
                            (recovered ? "Adapter recovered." : "Reconnect in Windows Wi-Fi settings; recovery could not be verified."));
                    }
                    catch (Exception recoveryError)
                    {
                        return new(false, $"{changeError.Message} Recovery failed: {recoveryError.Message} Enable the adapter in Windows network settings.");
                    }
                }
                string message = restore ? "MAC override removed; driver default restored." : "MAC change verified.";
                if (clearCache)
                {
                    try { await ClearCachesCoreAsync(nic); message += " Network caches refreshed."; }
                    catch (Exception ex) { return new(false, message + " Cache refresh failed: " + ex.Message); }
                }
                return new(true, message);
            }
            catch (Exception ex) { return new(false, ex.Message); }
            finally { OperationLock.Release(); }
        }

        private static async Task<bool> WaitForAdapterAsync(string id, string? target, bool requireConnection)
        {
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(45))
            {
                var nic = FindAdapter(id);
                if (nic != null && nic.GetPhysicalAddress().GetAddressBytes().Length == 6 &&
                    (target == null || nic.GetPhysicalAddress().ToString().Equals(target, StringComparison.OrdinalIgnoreCase)) &&
                    (!requireConnection || (nic.OperationalStatus == OperationalStatus.Up &&
                        nic.GetIPProperties().UnicastAddresses.Any(a =>
                            !System.Net.IPAddress.IsLoopback(a.Address) &&
                            !a.Address.IsIPv6LinkLocal && !a.Address.ToString().StartsWith("169.254.") &&
                            !a.Address.Equals(System.Net.IPAddress.Any))))) return true;
                await Task.Delay(1000);
            }
            return false;
        }

        private static async Task RestartAdapterAsync(string name)
        {
            // Always try to enable, even if disabling fails or times out.
            try { await RunAsync("netsh.exe", "interface", "set", "interface", "name=" + name, "admin=disabled"); }
            finally { await RunAsync("netsh.exe", "interface", "set", "interface", "name=" + name, "admin=enabled"); }
        }

        public static async Task<NetworkResult> ClearCachesAsync(string id)
        {
            if (!await OperationLock.WaitAsync(0)) return new(false, "Another network operation is still running.");
            try
            {
                await ClearCachesCoreAsync(FindAdapter(id) ?? throw new InvalidOperationException("Selected adapter is unavailable."));
                return new(true, "DNS and adapter neighbor caches cleared; DHCP renewed when enabled.");
            }
            catch (Exception ex) { return new(false, "Cache refresh incomplete: " + ex.Message); }
            finally { OperationLock.Release(); }
        }

        private static async Task ClearCachesCoreAsync(NetworkInterface nic)
        {
            await RunAsync("ipconfig.exe", "/flushdns");
            if (nic.Supports(NetworkInterfaceComponent.IPv4))
                await RunAsync("netsh.exe", "interface", "ipv4", "delete", "arpcache", "name=" + nic.Name, "store=active");
            if (nic.Supports(NetworkInterfaceComponent.IPv6))
                await RunAsync("netsh.exe", "interface", "ipv6", "delete", "neighbors", "interface=" + nic.Name, "store=active");
            if (nic.Supports(NetworkInterfaceComponent.IPv4) && nic.GetIPProperties().GetIPv4Properties()?.IsDhcpEnabled == true)
            {
                // ipconfig treats these as wildcards; never renew other adapters accidentally.
                if (nic.Name.IndexOfAny(new[] { '*', '?' }) >= 0)
                    throw new InvalidOperationException("Rename the adapter without wildcard characters before renewing DHCP.");
                await RunAsync("ipconfig.exe", "/renew", nic.Name);
            }
        }

        private static async Task RunAsync(string executable, params string[] arguments)
        {
            var info = new ProcessStartInfo(System.IO.Path.Combine(Environment.SystemDirectory, executable))
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (string argument in arguments) info.ArgumentList.Add(argument);
            using var process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start {executable}.");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                process.Kill(true);
                await process.WaitForExitAsync();
                throw new TimeoutException($"{executable} timed out.");
            }
            string details = ((await output) + " " + (await error)).Trim();
            if (process.ExitCode != 0) throw new InvalidOperationException($"{executable} failed ({process.ExitCode}): {details}");
        }
    }
}
