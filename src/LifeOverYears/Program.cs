using Autofac;
using LifeOverYears;
using LifeOverYears.Services;
using Microsoft.Extensions.Logging;

static async Task<int> RunAsync(string[] args)
{
    var nvidiaKey = Environment.GetEnvironmentVariable("NVIDIA_API_KEY")
        ?? throw new InvalidOperationException("NVIDIA_API_KEY environment variable is not set");

    var photoPath = ResolvePhotoPath(args);
    var year      = args.Length >= 2 ? int.Parse(args[1]) : 1985;

    using var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));

    var builder = new ContainerBuilder();
    builder.RegisterModule(new AppModule(loggerFactory, nvidiaKey));
    await using var container = builder.Build();

    try
    {
        var pipeline = container.Resolve<Pipeline>();
        await pipeline.RunAsync(photoPath, year);
        return 0;
    }
    catch (Exception ex)
    {
        var logger = loggerFactory.CreateLogger("Program");
        logger.LogError(ex, "Pipeline failed");
        return 1;
    }
}

static string ResolvePhotoPath(string[] args)
{
    if (args.Length >= 1)
        return args[0];

    var testImageDir = Path.Combine(AppContext.BaseDirectory, "testImage");
    if (Directory.Exists(testImageDir))
    {
        var first = Directory.EnumerateFiles(testImageDir, "*.jpg")
            .Concat(Directory.EnumerateFiles(testImageDir, "*.jpeg"))
            .Concat(Directory.EnumerateFiles(testImageDir, "*.png"))
            .FirstOrDefault();

        if (first is not null)
            return first;
    }

    throw new InvalidOperationException("No photo path provided and no images found in testImage/");
}

return await RunAsync(args);
