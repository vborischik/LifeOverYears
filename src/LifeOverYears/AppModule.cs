using Autofac;
using LifeOverYears.Providers;
using LifeOverYears.Services;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LifeOverYears;

public sealed class AppModule : Module
{
    private readonly IConfiguration _configuration;
    private readonly ILoggerFactory _loggerFactory;

    public AppModule(IConfiguration configuration, ILoggerFactory loggerFactory)
    {
        _configuration = configuration;
        _loggerFactory = loggerFactory;
    }

    // Connections are pooled indefinitely by default. OpenAI and NVIDIA drop
    // idle ones server-side, so a long run picks a dead socket out of the
    // pool and fails mid-upload with "Connection reset by peer". Capping
    // lifetime and idle time forces a fresh connection instead.
    private static HttpClient BuildHttpClient() =>
        new(new SocketsHttpHandler
        {
            PooledConnectionLifetime    = TimeSpan.FromMinutes(2),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(20),
            ConnectTimeout              = TimeSpan.FromSeconds(30),
        });

    protected override void Load(ContainerBuilder builder)
    {
        var nvidiaKey = _configuration["Nvidia:ApiKey"]
            ?? throw new InvalidOperationException("Nvidia:ApiKey is not configured in appsettings.json");

        // The standalone 'collect' mode needs Pipeline:EraChaining to know
        // whether a resubmitted era chains from the previous image or from the
        // shared base — it has no Pipeline instance to ask.
        builder.RegisterInstance(_configuration).As<IConfiguration>().SingleInstance();

        builder.RegisterInstance(new FileSystemProvider(_loggerFactory.CreateLogger<FileSystemProvider>()))
               .As<IFileSystemProvider>().SingleInstance();

        builder.RegisterInstance(new JsonProvider())
               .As<IJsonProvider>().SingleInstance();

        builder.Register(_ => new DataService(
                    _.Resolve<IFileSystemProvider>(),
                    _.Resolve<IJsonProvider>(),
                    _loggerFactory.CreateLogger<DataService>()))
               .As<IDataService>().SingleInstance();

        builder.RegisterInstance(new NvidiaProvider(BuildHttpClient(), nvidiaKey, _loggerFactory.CreateLogger<NvidiaProvider>()))
               .As<INvidiaProvider>().SingleInstance();

        builder.Register(_ => new VisionProvider(_.Resolve<INvidiaProvider>(), _loggerFactory.CreateLogger<VisionProvider>()))
               .As<IVisionProvider>().SingleInstance();

        builder.Register(_ => new VisionService(_.Resolve<IVisionProvider>(), _.Resolve<IDataService>(), _loggerFactory.CreateLogger<VisionService>()))
               .As<IVisionService>().SingleInstance();

        // CaptionProvider (the LLM path) is deliberately unregistered: captions are
        // now assembled locally from data/captions/{sceneType}.txt. The provider and
        // its interface remain in the repo, unwired, so the LLM path can be restored.
        builder.Register(_ => new CaptionService(_.Resolve<IDataService>(), _loggerFactory.CreateLogger<CaptionService>()))
               .As<ICaptionService>().SingleInstance();

        builder.Register(_ => new PromptService(_.Resolve<IDataService>(), _loggerFactory.CreateLogger<PromptService>()))
               .As<IPromptService>().SingleInstance();

        // The brand-series path: no IDataService dependency because it reads no
        // template and no era file — the series JSON is the whole input.
        builder.Register(_ => new BrandSeriesPromptService(_loggerFactory.CreateLogger<BrandSeriesPromptService>()))
               .As<IBrandSeriesPromptService>().SingleInstance();

        var folders = PipelineFolders.Resolve(_configuration);
        builder.RegisterInstance(new RunService(folders.OutputDir, _loggerFactory.CreateLogger<RunService>()))
               .As<IRunService>().SingleInstance();

        var imagesEnabled = _configuration.GetValue("OpenAi:Enabled", true);
        var imagesMode = _configuration["OpenAi:Mode"] ?? "sync";

        var baseMode = _configuration["Pipeline:BaseMode"] ?? "clean";
        if (!string.Equals(baseMode, "clean", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(baseMode, "synthetic", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Pipeline:BaseMode must be 'clean' or 'synthetic', got '{baseMode}'");
        }

        // Chained eras are generated one at a time, so under batch mode each era
        // gets its own batch and waits out its own completion window rather than
        // riding along in one combined submission. Supported in both modes; the
        // cost is latency, not correctness — a chained batch run is as slow as
        // the sum of its windows.
        var eraChaining = _configuration.GetValue("Pipeline:EraChaining", true);

        // Writes {runFolder}/short-prompts/ during a normal run, so the
        // hand-usable copies exist without having to come back with the
        // 'short-prompts' CLI mode later.
        var shortPrompts = _configuration.GetValue("Pipeline:ShortPrompts", false);

        _loggerFactory.CreateLogger<AppModule>().LogInformation(
            "Image generation provider: enabled={Enabled}, mode={Mode}, baseMode={BaseMode}, eraChaining={EraChaining}, shortPrompts={ShortPrompts}",
            imagesEnabled, imagesMode, baseMode, eraChaining, shortPrompts);

        if (!imagesEnabled)
        {
            // Service off: fall back to the stub — jobs are recorded and the
            // pipeline waits on the run folder for manually placed images.
            builder.RegisterInstance(new StubImageProvider(_loggerFactory.CreateLogger<StubImageProvider>()))
                   .As<IImageGenerationProvider>().SingleInstance();
        }
        else
        {
            var openAiKey = _configuration["OpenAi:ApiKey"]
                ?? throw new InvalidOperationException(
                    "OpenAi:ApiKey is not configured in appsettings.json (set OpenAi:Enabled to false to run without it)");

            builder.RegisterInstance(new OpenAiProvider(BuildHttpClient(), openAiKey, _loggerFactory.CreateLogger<OpenAiProvider>()))
                   .As<IOpenAiProvider>().SingleInstance();

            if (string.Equals(imagesMode, "batch", StringComparison.OrdinalIgnoreCase))
            {
                // Escape hatch for OpenAI's batch file-resolution outage: send
                // the image inline instead of by id. See the flag's comment on
                // the provider.
                var inlineImage = _configuration.GetValue("OpenAi:BatchInlineImage", false);
                if (inlineImage)
                    _loggerFactory.CreateLogger<AppModule>().LogInformation(
                        "Batch mode: inlining the base image as base64 instead of uploading it (OpenAi:BatchInlineImage)");

                builder.Register(_ => new OpenAiBatchImageProvider(
                            _.Resolve<IOpenAiProvider>(),
                            _loggerFactory.CreateLogger<OpenAiBatchImageProvider>(),
                            inlineImage))
                       .As<IImageGenerationProvider>().SingleInstance();
            }
            else if (string.Equals(imagesMode, "sync", StringComparison.OrdinalIgnoreCase))
            {
                builder.Register(_ => new OpenAiImageProvider(
                            _.Resolve<IOpenAiProvider>(),
                            _loggerFactory.CreateLogger<OpenAiImageProvider>()))
                       .As<IImageGenerationProvider>().SingleInstance();
            }
            else
            {
                throw new InvalidOperationException(
                    $"OpenAi:Mode must be 'sync' or 'batch', got '{imagesMode}'");
            }
        }

        builder.RegisterInstance(new YearOverlayService(_loggerFactory.CreateLogger<YearOverlayService>()))
               .As<IYearOverlayService>().SingleInstance();

        builder.RegisterInstance(new FfmpegProvider(_loggerFactory.CreateLogger<FfmpegProvider>()))
               .As<IFfmpegProvider>().SingleInstance();

        builder.Register(_ => new VideoService(_.Resolve<IFfmpegProvider>(), _loggerFactory.CreateLogger<VideoService>()))
               .As<IVideoService>().SingleInstance();

        builder.Register(_ => new Pipeline(
                    _.Resolve<IVisionService>(),
                    _.Resolve<IPromptService>(),
                    _.Resolve<IDataService>(),
                    _.Resolve<IRunService>(),
                    _.Resolve<IImageGenerationProvider>(),
                    _.Resolve<IYearOverlayService>(),
                    _.Resolve<IVideoService>(),
                    _.Resolve<ICaptionService>(),
                    baseMode,
                    eraChaining,
                    shortPrompts,
                    _loggerFactory.CreateLogger<Pipeline>()))
               .SingleInstance();

        // Pipeline's sibling for the brand mode. Registered unconditionally and
        // resolved only by the 'brand' CLI mode — it costs nothing until then,
        // and Pipeline:BaseMode does not apply to it, since a series has no
        // photograph to clean and always draws its base from text.
        builder.Register(_ => new BrandSeriesRunner(
                    _.Resolve<IBrandSeriesPromptService>(),
                    _.Resolve<IDataService>(),
                    _.Resolve<IRunService>(),
                    _.Resolve<IImageGenerationProvider>(),
                    _.Resolve<IYearOverlayService>(),
                    _.Resolve<IVideoService>(),
                    _.Resolve<ICaptionService>(),
                    shortPrompts,
                    _loggerFactory.CreateLogger<BrandSeriesRunner>()))
               .SingleInstance();
    }
}
