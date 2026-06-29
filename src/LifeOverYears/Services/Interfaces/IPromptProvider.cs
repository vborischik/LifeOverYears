namespace LifeOverYears.Services.Interfaces;

public interface IPromptProvider
{
    Task<string> GeneratePromptAsync(string context);
}
