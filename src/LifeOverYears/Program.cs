using Autofac;
using LifeOverYears;
using LifeOverYears.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

static async Task<int> RunAsync(string[] args)
{
    var configuration = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
        .Build();

    using var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));

    var builder = new ContainerBuilder();
    builder.RegisterModule(new AppModule(configuration, loggerFactory));
    await using var container = builder.Build();

    var photoPath = ResolvePhotoPath(args);
    var years     = args.Length >= 2
        ? args.Skip(1).Select(int.Parse).ToList()
        : new List<int> { 1985 };

    try
    {
        var pipeline = container.Resolve<Pipeline>();
        await pipeline.RunAsync(photoPath, years);
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

    var testImageDir = Path.Combine(Directory.GetCurrentDirectory(), "testImage");
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
