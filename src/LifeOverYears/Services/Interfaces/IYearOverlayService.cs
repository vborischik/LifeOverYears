namespace LifeOverYears.Services.Interfaces;

public interface IYearOverlayService
{
    // Draws a semi-transparent bar with the year centered in it onto the
    // bottom of the image, writing the result as a new PNG at outputImagePath.
    Task StampAsync(string inputImagePath, int year, string outputImagePath);
}
