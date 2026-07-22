using System.Text.Json.Serialization;

namespace LifeOverYears.Models;

public record EraProfile(
    [property: JsonPropertyName("year")]           int Year,
    [property: JsonPropertyName("label")]          string Label,
    [property: JsonPropertyName("description")]    string Description,
    [property: JsonPropertyName("transportation")] Transportation Transportation,
    [property: JsonPropertyName("architecture")]   Architecture Architecture,
    [property: JsonPropertyName("business")]       Business Business,
    [property: JsonPropertyName("infrastructure")] Infrastructure Infrastructure,
    [property: JsonPropertyName("society")]        Society Society,
    [property: JsonPropertyName("environment")]    EraEnvironment Environment,
    [property: JsonPropertyName("photography")]    Photography Photography,
    [property: JsonPropertyName("scene_content")]  IReadOnlyDictionary<string, SceneContent>? SceneContent = null,
    [property: JsonPropertyName("people_mix")]     IReadOnlyList<string>? PeopleMix = null,
    [property: JsonPropertyName("allowed_scene_conditions")] IReadOnlyList<string>? AllowedSceneConditions = null);

// ── Transportation ────────────────────────────────────────────────────────────

public record Transportation(
    [property: JsonPropertyName("cars")]   Cars Cars,
    [property: JsonPropertyName("trucks")] Trucks Trucks,
    [property: JsonPropertyName("fuel")]   Fuel Fuel);

public record Cars(
    [property: JsonPropertyName("dominant_makes")]         IReadOnlyList<string> DominantMakes,
    [property: JsonPropertyName("specific_models")]        IReadOnlyList<string> SpecificModels,
    [property: JsonPropertyName("body_styles")]            IReadOnlyList<string> BodyStyles,
    [property: JsonPropertyName("visual_characteristics")] IReadOnlyList<string> VisualCharacteristics,
    [property: JsonPropertyName("colors")]                 IReadOnlyList<string> Colors,
    [property: JsonPropertyName("absent")]                 IReadOnlyList<string> Absent);

public record Trucks(
    [property: JsonPropertyName("dominant_makes")]         IReadOnlyList<string> DominantMakes,
    [property: JsonPropertyName("specific_models")]        IReadOnlyList<string> SpecificModels,
    [property: JsonPropertyName("visual_characteristics")] IReadOnlyList<string> VisualCharacteristics);

public record Fuel(
    [property: JsonPropertyName("average_price_per_gallon")] string AveragePricePerGallon,
    [property: JsonPropertyName("context")]                  string Context);

// ── Architecture ──────────────────────────────────────────────────────────────

public record Architecture(
    [property: JsonPropertyName("commercial")]   CommercialArchitecture Commercial,
    [property: JsonPropertyName("gas_stations")] GasStations GasStations);

public record CommercialArchitecture(
    [property: JsonPropertyName("styles")]          IReadOnlyList<string> Styles,
    [property: JsonPropertyName("materials")]       IReadOnlyList<string> Materials,
    [property: JsonPropertyName("characteristics")] IReadOnlyList<string> Characteristics);

public record GasStations(
    [property: JsonPropertyName("characteristics")] IReadOnlyList<string> Characteristics);

// ── Business ──────────────────────────────────────────────────────────────────

public record Business(
    [property: JsonPropertyName("active_brands")] IReadOnlyList<string> ActiveBrands,
    [property: JsonPropertyName("absent_brands")] IReadOnlyList<string> AbsentBrands,
    [property: JsonPropertyName("signage")]       Signage Signage,
    [property: JsonPropertyName("gas_brands")]    IReadOnlyList<string>? GasBrands = null);

public record Signage(
    [property: JsonPropertyName("characteristics")]  IReadOnlyList<string> Characteristics,
    [property: JsonPropertyName("typography_style")] string TypographyStyle);

// ── Infrastructure ────────────────────────────────────────────────────────────

public record Infrastructure(
    [property: JsonPropertyName("roads")]            Roads Roads,
    [property: JsonPropertyName("traffic_signs")]    TrafficSigns TrafficSigns,
    [property: JsonPropertyName("street_furniture")] StreetFurniture StreetFurniture,
    [property: JsonPropertyName("utilities")]        Utilities Utilities);

public record Roads(
    [property: JsonPropertyName("markings")]        IReadOnlyList<string> Markings,
    [property: JsonPropertyName("materials")]       IReadOnlyList<string> Materials,
    [property: JsonPropertyName("characteristics")] IReadOnlyList<string> Characteristics);

public record TrafficSigns(
    [property: JsonPropertyName("style")]           string Style,
    [property: JsonPropertyName("characteristics")] IReadOnlyList<string> Characteristics);

public record StreetFurniture(
    [property: JsonPropertyName("items")] IReadOnlyList<string> Items);

public record Utilities(
    [property: JsonPropertyName("characteristics")] IReadOnlyList<string> Characteristics,
    [property: JsonPropertyName("downtown_characteristics")] IReadOnlyList<string>? DowntownCharacteristics = null);

// ── Society ───────────────────────────────────────────────────────────────────

public record Society(
    [property: JsonPropertyName("fashion")]     Fashion Fashion,
    [property: JsonPropertyName("advertising")] Advertising Advertising);

public record Fashion(
    [property: JsonPropertyName("men")]    IReadOnlyList<string> Men,
    [property: JsonPropertyName("women")]  IReadOnlyList<string> Women,
    [property: JsonPropertyName("colors")] IReadOnlyList<string> Colors);

public record Advertising(
    [property: JsonPropertyName("style")] string Style,
    [property: JsonPropertyName("media")] IReadOnlyList<string> Media);

// ── Environment ───────────────────────────────────────────────────────────────
// Named EraEnvironment to avoid collision with SceneDna.cs Environment record

public record EraEnvironment(
    [property: JsonPropertyName("vegetation")] Vegetation Vegetation,
    [property: JsonPropertyName("lighting")]   Lighting Lighting);

public record Vegetation(
    [property: JsonPropertyName("characteristics")] IReadOnlyList<string> Characteristics);

public record Lighting(
    [property: JsonPropertyName("street_lights")] string StreetLights,
    [property: JsonPropertyName("commercial")]    string Commercial,
    [property: JsonPropertyName("signs")]         string Signs);

// ── Photography ───────────────────────────────────────────────────────────────

public record Photography(
    [property: JsonPropertyName("film_stock")]            string FilmStock,
    [property: JsonPropertyName("color_characteristics")] IReadOnlyList<string> ColorCharacteristics,
    [property: JsonPropertyName("grain")]                 string Grain,
    [property: JsonPropertyName("style")]                 string Style,
    [property: JsonPropertyName("color_mode")]            string? ColorMode = null);

// ── Scene content ─────────────────────────────────────────────────────────────

public record CountRange(
    [property: JsonPropertyName("min")] int Min,
    [property: JsonPropertyName("max")] int Max);

public record SceneContent(
    [property: JsonPropertyName("narrative")]         string Narrative,
    [property: JsonPropertyName("storefronts")]       IReadOnlyList<string> Storefronts,
    [property: JsonPropertyName("window_signs")]      IReadOnlyList<string> WindowSigns,
    [property: JsonPropertyName("people")]            CountRange People,
    [property: JsonPropertyName("people_activities")] IReadOnlyList<string> PeopleActivities,
    [property: JsonPropertyName("vehicles")]          CountRange Vehicles,
    [property: JsonPropertyName("extras")]            IReadOnlyList<string> Extras);
