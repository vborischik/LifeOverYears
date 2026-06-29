using LifeOverYears.Models;

namespace LifeOverYears.Services;

public static class SceneDnaValidator
{
    public static IReadOnlyList<string> Validate(SceneDna s)
    {
        var missing = new List<string>();

        if (s.Geometry.Roads.Count == 0)     missing.Add("geometry.roads");
        if (s.Geometry.Buildings.Count == 0) missing.Add("geometry.buildings");

        return missing;
    }
}
