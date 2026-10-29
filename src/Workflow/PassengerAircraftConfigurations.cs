using System.Security.Cryptography;
using System.Text.RegularExpressions;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

/// <summary>Read-only evidence adapter. Does not discover Community, infer seats, modify aircraft files or grant live acceptance.</summary>
public sealed class PassengerAircraftConfigurations(Func<string?> caravanPackageDirectory) : IPassengerAircraftConfigurations
{
    public string SupportSummary => "Physical adapter: Black Square Caravan Professional 0.1.2 gear variant, exact reviewed configuration only (8 individual passenger stations). Vision Jet / stock streamed aircraft: capacity and station configuration unavailable, physical boarding blocked. Cabin people/animations: no supported control established. Missing per-station limits and planned CG remain explicit limitations; reported limits are enforced. Live acceptance pending.";
    public PassengerAircraftConfiguration Resolve(string aircraftType, AircraftLoadingSnapshot snapshot)
    {
        if (aircraftType != "c208") throw new InvalidOperationException(SupportSummary);
        var root = caravanPackageDirectory();
        if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("Select the installed Black Square Caravan Professional package directory in passenger loading. No simulator files will be changed.");
        var aircraft = Path.Combine(root, "SimObjects", "Airplanes", "bksq-aircraft-caravanpro");
        var flight = ReadExact(Path.Combine(aircraft, "flight_model.cfg"), "70406399CEA603025179074FD5FCA978DE1718134DFAEA1B2147C71BBB068892");
        var cfg = ReadExact(Path.Combine(aircraft, "aircraft.cfg"), "2C20B41F5FC4BCB5DC80FDA5E512901B3244E76822DD2DB7CEE72CC772EA38FB");
        ReadExact(Path.Combine(root, "manifest.json"), "0B6F50757C2E7FBF8D803C015D6B8BE956347F3536ACFE4318853B73DC1DFD56");
        var titles = Regex.Matches(cfg, "^\\s*title\\s*=\\s*\"([^\"]+)\"", RegexOptions.Multiline | RegexOptions.IgnoreCase).Select(x => x.Groups[1].Value).ToArray();
        if (!titles.Contains(snapshot.Aircraft.Title, StringComparer.Ordinal))
            throw new InvalidOperationException("Actual live aircraft title is not one of the reviewed Caravan gear variants. Confirm the company aircraft binding separately.");
        // The exact hashed source has individually numbered type-3 stations .2 through .9.
        // CFG indices are zero-based; existing native payload API indices are one-based.
        _ = flight;
        return new("bksq-caravanpro-gear-0.1.2", aircraftType, snapshot.Aircraft, 15,
            Enumerable.Range(3, 8).Select(i => new PassengerStationConfiguration(i, 1, null)).ToArray(),
            LoadingUnits.PoundsToKilograms(8750), null,
            "70406399CEA603025179074FD5FCA978DE1718134DFAEA1B2147C71BBB068892",
            "Black Square Caravan Professional 0.1.2 gear: exact cfg/manifest hashes; eight numbered individual passenger stations. SDK cfg/SimVar index and pound units reviewed; live acceptance pending.",
            "Physical weight only. No seated passenger or boarding animation control established.");
    }
    private static string ReadExact(string path, string hash)
    {
        var bytes = File.ReadAllBytes(path);
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(hash, StringComparison.Ordinal))
            throw new InvalidOperationException("Installed aircraft configuration/version differs from the reviewed passenger profile. Loading blocked until configuration review.");
        return System.Text.Encoding.UTF8.GetString(bytes);
    }
}
