using Autofac;
using LifeOverYears.Providers;
using LifeOverYears.Services;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears;

public sealed class AppModule : Module
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _nvidiaKey;

    public AppModule(ILoggerFactory loggerFactory, string nvidiaKey)
    {
        _loggerFactory = loggerFactory;
        _nvidiaKey     = nvidiaKey;
    }

    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterInstance(new FileSystemProvider(_loggerFactory.CreateLogger<FileSystemProvider>()))
               .As<IFileSystemProvider>().SingleInstance();

        builder.RegisterInstance(new JsonProvider())
               .As<IJsonProvider>().SingleInstance();

        builder.Register(_ => new DataService(
                    _.Resolve<IFileSystemProvider>(),
                    _.Resolve<IJsonProvider>(),
                    _loggerFactory.CreateLogger<DataService>()))
               .As<IDataService>().SingleInstance();

        builder.RegisterInstance(new NvidiaProvider(new HttpClient(), _nvidiaKey, _loggerFactory.CreateLogger<NvidiaProvider>()))
               .As<INvidiaProvider>().SingleInstance();

        builder.Register(_ => new VisionProvider(_.Resolve<INvidiaProvider>(), _loggerFactory.CreateLogger<VisionProvider>()))
               .As<IVisionProvider>().SingleInstance();

        builder.Register(_ => new PromptProvider(_.Resolve<INvidiaProvider>(), _loggerFactory.CreateLogger<PromptProvider>()))
               .As<IPromptProvider>().SingleInstance();

        builder.Register(_ => new VisionService(_.Resolve<IVisionProvider>(), _.Resolve<IDataService>(), _loggerFactory.CreateLogger<VisionService>()))
               .As<IVisionService>().SingleInstance();

        builder.Register(_ => new PromptService(_.Resolve<IPromptProvider>(), _.Resolve<IDataService>(), _loggerFactory.CreateLogger<PromptService>()))
               .As<IPromptService>().SingleInstance();

        builder.Register(_ => new Pipeline(_.Resolve<IVisionService>(), _.Resolve<IPromptService>(), _.Resolve<IDataService>(), _loggerFactory.CreateLogger<Pipeline>()))
               .SingleInstance();
    }
}
