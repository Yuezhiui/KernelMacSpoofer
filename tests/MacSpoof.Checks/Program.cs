using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using MacSpoof;

var seen = new HashSet<string>();
for (int i = 0; i < 10000; i++)
{
    string mac = MacSpoofService.GenerateRandomMacAddress();
    byte[] bytes = Convert.FromHexString(mac);
    if (bytes.Length != 6 || (bytes[0] & 1) != 0 || (bytes[0] & 2) == 0 || !seen.Add(mac))
        throw new Exception("Invalid or duplicate generated address.");
}
if (MacSpoofService.NormalizeMacAddress("02:ab:cd:12:34:56") != "02ABCD123456") throw new Exception("Normalization failed.");
foreach (string invalid in new[] { "", "02GG00112233", "0200112233", "FF0011223344", "000011223344", "02!0011223344" })
{
    try { MacSpoofService.NormalizeMacAddress(invalid); }
    catch (ArgumentException) { continue; }
    throw new Exception("Invalid input accepted: " + invalid);
}
if (MacSpoofService.FormatMacAddress("02ABCD123456") != "02:AB:CD:12:34:56") throw new Exception("Formatting failed.");

if (MacSpoofService.GetRollbackExpectedMac("02:ab:cd:12:34:56") != "02ABCD123456")
    throw new Exception("Valid local rollback MAC was not normalized.");
if (MacSpoofService.GetRollbackExpectedMac("00-11-22-33-44-55") != "001122334455")
    throw new Exception("Valid universal unicast rollback MAC was not accepted.");
foreach (object? invalidPrevious in new object?[] { null, 1234, "", "000000000000", "01AABBCCDDEE", "02GG00112233" })
{
    if (MacSpoofService.GetRollbackExpectedMac(invalidPrevious) != null)
        throw new Exception("Invalid rollback MAC was accepted: " + invalidPrevious);
}

var usableAddresses = new[] { IPAddress.Parse("192.168.1.25"), IPAddress.Parse("2001:db8::25") };
foreach (var address in usableAddresses)
    if (!MacSpoofService.IsUsableIpAddress(address)) throw new Exception("Usable IP rejected: " + address);
foreach (var address in new[]
{
    IPAddress.Loopback,
    IPAddress.IPv6Loopback,
    IPAddress.Any,
    IPAddress.IPv6Any,
    IPAddress.Parse("169.254.10.20"),
    IPAddress.Parse("fe80::1234")
})
    if (MacSpoofService.IsUsableIpAddress(address)) throw new Exception("Unusable IP accepted: " + address);
if (!MacSpoofService.HasUsableIpAddress(OperationalStatus.Up, usableAddresses))
    throw new Exception("Up adapter with usable IP was not considered connected.");
if (MacSpoofService.HasUsableIpAddress(OperationalStatus.Down, usableAddresses) ||
    MacSpoofService.HasUsableIpAddress(OperationalStatus.Up, new[] { IPAddress.Any }))
    throw new Exception("Pre-change connectivity requirement is too broad.");

string hostileAlias = "Ethernet & whoami | test";
string[] disableArgs = MacSpoofService.BuildInterfaceAdminArguments(hostileAlias, false);
string[] enableArgs = MacSpoofService.BuildInterfaceAdminArguments(hostileAlias, true);
if (disableArgs.Length != 5 || disableArgs[3] != "name=" + hostileAlias || disableArgs[4] != "admin=disabled" ||
    enableArgs.Length != 5 || enableArgs[3] != "name=" + hostileAlias || enableArgs[4] != "admin=enabled")
    throw new Exception("Adapter command arguments were not kept as atomic ArgumentList values.");

var partialSuccess = MacSpoofService.BuildSuccessfulChangeResult(false, true, "simulated cache failure");
if (!partialSuccess.Success || !partialSuccess.Message.Contains("MAC change verified.", StringComparison.Ordinal) ||
    !partialSuccess.Message.Contains("Warning: cache refresh incomplete:", StringComparison.Ordinal))
    throw new Exception("Cache failure was not represented as a successful MAC change with a warning.");

var missing = await MacSpoofService.ChangeMacAsync("missing-adapter", false, false);
var cache = await MacSpoofService.ClearCachesAsync("missing-adapter");
if (missing.Success || cache.Success) throw new Exception("Missing adapter reported success.");
Console.WriteLine("PASS: random MACs, validation, rollback expectation, usable-IP state, command arguments, cache warnings, and missing adapter handling. No network settings changed.");
