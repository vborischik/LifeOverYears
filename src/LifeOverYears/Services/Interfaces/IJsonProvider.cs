namespace LifeOverYears.Services.Interfaces;

public interface IJsonProvider
{
    T Deserialize<T>(string json);
    string Serialize<T>(T value);
    Task<T> DeserializeFileAsync<T>(string path);
    Task SerializeFileAsync<T>(T value, string path);
    bool TryDeserialize<T>(string json, out T result);
}
