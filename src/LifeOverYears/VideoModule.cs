using Autofac;
using LifeOverYears.Providers;
using LifeOverYears.Services;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears;

// The registrations that need no API key: ffmpeg, the year overlay, video
// assembly. Split out of AppModule because AppModule refuses to load without
// Nvidia:ApiKey, and `assemble` is meant to re-cut a finished run on a
// machine that has no keys at all. Before this, assemble built the same
// three objects by hand in Program.cs — the one place in the app that named
// a concrete provider outside a module. AppModule loads this module too, so
// the wiring exists once.
public sealed class VideoModule : Module
{
    private readonly ILoggerFactory _loggerFactory;

    public VideoModule(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterInstance(new YearOverlayService(_loggerFactory.CreateLogger<YearOverlayService>()))
               .As<IYearOverlayService>().SingleInstance();

        builder.RegisterInstance(new FfmpegProvider(_loggerFactory.CreateLogger<FfmpegProvider>()))
               .As<IFfmpegProvider>().SingleInstance();

        builder.Register(_ => new VideoService(_.Resolve<IFfmpegProvider>(), _loggerFactory.CreateLogger<VideoService>()))
               .As<IVideoService>().SingleInstance();
    }
}
