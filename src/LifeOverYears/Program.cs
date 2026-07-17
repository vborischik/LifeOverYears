using Autofac;
using LifeOverYears;
using LifeOverYears.Providers;
using LifeOverYears.Services;
using LifeOverYears.Services.Interfaces; // TODO: remove smoke test
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

var projectRoot = FindProjectRoot();
Directory.SetCurrentDirectory(projectRoot);

static async Task<int> RunAsync(string[] args, string projectRoot)
{
    // TODO: remove smoke test
    // Fully isolated: no appsettings, no DI container, no vision/prompts.
    if (args.Contains("--smoke-video"))
    {
        var logCapture = new CapturingLoggerProvider();
        using var videoLoggerFactory = LoggerFactory.Create(b =>
            b.AddConsole().AddProvider(logCapture).SetMinimumLevel(LogLevel.Debug));
        var ffmpegProvider = new FfmpegProvider(videoLoggerFactory.CreateLogger<FfmpegProvider>());
        var videoService   = new VideoService(ffmpegProvider, videoLoggerFactory.CreateLogger<VideoService>());
        return await VideoSmokeTest.RunAsync(
            videoService, videoLoggerFactory.CreateLogger("VideoSmokeTest"), logCapture);
    }

    // TODO: remove smoke test
    bool isSmokeTest = args.Contains("--smoke-prompts");

    var configBuilder = new ConfigurationBuilder()
        .SetBasePath(projectRoot)
        .AddJsonFile("appsettings.json", optional: isSmokeTest, reloadOnChange: false);

    // TODO: remove smoke test
    if (isSmokeTest)
        configBuilder.AddInMemoryCollection(new[]
        {
            new KeyValuePair<string, string?>("Nvidia:ApiKey", "smoke-test-dummy")
        });

    var configuration = configBuilder.Build();

    using var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));

    var builder = new ContainerBuilder();
    builder.RegisterModule(new AppModule(configuration, loggerFactory));
    await using var container = builder.Build();

    // TODO: remove smoke test
    if (isSmokeTest)
    {
        var promptService = container.Resolve<IPromptService>();
        var dataService   = container.Resolve<IDataService>();
        return await PromptSmokeTest.RunAsync(
            promptService, dataService,
            loggerFactory.CreateLogger("SmokeTest"));
    }

    // 'run <photoPath> [years...]' — the mode keyword is optional for now
    if (args.Length >= 1 && args[0] == "run")
        args = args.Skip(1).ToArray();

    var photoPath = ResolvePhotoPath(args, projectRoot);
    var years     = args.Length >= 2
        ? args.Skip(1).Select(int.Parse).ToList()
        : new List<int> { 1975,1985,1995,2005,2015,2025 };

    try
    {
        var pipeline = container.Resolve<Pipeline>();
        return await pipeline.RunAsync(photoPath, years);
    }
    catch (Exception ex)
    {
        var logger = loggerFactory.CreateLogger("Program");
        logger.LogError(ex, "Pipeline failed");
        return 1;
    }
}

static string ResolvePhotoPath(string[] args, string projectRoot)
{
    if (args.Length >= 1)
        return args[0];

    var testImageDir = Path.Combine(projectRoot, "testImage");
    if (Directory.Exists(testImageDir))
    {
        var first = Directory.EnumerateFiles(testImageDir, "*.jpg")
            .Concat(Directory.EnumerateFiles(testImageDir, "*.jpeg"))
            .Concat(Directory.EnumerateFiles(testImageDir, "*.png"))
            .FirstOrDefault();

        if (first is not null)
            return first;
    }

    throw new InvalidOperationException($"No photo path provided and no images found in {testImageDir}");
}

static string FindProjectRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (dir.GetFiles("*.csproj").Length > 0)
            return dir.FullName;
        dir = dir.Parent;
    }
    throw new InvalidOperationException("Could not locate project root: no .csproj file found walking up from " + AppContext.BaseDirectory);
}

return await RunAsync(args, projectRoot);
