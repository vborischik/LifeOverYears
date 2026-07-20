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

        // TODO: replace with OpenAiImageProvider once Step 3 goes live
        builder.RegisterInstance(new StubImageProvider(_loggerFactory.CreateLogger<StubImageProvider>()))
               .As<IImageGenerationProvider>().SingleInstance();

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
                    _loggerFactory.CreateLogger<Pipeline>()))
               .SingleInstance();
    }
}
