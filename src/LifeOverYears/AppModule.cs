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

        builder.RegisterInstance(new NvidiaProvider(new HttpClient(), nvidiaKey, _loggerFactory.CreateLogger<NvidiaProvider>()))
               .As<INvidiaProvider>().SingleInstance();

        builder.Register(_ => new VisionProvider(_.Resolve<INvidiaProvider>(), _loggerFactory.CreateLogger<VisionProvider>()))
               .As<IVisionProvider>().SingleInstance();

        builder.Register(_ => new VisionService(_.Resolve<IVisionProvider>(), _.Resolve<IDataService>(), _loggerFactory.CreateLogger<VisionService>()))
               .As<IVisionService>().SingleInstance();

        builder.Register(_ => new PromptService(_.Resolve<IDataService>(), _loggerFactory.CreateLogger<PromptService>()))
               .As<IPromptService>().SingleInstance();

        builder.RegisterInstance(new RunService(_loggerFactory.CreateLogger<RunService>()))
               .As<IRunService>().SingleInstance();

        var imagesEnabled = _configuration.GetValue("OpenAi:Enabled", true);
        var imagesMode = _configuration["OpenAi:Mode"] ?? "sync";
        _loggerFactory.CreateLogger<AppModule>().LogInformation(
            "Image generation provider: enabled={Enabled}, mode={Mode}", imagesEnabled, imagesMode);

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

            builder.RegisterInstance(new OpenAiProvider(new HttpClient(), openAiKey, _loggerFactory.CreateLogger<OpenAiProvider>()))
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
                    _loggerFactory.CreateLogger<Pipeline>()))
               .SingleInstance();
    }
}
