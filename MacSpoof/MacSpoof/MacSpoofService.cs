using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
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
        private const string OperationMutexName = @"Local\MacSpoof.NetworkOperation";
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

        internal static string? GetRollbackExpectedMac(object? registryValue)
        {
            if (registryValue is not string value) return null;
            string clean = value.Replace(":", "").Replace("-", "").Trim().ToUpperInvariant();
            if (clean.Length != 12 || !clean.All(Uri.IsHexDigit) || clean == "000000000000") return null;
            return (Convert.ToByte(clean[..2], 16) & 1) == 0 ? clean : null;
        }

        internal static bool IsUsableIpAddress(IPAddress address) =>
            !IPAddress.IsLoopback(address) &&
            !address.IsIPv6LinkLocal &&
            !address.Equals(IPAddress.Any) &&
            !address.Equals(IPAddress.IPv6Any) &&
            !(address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
              address.GetAddressBytes() is var bytes && bytes[0] == 169 && bytes[1] == 254);

        internal static bool HasUsableIpAddress(OperationalStatus status, IEnumerable<IPAddress> addresses) =>
            status == OperationalStatus.Up && addresses.Any(IsUsableIpAddress);

        private static bool HasUsableIpAddress(NetworkInterface nic) =>
            HasUsableIpAddress(nic.OperationalStatus, nic.GetIPProperties().UnicastAddresses.Select(a => a.Address));

        internal static string[] BuildInterfaceAdminArguments(string name, bool enabled) =>
            new[] { "interface", "set", "interface", "name=" + name, enabled ? "admin=enabled" : "admin=disabled" };

        internal static NetworkResult BuildSuccessfulChangeResult(bool restore, bool cacheRefreshRequested, string? cacheError = null)
        {
            string message = restore ? "MAC override removed; adapter restarted successfully." : "MAC change verified.";
            if (!cacheRefreshRequested) return new(true, message);
            return cacheError == null
                ? new(true, message + " Network caches refreshed.")
                : new(true, message + " Warning: cache refresh incomplete: " + cacheError);
        }

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

        private static async Task<NetworkResult> RunExclusiveAsync(Func<Task<NetworkResult>> operation)
        {
            if (!await OperationLock.WaitAsync(0)) return new(false, "Another network operation is still running.");
            try
            {
                // Mutex ownership is thread-affine, so one worker thread owns it for the whole async operation.
                return await Task.Run(() =>
                {
                    using var mutex = new Mutex(false, OperationMutexName);
                    bool acquired;
                    try { acquired = mutex.WaitOne(0); }
                    catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired) return new NetworkResult(false, "Another MacSpoof process is changing network settings.");

                    try { return operation().GetAwaiter().GetResult(); }
                    finally { mutex.ReleaseMutex(); }
                });
            }
            finally { OperationLock.Release(); }
        }

        public static Task<NetworkResult> ChangeMacAsync(string id, bool restore, bool clearCache, string? newMac = null) =>
            RunExclusiveAsync(() => ChangeMacCoreAsync(id, restore, clearCache, newMac));

        private static async Task<NetworkResult> ChangeMacCoreAsync(string id, bool restore, bool clearCache, string? newMac)
        {
            try
            {
                var nic = FindAdapter(id) ?? throw new InvalidOperationException("Selected adapter is unavailable.");
                string? target = restore ? null : NormalizeMacAddress(newMac ?? GenerateRandomMacAddress());
                bool hadUsableIp = HasUsableIpAddress(nic);
                using var key = OpenAdapterKey(id);
                object? previous = key.GetValue("NetworkAddress");
                var previousKind = previous == null ? RegistryValueKind.String : key.GetValueKind("NetworkAddress");
                string? rollbackExpectedMac = GetRollbackExpectedMac(previous);
                try
                {
                    if (restore) key.DeleteValue("NetworkAddress", false);
                    else key.SetValue("NetworkAddress", target!, RegistryValueKind.String);
                    await RestartAdapterAsync(id);
                    if (!await WaitForAdapterAsync(id, target, hadUsableIp))
                        throw new InvalidOperationException("The driver did not apply the MAC or the adapter did not reconnect within 45 seconds.");
                }
                catch (Exception changeError)
                {
                    try
                    {
                        if (previous == null) key.DeleteValue("NetworkAddress", false);
                        else key.SetValue("NetworkAddress", previous, previousKind);
                        await RestartAdapterAsync(id);
                        bool recovered = await WaitForAdapterAsync(id, rollbackExpectedMac, hadUsableIp);
                        string recoveryMessage = recovered
                            ? rollbackExpectedMac == null
                                ? "Previous setting restored. Adapter recovered."
                                : "Previous MAC restored and verified."
                            : "Previous registry setting was written back, but adapter recovery could not be verified.";
                        return new(false, $"{changeError.Message} {recoveryMessage}");
                    }
                    catch (Exception recoveryError)
                    {
                        return new(false, $"{changeError.Message} Recovery failed: {recoveryError.Message} Enable the adapter in Windows network settings.");
                    }
                }
                if (clearCache)
                {
                    try
                    {
                        await ClearCachesCoreAsync(FindAdapter(id) ?? nic);
                        return BuildSuccessfulChangeResult(restore, true);
                    }
                    catch (Exception ex) { return BuildSuccessfulChangeResult(restore, true, ex.Message); }
                }
                return BuildSuccessfulChangeResult(restore, false);
            }
            catch (Exception ex) { return new(false, ex.Message); }
        }

        private static async Task<bool> WaitForAdapterAsync(string id, string? target, bool requireConnection)
        {
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(45))
            {
                var nic = FindAdapter(id);
                if (nic != null && nic.GetPhysicalAddress().GetAddressBytes().Length == 6 &&
                    (target == null || nic.GetPhysicalAddress().ToString().Equals(target, StringComparison.OrdinalIgnoreCase)) &&
                    (!requireConnection || HasUsableIpAddress(nic))) return true;
                await Task.Delay(1000);
            }
            return false;
        }

        private static async Task<string> SetAdapterAdminStateAsync(string id, bool enabled, string? fallbackName = null)
        {
            string name = FindAdapter(id)?.Name ?? fallbackName ??
                throw new InvalidOperationException("Selected adapter is unavailable.");
            try
            {
                await RunAsync("netsh.exe", BuildInterfaceAdminArguments(name, enabled));
                return name;
            }
            catch
            {
                string? currentName = FindAdapter(id)?.Name;
                if (currentName == null || currentName.Equals(name, StringComparison.Ordinal))
                    throw;
                await RunAsync("netsh.exe", BuildInterfaceAdminArguments(currentName, enabled));
                return currentName;
            }
        }

        private static async Task RestartAdapterAsync(string id)
        {
            string? disabledName = null;
            try { disabledName = await SetAdapterAdminStateAsync(id, false); }
            finally
            {
                // Resolve the alias again by stable adapter ID; fallback only if a disabled adapter temporarily disappears from enumeration.
                await SetAdapterAdminStateAsync(id, true, disabledName);
            }
        }

        public static Task<NetworkResult> ClearCachesAsync(string id) =>
            RunExclusiveAsync(async () =>
            {
                try
                {
                    await ClearCachesCoreAsync(FindAdapter(id) ?? throw new InvalidOperationException("Selected adapter is unavailable."));
                    return new(true, "DNS and adapter neighbor caches cleared; DHCP renewed when enabled.");
                }
                catch (Exception ex) { return new(false, "Cache refresh incomplete: " + ex.Message); }
            });

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
