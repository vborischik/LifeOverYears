namespace LifeOverYears.Services.Interfaces;

public interface IImageGenerationProvider
{
    Task CleanBaseAsync(string sourcePath, string prompt, string outputPath);
    Task GenerateEraAsync(string basePath, string prompt, int year, string outputPath);
}
