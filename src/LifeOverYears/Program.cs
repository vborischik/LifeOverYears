using Autofac;
using LifeOverYears;
using LifeOverYears.Providers;
using LifeOverYears.Services;
using LifeOverYears.Services.Interfaces; // TODO: remove smoke test
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

// Captured before SetCurrentDirectory so CLI folder arguments (assemble,
// collect) resolve against the directory the user launched from, not the
// project root.
var launchDir = Environment.CurrentDirectory;
var projectRoot = FindProjectRoot();
Directory.SetCurrentDirectory(projectRoot);

static async Task<int> RunAsync(string[] args, string projectRoot, string launchDir)
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
        var overlayService = new YearOverlayService(videoLoggerFactory.CreateLogger<YearOverlayService>());
        return await VideoSmokeTest.RunAsync(
            videoService, overlayService, videoLoggerFactory.CreateLogger("VideoSmokeTest"), logCapture);
    }

    // TODO: remove smoke test
    // Fully isolated: no appsettings, no DI container. Covers PipelineFolders
    // config binding plus the real ResolvePhotoPath/MoveProcessedPhoto local
    // functions below, reached via reflection.
    if (args.Contains("--smoke-folders"))
    {
        using var folderLoggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        return await FolderSmokeTest.RunAsync(folderLoggerFactory.CreateLogger("FolderSmokeTest"));
    }

    // 'assemble <folderPath> [years...]' — manual testing only: no vision, no
    // prompts, no image provider call. Points overlay+assembly at images that
    // are already sitting in {folderPath}/images/. Isolated like --smoke-video:
    // no appsettings, no DI container.
    if (args.Length >= 1 && args[0] == "assemble")
        return await RunAssembleAsync(args.Skip(1).ToArray(), launchDir);

    // TODO: remove smoke test
    bool isSmokeTest = args.Contains("--smoke-prompts");

    var configBuilder = new ConfigurationBuilder()
        .SetBasePath(projectRoot)
        .AddJsonFile("appsettings.json", optional: isSmokeTest, reloadOnChange: false);

    // TODO: remove smoke test
    if (isSmokeTest)
        configBuilder.AddInMemoryCollection(new[]
        {
            new KeyValuePair<string, string?>("Nvidia:ApiKey", "smoke-test-dummy"),
            new KeyValuePair<string, string?>("OpenAi:ApiKey", "smoke-test-dummy")
        });

    var configuration = configBuilder.Build();
    var folders = PipelineFolders.Resolve(configuration);

    using var loggerFactory = LoggerFactory.Create(b => b.AddConsole().AddProvider(RunLogProvider.Instance)
                                                          .SetMinimumLevel(LogLevel.Debug));

    var builder = new ContainerBuilder();
    builder.RegisterModule(new AppModule(configuration, loggerFactory));
    await using var container = builder.Build();

    // TODO: remove smoke test
    if (isSmokeTest)
    {
        var promptService = container.Resolve<IPromptService>();
        var dataService   = container.Resolve<IDataService>();
        var promptResult = await PromptSmokeTest.RunAsync(
            promptService, dataService,
            loggerFactory.CreateLogger("SmokeTest"));
        var folderResult = await FolderSmokeTest.RunAsync(loggerFactory.CreateLogger("SmokeTest"));
        return promptResult == 0 && folderResult == 0 ? 0 : 1;
    }

    // 'collect <runFolder> [--wait]' — fetches finished generation jobs into
    // images/, then assembles the video. Needs the DI container: the real
    // provider requires configuration.
    if (args.Length >= 1 && args[0] == "collect")
        return await RunCollectAsync(args.Skip(1).ToArray(), launchDir, container, loggerFactory);

    // 'run <photoPath> [years...]' — the mode keyword is optional for now
    if (args.Length >= 1 && args[0] == "run")
        args = args.Skip(1).ToArray();

    var photoPath = ResolvePhotoPath(args, projectRoot, folders.InputDir);
    var years     = args.Length >= 2
        ? args.Skip(1).Select(int.Parse).ToList()
        : new List<int> { 1975,1985,1995,2005,2015,2025 };

    try
    {
        var pipeline = container.Resolve<Pipeline>();
        var result = await pipeline.RunAsync(photoPath, years);

        // Retire the source photo either way so the input folder does not
        // accumulate already-processed (or already-failed) images across
        // runs. photoPath was read relative to projectRoot (RunAsync runs
        // after SetCurrentDirectory), so the move must resolve against
        // projectRoot too — not launchDir.
        var destDir = result == 0 ? folders.ProcessedDir : folders.FailedDir;
        MoveProcessedPhoto(photoPath, projectRoot, destDir, loggerFactory.CreateLogger("Program"));

        return result;
    }
    catch (Exception ex)
    {
        var logger = loggerFactory.CreateLogger("Program");
        logger.LogError(ex, "Pipeline failed");
        MoveProcessedPhoto(photoPath, projectRoot, folders.FailedDir, logger);
        return 1;
    }
    finally
    {
        // A run that dies before RunService ever creates the run folder (bad
        // photo path, vision failure, config error) would otherwise lose its
        // whole buffered log — this is the fallback destination for exactly
        // that case. No-op once a real run.log exists.
        RunLogProvider.FlushIfUnattached();
    }
}

static async Task<int> RunAssembleAsync(string[] args, string launchDir)
{
    if (args.Length < 1)
        throw new InvalidOperationException("assemble requires a folder path: assemble <folderPath> [years...]");

    // Relative folder arguments are relative to where the user launched from,
    // not the project root the process chdir'd into.
    var folderPath = Path.GetFullPath(args[0], launchDir);

    using var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
    var logger = loggerFactory.CreateLogger("Assemble");

    // assemble targets a pre-existing folder — never create it.
    if (!Directory.Exists(folderPath))
    {
        logger.LogError("assemble: folder does not exist: {Folder}", folderPath);
        return 1;
    }

    // Default six standard years applies ONLY at this CLI boundary — once
    // parsed, `years` is threaded through unchanged to the overlay and video
    // assembly, exactly the list requested (or exactly the default).
    var years = args.Length >= 2
        ? args.Skip(1).Select(int.Parse).ToList()
        : new List<int> { 1975, 1985, 1995, 2005, 2015, 2025 };

    var imagesDir  = Path.Combine(folderPath, "images");
    var stampedDir = Path.Combine(folderPath, "stamped");
    var videoDir   = Path.Combine(folderPath, "video");
    Directory.CreateDirectory(stampedDir);
    Directory.CreateDirectory(videoDir);

    var ffmpegProvider = new FfmpegProvider(loggerFactory.CreateLogger<FfmpegProvider>());
    var videoService   = new VideoService(ffmpegProvider, loggerFactory.CreateLogger<VideoService>());
    var overlayService = new YearOverlayService(loggerFactory.CreateLogger<YearOverlayService>());

    logger.LogInformation("Assemble: folder={Folder} years={Years}", folderPath, string.Join(", ", years));

    var (missing, video) = await VideoAssemblyRunner.RunAsync(
        overlayService, videoService, imagesDir, stampedDir,
        Path.Combine(videoDir, "timeline.mp4"), years, logger);

    if (missing.Count > 0)
    {
        logger.LogError("assemble: missing images for years {Years} — see {ImagesDir}",
            string.Join(", ", missing), imagesDir);
        return 1;
    }

    if (video is null)
    {
        logger.LogWarning("assemble finished without video (assembly skipped)");
        return 1;
    }

    logger.LogInformation("assemble complete — video: {Path}", video.FilePath);
    return 0;
}

static async Task<int> RunCollectAsync(
    string[] args, string launchDir, Autofac.IContainer container, ILoggerFactory loggerFactory)
{
    if (args.Length < 1)
        throw new InvalidOperationException("collect requires a run folder: collect <runFolder> [--wait]");

    var logger = loggerFactory.CreateLogger("Collect");
    var wait   = args.Contains("--wait");
    var folder = Path.GetFullPath(args[0], launchDir);

    if (!Directory.Exists(folder))
    {
        logger.LogError("collect: run folder does not exist: {Folder}", folder);
        return 1;
    }

    // Years come from the run manifest; the default six only cover manifests
    // predating run.json.
    var manifestPath = Path.Combine(folder, "run.json");
    IReadOnlyList<int> years;
    if (File.Exists(manifestPath))
    {
        var manifest = System.Text.Json.JsonSerializer.Deserialize<LifeOverYears.Models.RunManifest>(
            await File.ReadAllTextAsync(manifestPath),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException($"collect: could not parse {manifestPath}");
        years = manifest.Years;
    }
    else
    {
        years = new List<int> { 1975, 1985, 1995, 2005, 2015, 2025 };
        logger.LogWarning("collect: no run.json in {Folder} — assuming default years {Years}",
            folder, string.Join(", ", years));
    }

    var imagesDir = Path.Combine(folder, "images");
    var jobsDir   = Path.Combine(folder, "jobs");
    var provider  = container.Resolve<IImageGenerationProvider>();

    // State is entirely on disk (jobs/ + images/), so Ctrl+C at any point is
    // safe — rerunning collect resumes where it left off.
    while (true)
    {
        var pending = new List<int>();
        foreach (var year in years)
        {
            // A hand-corrected "{year}-clean.png" counts as delivered too, so
            // collect does not keep chasing a year that is already satisfied.
            if (VideoAssemblyRunner.FindEraImage(imagesDir, year) is not null)
                continue;
            var outputPath = Path.Combine(imagesDir, $"{year}.png");

            if (await provider.TryCollectAsync(jobsDir, year, outputPath))
                logger.LogInformation("Collected {Year}", year);
            else
            {
                logger.LogInformation("Pending {Year}", year);
                pending.Add(year);
            }
        }

        if (pending.Count == 0)
            break;

        if (!wait)
        {
            logger.LogInformation(
                "collect: {Pending} of {Total} years still pending ({Years}) — rerun with --wait to poll",
                pending.Count, years.Count, string.Join(", ", pending));
            return 2;
        }

        logger.LogInformation("collect: waiting 60s for {Count} pending years ({Years})",
            pending.Count, string.Join(", ", pending));
        await Task.Delay(TimeSpan.FromSeconds(60));
    }

    logger.LogInformation("collect: all {Count} era images present — assembling video", years.Count);

    var (missing, video) = await VideoAssemblyRunner.RunAsync(
        container.Resolve<IYearOverlayService>(),
        container.Resolve<IVideoService>(),
        imagesDir,
        Path.Combine(folder, "stamped"),
        Path.Combine(folder, "video", "timeline.mp4"),
        years, logger);

    if (missing.Count > 0 || video is null)
        return 1;

    logger.LogInformation("collect complete — video: {Path}", video.FilePath);
    return 0;
}

static void MoveProcessedPhoto(string photoPath, string projectRoot, string destDir, ILogger logger)
{
    try
    {
        var sourceFull = Path.GetFullPath(photoPath, projectRoot);
        if (!File.Exists(sourceFull))
        {
            logger.LogWarning("Processed-move skipped — source no longer exists: {Source}", sourceFull);
            return;
        }

        var destDirFull = Path.Combine(projectRoot, destDir);
        Directory.CreateDirectory(destDirFull);

        var fileName = Path.GetFileName(sourceFull);
        var destFull = Path.Combine(destDirFull, fileName);

        // Name collision → append a timestamp rather than overwrite, so no
        // previously processed source is ever lost.
        if (File.Exists(destFull))
        {
            var stem = Path.GetFileNameWithoutExtension(fileName);
            var ext  = Path.GetExtension(fileName);
            fileName = $"{stem}_{DateTimeOffset.Now:yyyyMMdd-HHmmss}{ext}";
            destFull = Path.Combine(destDirFull, fileName);
        }

        File.Move(sourceFull, destFull);
        logger.LogInformation("Source photo retired to {Dest}", destFull);
    }
    catch (Exception ex)
    {
        // A failed move must never fail the run — the video is already done.
        logger.LogWarning(ex, "Processed-move failed for {Source} — leaving source in place", photoPath);
    }
}

static string ResolvePhotoPath(string[] args, string projectRoot, string inputDir)
{
    if (args.Length >= 1)
        return args[0];

    var inputDirFull = Path.Combine(projectRoot, inputDir);
    if (Directory.Exists(inputDirFull))
    {
        var first = Directory.EnumerateFiles(inputDirFull, "*.jpg")
            .Concat(Directory.EnumerateFiles(inputDirFull, "*.jpeg"))
            .Concat(Directory.EnumerateFiles(inputDirFull, "*.png"))
            .FirstOrDefault();

        if (first is not null)
            return first;
    }

    throw new InvalidOperationException($"No photo path provided and no images found in {inputDirFull}");
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

return await RunAsync(args, projectRoot, launchDir);
