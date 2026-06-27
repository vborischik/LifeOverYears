using System.Text.Json;
using LifeOverYears.Services.Interfaces;

namespace LifeOverYears.Providers;

public sealed class JsonProvider : IJsonProvider
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public T Deserialize<T>(string json)
        => JsonSerializer.Deserialize<T>(json, Options)
           ?? throw new JsonException($"Deserialization of {typeof(T).Name} returned null");

    public string Serialize<T>(T value)
        => JsonSerializer.Serialize(value, Options);

    public async Task<T> DeserializeFileAsync<T>(string path)
    {
        var json = await File.ReadAllTextAsync(path);
        return Deserialize<T>(json);
    }

    public async Task SerializeFileAsync<T>(T value, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = Serialize(value);
        await File.WriteAllTextAsync(path, json);
    }

    public bool TryDeserialize<T>(string json, out T result)
    {
        try
        {
            result = Deserialize<T>(json);
            return true;
        }
        catch
        {
            result = default!;
            return false;
        }
    }
}
