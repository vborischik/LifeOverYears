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

        // Chained eras must be generated one at a time, which removes the whole
        // point of batch mode (all years submitted at once, up to a 24h window
        // each). Off by default there; on elsewhere.
        var eraChaining = _configuration.GetValue("Pipeline:EraChaining", true);

        _loggerFactory.CreateLogger<AppModule>().LogInformation(
            "Image generation provider: enabled={Enabled}, mode={Mode}, baseMode={BaseMode}, eraChaining={EraChaining}",
            imagesEnabled, imagesMode, baseMode, eraChaining);

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
                builder.Register(_ => new OpenAiBatchImageProvider(
                            _.Resolve<IOpenAiProvider>(),
                            _loggerFactory.CreateLogger<OpenAiBatchImageProvider>()))
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
                    _loggerFactory.CreateLogger<Pipeline>()))
               .SingleInstance();
    }
}
