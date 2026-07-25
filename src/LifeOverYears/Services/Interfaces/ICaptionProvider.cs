namespace LifeOverYears.Services.Interfaces;

public interface ICaptionProvider
{
    // Returns the warm description text (no hashtags, no title).
    Task<string> GenerateDescriptionAsync(string systemPrompt, string userContext);
}
