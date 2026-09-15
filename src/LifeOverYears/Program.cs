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

    // TODO: remove smoke test
    // Fully isolated: no appsettings, no DI container, no network. Drives the
    // real VisionProvider against a fake INvidiaProvider — covers reading the
    // model's answer, not the accuracy of what it saw.
    if (args.Contains("--smoke-vision"))
    {
        using var visionLoggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        return await VisionSmokeTest.RunAsync(visionLoggerFactory, visionLoggerFactory.CreateLogger("VisionSmokeTest"));
    }

    // TODO: remove smoke test
    // Fully isolated: no appsettings, no DI container, no network. Drives the
    // real OpenAiBatchImageProvider against a fake IOpenAiProvider.
    if (args.Contains("--smoke-batch"))
    {
        using var batchLoggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        return await BatchSmokeTest.RunAsync(batchLoggerFactory, batchLoggerFactory.CreateLogger("BatchSmokeTest"));
    }

    // 'short-prompts <runFolder>' — rewrites a finished run's prompts into a
    // shorter, hand-usable form under {runFolder}/short-prompts/. Reads and
    // writes files only: no vision, no prompts rebuilt, no API, no cost, and
    // nothing in the normal pipeline is touched. Isolated like 'assemble'.
    if (args.Length >= 1 && args[0] == "short-prompts")
    {
        using var shortLoggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
        return await ShortPromptWriter.RunAsync(
            args.Skip(1).ToArray(), launchDir, shortLoggerFactory.CreateLogger("ShortPrompts"));
    }

    // Publishing. Both modes build a container from PublishModule alone —
    // AppModule refuses to load without the generation keys, and putting a
    // finished run in front of people needs none of them.
    //
    //   publish <runFolder> [--yes]   one run, now. Without --yes it is queued
    //                                 for review; with it, published directly —
    //                                 the mode for testing a platform on an
    //                                 existing run before the loop is trusted.
    //                                 --again lets --yes republish a decided run.
    //                                 --storage-only does music + Dropbox and
    //                                 prints the URL; posts nothing.
    //                                 --targets a,b limits a --yes run to those
    //                                 platforms; the record merges, not replaces.
    //   review                        the loop: send each queued run to the
    //                                 reviewer, act on the answer, next.
    if (args.Length >= 1 && (args[0] == "publish" || args[0] == "review"))
        return await RunPublishModeAsync(args, launchDir, projectRoot);

    if (args.Contains("--smoke-publish"))
    {
        using var publishLoggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        return await PublishSmokeTest.RunAsync(publishLoggerFactory, publishLoggerFactory.CreateLogger("PublishSmokeTest"));
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
        var brandService  = container.Resolve<IBrandSeriesPromptService>();
        var dataService   = container.Resolve<IDataService>();
        var promptResult = await PromptSmokeTest.RunAsync(
            promptService, brandService, dataService, container.Resolve<ICaptionService>(),
            loggerFactory.CreateLogger("SmokeTest"));
        var folderResult = await FolderSmokeTest.RunAsync(loggerFactory.CreateLogger("SmokeTest"));
        return promptResult == 0 && folderResult == 0 ? 0 : 1;
    }

    // 'vision-variance <folder> [--repeat N]' — diagnostic only: runs vision over
    // a folder and reports which SceneDna fields fail to vary between photos.
    if (args.Length >= 1 && args[0] == "vision-variance")
        return await VisionVarianceTest.RunAsync(
            args.Skip(1).ToArray(), launchDir, container, loggerFactory);

    // 'vision-accuracy <folder> [--no-double-check]' — grades Vision against an
    // expected.json beside the photos. Diagnostic, like vision-variance: needs
    // the network, and it measures a model rather than the code.
    if (args.Length >= 1 && args[0] == "vision-accuracy")
        return await VisionAccuracyTest.RunAsync(
            args.Skip(1).ToArray(), launchDir, container, loggerFactory);

    // 'collect <runFolder> [--wait]' — fetches finished generation jobs into
    // images/, then assembles the video. Needs the DI container: the real
    // provider requires configuration.
    if (args.Length >= 1 && args[0] == "collect")
        return await RunCollectAsync(args.Skip(1).ToArray(), launchDir, container, loggerFactory);

    // 'brand <name> [years...]' — the second generation path: the scene comes
    // from data/brands/series/{name}.json instead of a photograph, so Vision is
    // never called and no image is ever uploaded as a source. Everything after
    // the prompt is the ordinary run folder / chaining / overlay / assembly path.
    if (args.Length >= 1 && args[0] == "brand")
        return await RunBrandAsync(args.Skip(1).ToArray(), container, loggerFactory);

    // 'run <photoPath> [years...]'. The keyword used to be optional, which
    // meant any mistyped mode — a flag first, a missing "publish" — fell
    // through to here and died parsing a folder name as a year. A bare
    // photo path still works when it is a file that exists; anything else
    // that is not a known mode is refused with the list.
    if (args.Length >= 1 && args[0] == "run")
        args = args.Skip(1).ToArray();
    else if (args.Length >= 1 && !File.Exists(Path.GetFullPath(args[0], launchDir)))
    {
        Console.Error.WriteLine(
            $"Unknown mode '{args[0]}'. Modes: run [photo] [years...] | brand <name> [years...] | " +
            "collect <runFolder> [--wait] | assemble <runFolder> [years...] | publish <runFolder> [--yes] [--again] [--storage-only] | " +
            "review | short-prompts <runFolder> | vision-variance <folder> | vision-accuracy <folder> | " +
            "--smoke-prompts | --smoke-video | --smoke-batch | --smoke-vision | --smoke-publish | --smoke-folders");
        return 2;
    }

    // Years are the only numeric arguments; a non-number here is a mistyped
    // command, not a format exception to read a stack trace for.
    foreach (var y in args.Skip(1))
        if (!int.TryParse(y, out _))
        {
            Console.Error.WriteLine($"run: '{y}' is not a year. Usage: run [photo] [years...]");
            return 2;
        }

    // Every photo in the input folder, not just the first one it happened to
    // enumerate. Dropping five in and getting one back — with the other four
    // silently left for a later invocation nobody remembered to make — was the
    // old behaviour. An explicit path on the command line still means that one
    // photo, so `run <photo> [years...]` is unchanged.
    var photoPaths = ResolvePhotoPaths(args, projectRoot, folders.InputDir);
    var years      = args.Length >= 2
        ? args.Skip(1).Select(int.Parse).ToList()
        : new List<int> { 1975,1985,1995,2005,2015,2025 };

    var batchLogger = loggerFactory.CreateLogger("Program");
    if (photoPaths.Count > 1)
        batchLogger.LogInformation("Batch: {Count} photos to process — {Names}",
            photoPaths.Count, string.Join(", ", photoPaths.Select(Path.GetFileName)));

    var failed = new List<string>();
    for (var i = 0; i < photoPaths.Count; i++)
    {
        var photoPath = photoPaths[i];

        // Each photo is its own run with its own folder and run.log, so the log
        // provider has to start over — otherwise every photo after the first
        // appends into the first one's file.
        RunLogProvider.BeginRun();

        if (photoPaths.Count > 1)
            batchLogger.LogInformation("=== Photo {Index}/{Total}: {Name} ===",
                i + 1, photoPaths.Count, Path.GetFileName(photoPath));

        try
        {
            var pipeline = container.Resolve<Pipeline>();
            var result = await pipeline.RunAsync(photoPath, years);
            if (result != 0)
                failed.Add(Path.GetFileName(photoPath));

            // Retire the source photo either way so the input folder does not
            // accumulate already-processed (or already-failed) images across
            // runs. photoPath was read relative to projectRoot (RunAsync runs
            // after SetCurrentDirectory), so the move must resolve against
            // projectRoot too — not launchDir.
            var destDir = result == 0 ? folders.ProcessedDir : folders.FailedDir;
            MoveProcessedPhoto(photoPath, projectRoot, destDir, batchLogger);
        }
        catch (Exception ex)
        {
            // One bad photo does not end the batch — that is the whole point of
            // handing over a folder. It is retired to failed/ and the next one
            // starts clean.
            batchLogger.LogError(ex, "Pipeline failed for {Name}", Path.GetFileName(photoPath));
            failed.Add(Path.GetFileName(photoPath));
            MoveProcessedPhoto(photoPath, projectRoot, folders.FailedDir, batchLogger);
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

    if (photoPaths.Count > 1)
        batchLogger.LogInformation("Batch complete — {Ok}/{Total} succeeded{Failed}",
            photoPaths.Count - failed.Count, photoPaths.Count,
            failed.Count == 0 ? "" : $", failed: {string.Join(", ", failed)}");

    return failed.Count == 0 ? 0 : 1;
}

static async Task<int> RunBrandAsync(
    string[] args, Autofac.IContainer container, ILoggerFactory loggerFactory)
{
    var logger = loggerFactory.CreateLogger("Brand");
    if (args.Length < 1)
    {
        logger.LogError("brand requires a series name: brand <name> [years...]");
        return 1;
    }

    // No default year list here, unlike every other mode: the series file
    // carries its own years, and defaulting to the standard six would invent
    // eras a brand may not have.
    var years = args.Skip(1).Select(int.Parse).ToList();

    try
    {
        return await container.Resolve<BrandSeriesRunner>().RunAsync(args[0], years);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Brand series run failed");
        return 1;
    }
    finally
    {
        // Same fallback as the photo run: a run that dies before the run folder
        // exists would otherwise lose its whole buffered log.
        RunLogProvider.FlushIfUnattached();
    }
}

static async Task<int> RunPublishModeAsync(string[] args, string launchDir, string projectRoot)
{
    using var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
    var logger = loggerFactory.CreateLogger(args[0] == "review" ? "Review" : "Publish");

    var configuration = new ConfigurationBuilder()
        .SetBasePath(projectRoot)
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
        .Build();
    var folders = PipelineFolders.Resolve(configuration);
    var enabled = configuration.GetValue("Publish:Enabled", false);

    // on-review sits beside the runs folder: output/runs → output/on-review.
    var outputRoot = Path.GetDirectoryName(Path.GetFullPath(folders.OutputDir, projectRoot))!;

    var builder = new ContainerBuilder();
    builder.RegisterModule(new PublishModule(configuration, loggerFactory, outputRoot));
    await using var container = builder.Build();
    var queue = container.Resolve<ReviewQueue>();

    // A misconfigured Publish: section is the likeliest failure here, and it
    // surfaces as a resolution exception three frames deep. The message the
    // service wrote is the one worth reading.
    T ResolveOrExplain<T>() where T : notnull
    {
        try { return container.Resolve<T>(); }
        catch (Autofac.Core.DependencyResolutionException ex)
        {
            var root = ex.GetBaseException();
            logger.LogError("publish is not configured: {Message}", root.Message);
            throw new PublishConfigurationException(root.Message, root);
        }
    }

    if (args[0] == "review")
    {
        // No ReviewChatId yet: this is first contact, not the loop. Listen for
        // one message to the bot, print the chat id to put in config, stop.
        var botToken     = configuration["Publish:Telegram:BotToken"];
        var reviewChatId = configuration["Publish:Telegram:ReviewChatId"];
        if (string.IsNullOrWhiteSpace(botToken))
        {
            logger.LogError("Publish:Telegram:BotToken is empty — create the bot with @BotFather and paste its token");
            return 1;
        }
        if (string.IsNullOrWhiteSpace(reviewChatId))
        {
            logger.LogInformation("Publish:Telegram:ReviewChatId is empty. Open your bot in Telegram and send it any message — waiting up to 2 minutes…");
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(40) };
            var id = await TelegramReviewProvider.DiscoverChatIdAsync(http, botToken, TimeSpan.FromMinutes(2), logger);
            if (id is null)
            {
                logger.LogError("No message arrived. Send the bot a message and run 'review' again.");
                return 1;
            }
            logger.LogInformation("Your chat id is {Id}. Put it in appsettings.json as Publish:Telegram:ReviewChatId and run 'review' again.", id);
            return 0;
        }

        // The loop publishes on the reviewer's word, so it is the thing the
        // flag gates. Off until a platform has been proven on a one-shot
        // publish — that is what --yes below exists for.
        if (!enabled)
        {
            logger.LogError("Publish:Enabled is false — the review loop stays off. Test a single run with " +
                            "'publish <runFolder> --yes' first, then enable it.");
            return 1;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        logger.LogInformation("Review loop started — Ctrl+C to stop. Queue: {Root}", queue.Root);
        ReviewLoop loop;
        try { loop = ResolveOrExplain<ReviewLoop>(); } catch (PublishConfigurationException) { return 1; }
        try
        {
            var done = await loop.RunAsync(cts.Token);
            logger.LogInformation("Review loop stopped after {Count} decision(s)", done);
            return 0;
        }
        catch (AnotherReviewRunningException ex)
        {
            logger.LogError("{Message}", ex.Message);
            return 1;
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Review loop stopped");
            return 0;
        }
    }

    if (args.Length < 2)
    {
        logger.LogError("usage: publish <runFolder> [--yes]");
        return 1;
    }

    var runFolder = Path.GetFullPath(args[1], launchDir);
    if (!Directory.Exists(runFolder))
    {
        logger.LogError("publish: run folder does not exist: {Folder}", runFolder);
        return 1;
    }

    // --storage-only: music and the public-URL step through the real
    // providers, nothing posted, nothing recorded. The check to run before
    // a platform token exists — the URL it prints is what Instagram would be
    // handed, and can be fetched by hand.
    if (args.Contains("--storage-only"))
    {
        var privacyForStorage = configuration["Publish:Privacy"] ?? "private";
        var storageRequest = await RunPublishSource.ReadAsync(runFolder, privacyForStorage);
        IMusicService music; IPublicStorage storage;
        try
        {
            music   = ResolveOrExplain<IMusicService>();
            storage = container.ResolveOptional<IPublicStorage>()
                ?? throw new PublishConfigurationException("Publish:Dropbox has no RefreshToken or AccessToken — nothing to upload with", new InvalidOperationException());
        }
        catch (PublishConfigurationException) { return 1; }

        var family = music.FamilyOf("instagram");
        var (withMusic, track) = await music.WithMusicAsync(family, storageRequest);
        logger.LogInformation("Music: {Track} → {File}", track, withMusic.Video.FilePath);
        var url = await storage.UploadPublicAsync(withMusic.Video.FilePath, $"{storageRequest.Video.Id}.{family}.mp4");
        logger.LogInformation("Public URL: {Url}", url);
        logger.LogInformation("storage-only complete — nothing was posted and no publish.json was written");
        return 0;
    }

    if (!args.Contains("--yes"))
    {
        var item = await queue.EnqueueAsync(runFolder);
        logger.LogInformation(item is null
            ? "Not queued — the run already has a decision (publish.json)"
            : "Queued for review: {Root}/{Id}. Run 'review' to send it.", queue.Root, item?.Id);
        return 0;
    }

    // --yes: straight to the platforms, no Telegram in between. Explicit on
    // purpose — the folder and the flag were both typed by a person. A run
    // that already went through — published or skipped — is refused unless
    // --again is typed too: without this a test command re-uploaded a video
    // that was already on the channel, and a private duplicate on YouTube is
    // the cheap version of that mistake.
    if (RunPublishSource.HasDecision(runFolder) && !args.Contains("--again"))
    {
        logger.LogError("publish: {Folder} already has a decision in publish.json — add --again to publish it once more", runFolder);
        return 1;
    }

    var privacy = configuration["Publish:Privacy"] ?? "private";
    var request = await RunPublishSource.ReadAsync(runFolder, privacy);
    IPublishService service;
    try { service = ResolveOrExplain<IPublishService>(); } catch (PublishConfigurationException) { return 1; }
    logger.LogInformation("Publishing {Id} to {Targets} as {Privacy}",
        request.Video.Id, string.Join(", ", service.Targets), privacy);

    // --targets a,b narrows the run to those platforms — the re-publish
    // for the one that failed, without re-posting the ones that did not.
    IReadOnlyList<string>? only = null;
    var targetsIdx = Array.IndexOf(args, "--targets");
    if (targetsIdx >= 0 && targetsIdx + 1 < args.Length)
        only = args[targetsIdx + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    var state = await service.PublishAsync(request, only);

    // A re-publish merges into the record rather than replacing it: the
    // YouTube and Instagram links from the first pass must not be lost
    // because Facebook was run again on its own.
    var recordPath = Path.Combine(runFolder, RunPublishSource.PublishFileName);
    var jsonOpts   = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
    if (File.Exists(recordPath))
    {
        try
        {
            var previous = System.Text.Json.JsonSerializer.Deserialize<LifeOverYears.Models.PublishState>(
                await File.ReadAllTextAsync(recordPath), jsonOpts);
            if (previous is not null)
                state = MergePublishState(previous, state);
        }
        catch (System.Text.Json.JsonException) { /* an unreadable record is replaced */ }
    }
    await File.WriteAllTextAsync(recordPath, System.Text.Json.JsonSerializer.Serialize(state, jsonOpts));

    foreach (var p in state.Publications)
        logger.LogInformation("  {Platform}: {Url}", p.Platform, p.Url);
    if (state.Status != "published")
    {
        logger.LogError("publish: {Error}", state.Error);
        return 1;
    }
    logger.LogInformation("publish complete — recorded in {File}", RunPublishSource.PublishFileName);
    return 0;
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

    // Only the key-free module: assemble re-cuts a finished run and must work
    // on a machine with no API keys, which AppModule refuses to load without.
    var assembleBuilder = new ContainerBuilder();
    assembleBuilder.RegisterModule(new VideoModule(loggerFactory));
    await using var assembleContainer = assembleBuilder.Build();
    var videoService   = assembleContainer.Resolve<IVideoService>();
    var overlayService = assembleContainer.Resolve<IYearOverlayService>();

    logger.LogInformation("Assemble: folder={Folder} years={Years}", folderPath, string.Join(", ", years));

    // Same order every other path composes in, whatever order the years were
    // typed in — so re-assembling a finished run reproduces its video rather
    // than quietly making a different one.
    var (missing, video) = await VideoAssemblyRunner.RunAsync(
        overlayService, videoService, imagesDir, stampedDir,
        Path.Combine(videoDir, "timeline.mp4"),
        VideoAssemblyRunner.NewestFirst(years), logger);

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

    var imagesDir  = Path.Combine(folder, "images");
    var jobsDir    = Path.Combine(folder, "jobs");
    var promptsDir = Path.Combine(folder, "prompts");
    var provider   = container.Resolve<IImageGenerationProvider>();

    // A brand run keeps its series file beside run.json. Without it collect
    // would resume one blind: it would resubmit eras with no logo reference —
    // the one input that stops the model inventing the sign — and it would give
    // up entirely on a missing first frame, because a brand run has no shared
    // base for that frame to be edited from.
    var seriesPath = Path.Combine(folder, "series.json");
    LifeOverYears.Models.BrandSeries? series = null;
    if (File.Exists(seriesPath))
    {
        series = System.Text.Json.JsonSerializer.Deserialize<LifeOverYears.Models.BrandSeries>(
            await File.ReadAllTextAsync(seriesPath),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        logger.LogInformation("collect: brand series run ({Brand})", series?.Brand);
    }

    // Never a choice for a brand run: with no shared base, era N+1 has only
    // era N to edit.
    var chained = series is not null
                  || container.Resolve<IConfiguration>().GetValue("Pipeline:EraChaining", true);

    // The shared base every non-chained era edits, and the starting point of a
    // chained run. Whichever mode produced this run, its file is already here —
    // except a brand run, which has none and opens on its first era instead.
    var sharedBase = new[] { "base_synthetic.png", "base_clean.png" }
        .Select(name => Path.Combine(folder, name))
        .FirstOrDefault(File.Exists);

    // State is entirely on disk (jobs/ + images/), so Ctrl+C at any point is
    // safe — rerunning collect resumes where it left off.
    while (true)
    {
        var pending = new List<int>();

        // Chained runs walk forward: each era edits the previous era's finished
        // image, so a year cannot be submitted until the one before it exists.
        var chainBase = sharedBase;

        foreach (var year in years)
        {
            // A hand-corrected "{year}-clean.png" counts as delivered too, so
            // collect does not keep chasing a year that is already satisfied.
            if (VideoAssemblyRunner.FindEraImage(imagesDir, year) is { } done)
            {
                chainBase = done;
                continue;
            }
            var outputPath = Path.Combine(imagesDir, $"{year}.png");

            // A year with no job state was never submitted — either the run died
            // before reaching it, or its job file was deleted because the batch
            // behind it was dead. Everything needed to submit it is on disk:
            // the prompt was written at build time and the base is either the
            // shared one or the previous era's image.
            var jobPath    = Path.Combine(jobsDir, $"{year}.json");
            var promptPath = Path.Combine(promptsDir, $"{year}.txt");
            var drawnNow   = false;
            if (!File.Exists(jobPath))
            {
                if (!File.Exists(promptPath))
                {
                    logger.LogError("collect: {Year} has no job state and no prompts/{Year}.txt to resubmit from", year, year);
                    return 1;
                }
                if (chainBase is null && series is null)
                {
                    logger.LogError("collect: {Year} needs resubmitting but the run has no base image", year);
                    return 1;
                }

                if (chainBase is null)
                {
                    // The series' opening frame. It was drawn from text the
                    // first time and is redrawn the same way — there is nothing
                    // for it to edit, and losing one frame must not cost the run.
                    logger.LogInformation(
                        "collect: {Year} is the series' first frame and is missing — redrawing it from text", year);
                    await provider.SynthesizeBaseAsync(await File.ReadAllTextAsync(promptPath), outputPath);
                    drawnNow = true;
                }
                else
                {
                    var reference = BrandLogoRef(series, year);
                    logger.LogInformation("collect: {Year} was never submitted — submitting from {Base}{Reference}",
                        year, Path.GetFileName(chainBase),
                        reference is null ? "" : $" with reference {reference}");
                    await provider.SubmitEraAsync(
                        chainBase, await File.ReadAllTextAsync(promptPath), year, jobsDir, reference);
                }
            }

            // A frame drawn from text is written synchronously and has no job to
            // poll, so it is judged by whether the file landed.
            if (drawnNow)
            {
                if (VideoAssemblyRunner.FindEraImage(imagesDir, year) is { } drawn)
                {
                    logger.LogInformation("Collected {Year}", year);
                    chainBase = drawn;
                    continue;
                }
                logger.LogInformation("Pending {Year}", year);
                pending.Add(year);
                if (chained) break;
                continue;
            }

            bool collected;
            try
            {
                collected = await provider.TryCollectAsync(jobsDir, year, outputPath);
            }
            catch (Exception ex)
            {
                // A dead job is not retried automatically: resubmitting costs
                // money and only the operator can tell a spent batch from a
                // transient fault. Say exactly what to delete to retry.
                logger.LogError(ex,
                    "collect: {Year} cannot be collected. If its job is spent, delete {JobPath} and rerun collect to submit it again",
                    year, jobPath);
                return 1;
            }

            if (collected)
            {
                logger.LogInformation("Collected {Year}", year);
                chainBase = VideoAssemblyRunner.FindEraImage(imagesDir, year) ?? outputPath;
                continue;
            }

            logger.LogInformation("Pending {Year}", year);
            pending.Add(year);

            // Nothing after this year can even be submitted until it lands.
            if (chained)
                break;
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

    // The manifest's order drives generation; the cut is the same newest-first
    // shape the pipeline uses. A batch run normally finishes here rather than in
    // Pipeline, so the two must not disagree about what the video looks like.
    var (missing, video) = await VideoAssemblyRunner.RunAsync(
        container.Resolve<IYearOverlayService>(),
        container.Resolve<IVideoService>(),
        imagesDir,
        Path.Combine(folder, "stamped"),
        Path.Combine(folder, "video", "timeline.mp4"),
        VideoAssemblyRunner.NewestFirst(years), logger);

    if (missing.Count > 0 || video is null)
        return 1;

    // The same step 5 the pipeline runs. A batch run normally finishes here
    // rather than inside Pipeline, so without this every resumed run ends up
    // with a video and no caption.
    var captionState = "NOT written";
    var narrative = await CaptionRunner.ReadNarrativeAsync(folder);
    var scene     = await CaptionRunner.ReadSceneAsync(folder);
    if (narrative is null || scene is null)
        logger.LogWarning(
            "collect: no {Missing} in {Folder} — caption skipped. Runs built before narrative.json existed cannot be captioned after the fact.",
            narrative is null ? "narrative.json" : "scene.json", folder);
    else if (await CaptionRunner.WriteAsync(
                 container.Resolve<ICaptionService>(), scene, narrative, folder, logger))
        captionState = "written";

    logger.LogInformation("collect complete — video: {Path}, caption.txt: {CaptionState}",
        video.FilePath, captionState);

    await container.Resolve<ReviewQueue>().TryEnqueueAfterRunAsync(folder);
    return 0;
}

// An era's logo sheet, as data/-relative as the series file writes it. Null for
// a photo run, and for the eras after the sign comes down.
static string? BrandLogoRef(LifeOverYears.Models.BrandSeries? series, int year)
{
    if (series is null || !series.Eras.TryGetValue(year.ToString(), out var era) || era.LogoRef is null)
        return null;
    return Path.Combine("data", era.LogoRef.Replace('/', Path.DirectorySeparatorChar));
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

// One explicit path, or everything in the input folder in name order.
//
// Extensions are matched case-insensitively rather than by three "*.jpg"-style
// patterns: those are case-sensitive on a case-sensitive filesystem, and the
// processed folder already holds files named ".JPG" that such a pattern would
// have walked straight past.
static IReadOnlyList<string> ResolvePhotoPaths(string[] args, string projectRoot, string inputDir)
{
    if (args.Length >= 1)
        return new[] { args[0] };

    var inputDirFull = Path.Combine(projectRoot, inputDir);
    string[] extensions = { ".jpg", ".jpeg", ".png" };

    if (Directory.Exists(inputDirFull))
    {
        // Ordered so a folder of photos is processed predictably, and so a
        // re-run after an interruption picks up where the eye expects.
        var photos = Directory.EnumerateFiles(inputDirFull)
            .Where(f => extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (photos.Count > 0)
            return photos;
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

// Newer publications replace older ones for the same platform; everything
// else carries over. Status is "published" only when nothing in the merged
// record is left failing.
static LifeOverYears.Models.PublishState MergePublishState(
    LifeOverYears.Models.PublishState previous, LifeOverYears.Models.PublishState latest)
{
    var byPlatform = previous.Publications.ToDictionary(p => p.Platform, StringComparer.OrdinalIgnoreCase);
    foreach (var p in latest.Publications) byPlatform[p.Platform] = p;
    var music = new Dictionary<string, string>(previous.Music ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
    foreach (var (k, v) in latest.Music ?? new Dictionary<string, string>()) music[k] = v;
    return new LifeOverYears.Models.PublishState(
        Status:       latest.Error is null ? "published" : "failed",
        DecidedAt:    latest.DecidedAt,
        Publications: byPlatform.Values.OrderBy(p => p.PublishedAt).ToList(),
        Error:        latest.Error,
        Music:        music.Count == 0 ? null : music);
}

// Thrown by the publish modes when the Publish: section cannot be turned into
// a working service; already logged with the cause by the time it is thrown.
sealed class PublishConfigurationException : Exception
{
    public PublishConfigurationException(string message, Exception inner) : base(message, inner) { }
}
