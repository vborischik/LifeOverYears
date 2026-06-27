using LifeOverYears.Models;

namespace LifeOverYears.Services;

public static class SceneDnaValidator
{
    public static IReadOnlyList<string> Validate(SceneDna s)
    {
        var missing = new List<string>();

        if (s.Camera.Height == "eye-level")     missing.Add("camera.height");
        if (s.Camera.Direction == "street")     missing.Add("camera.direction");
        if (s.Geometry.Roads.Count == 0)        missing.Add("geometry.roads");
        if (s.Geometry.Buildings.Count == 0)    missing.Add("geometry.buildings");
        if (s.ImmutableElements.Count == 0)     missing.Add("immutable_elements");

        return missing;
    }
}
