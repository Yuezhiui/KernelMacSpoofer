using System;
using System.Collections.Generic;
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
var missing = await MacSpoofService.ChangeMacAsync("missing-adapter", false, false);
var cache = await MacSpoofService.ClearCachesAsync("missing-adapter");
if (missing.Success || cache.Success) throw new Exception("Missing adapter reported success.");
Console.WriteLine("PASS: 10,000 generated addresses, input validation, formatting, missing adapter handling. No network settings changed.");
