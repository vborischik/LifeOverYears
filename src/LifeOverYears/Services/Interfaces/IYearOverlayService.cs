namespace LifeOverYears.Services.Interfaces;

public interface IYearOverlayService
{
    // Draws the year, centered near the bottom of the image with a drop
    // shadow, writing the result as a new PNG at outputImagePath.
    Task StampAsync(string inputImagePath, int year, string outputImagePath);
}
