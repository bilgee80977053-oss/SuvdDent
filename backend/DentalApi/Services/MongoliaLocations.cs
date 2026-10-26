using System.Text.Json;

namespace DentalApi.Services;

public record ProvinceEntry(string Code, string Name);
public record DistrictEntry(string Code, string Name, string ProvinceOrCapitalCode);
public record LocationCatalog(string SourceVersion, string[] Sources, ProvinceEntry[] Provinces, DistrictEntry[] Districts);

public sealed class MongoliaLocations
{
    private readonly LocationCatalog catalog;
    public MongoliaLocations()
    {
        using var stream = typeof(MongoliaLocations).Assembly.GetManifestResourceStream("DentalApi.ReferenceData.MongoliaLocations.json")!;
        catalog = JsonSerializer.Deserialize<LocationCatalog>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        if (catalog.Provinces.Length != 22 || catalog.Districts.Length != 339 ||
            catalog.Districts.Select(x => x.Code).Distinct().Count() != 339 ||
            catalog.Districts.Any(d => !catalog.Provinces.Any(p => p.Code == d.ProvinceOrCapitalCode)))
            throw new InvalidOperationException("Invalid location catalog.");
    }
    public ProvinceEntry[] Provinces() => catalog.Provinces.ToArray();
    public DistrictEntry[] Districts(string province) => catalog.Districts.Where(x => x.ProvinceOrCapitalCode == province).ToArray();
    public bool Valid(string country, string province, string district) => country == "MN" &&
        catalog.Districts.Any(x => x.Code == district && x.ProvinceOrCapitalCode == province);
    public string? Suggest(string country, string province, string district) => !Valid(country, province, district) ? null :
        province is "83" or "84" or "85" ? "Asia/Hovd" : "Asia/Ulaanbaatar";
    public static bool Supported(string zone) => zone is "Asia/Ulaanbaatar" or "Asia/Hovd";
}
