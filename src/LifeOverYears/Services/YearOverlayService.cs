using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Path = System.IO.Path;

namespace LifeOverYears.Services;

public sealed class YearOverlayService : IYearOverlayService
{
    // Fonts most likely to be present across macOS/Linux CI images; falls
    // back to whatever SixLabors.Fonts finds installed on the box.
    private static readonly string[] PreferredFontFamilies =
        { "Arial", "Helvetica", "DejaVu Sans", "Liberation Sans", "Verdana" };

    private readonly ILogger<YearOverlayService> _logger;

    public YearOverlayService(ILogger<YearOverlayService> logger)
    {
        _logger = logger;
    }

    public async Task StampAsync(string inputImagePath, int year, string outputImagePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputImagePath))!);

        using var image = await Image.LoadAsync<Rgba32>(inputImagePath);

        var barHeight = (int)Math.Round(image.Height * 0.10);
        var barTop    = image.Height - barHeight;
        var barRect   = new RectangularPolygon(0, barTop, image.Width, barHeight);

        var font = ResolveFont(barHeight * 0.6f);
        var text = year.ToString();
        var textSize = TextMeasurer.MeasureSize(text, new TextOptions(font));
        var textOrigin = new PointF(
            image.Width / 2f - textSize.Width / 2f,
            barTop + barHeight / 2f - textSize.Height / 2f);
        var shadowOrigin = new PointF(textOrigin.X + 3, textOrigin.Y + 3);

        image.Mutate(ctx =>
        {
            ctx.Fill(Color.Black.WithAlpha(0.55f), barRect);
            ctx.DrawText(text, font, Color.Black.WithAlpha(0.7f), shadowOrigin);
            ctx.DrawText(text, font, Color.White, textOrigin);
        });

        await image.SaveAsPngAsync(outputImagePath);
        _logger.LogInformation("Stamped year {Year} onto {Output}", year, outputImagePath);
    }

    private static Font ResolveFont(float size)
    {
        foreach (var name in PreferredFontFamilies)
            if (SystemFonts.TryGet(name, out var family))
                return family.CreateFont(size, FontStyle.Bold);

        var fallback = SystemFonts.Families.FirstOrDefault();
        if (fallback == default)
            throw new InvalidOperationException("No system fonts available to render the year overlay.");

        return fallback.CreateFont(size, FontStyle.Bold);
    }
}
