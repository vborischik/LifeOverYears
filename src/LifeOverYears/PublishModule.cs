using Autofac;
using LifeOverYears.Providers;
using LifeOverYears.Services;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LifeOverYears;

// Everything the publish and review modes need, wired from Publish:*. Its
// own module for the same reason VideoModule is: AppModule refuses to load
// without the generation keys, and publishing a finished run needs none of
// them. Loaded only by those two modes — a `run` never touches a publish
// key — and it registers a provider only for the platforms that are
// configured, so a missing Facebook token is not an error until Facebook is
// in Publish:Targets.
public sealed class PublishModule : Module
{
    private readonly IConfiguration _configuration;
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _outputRoot;

    public PublishModule(IConfiguration configuration, ILoggerFactory loggerFactory, string outputRoot)
    {
        _configuration = configuration;
        _loggerFactory = loggerFactory;
        _outputRoot    = outputRoot;
    }

    public static string ReviewRoot(string outputRoot) => Path.Combine(outputRoot, "on-review");

    private static HttpClient BuildHttpClient() =>
        new(new SocketsHttpHandler
        {
            PooledConnectionLifetime    = TimeSpan.FromMinutes(2),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(20),
        })
        {
            // A Telegram long poll and a 5 MB upload both outlive the default.
            Timeout = TimeSpan.FromMinutes(3),
        };

    protected override void Load(ContainerBuilder builder)
    {
        var enabled = _configuration.GetValue("Publish:Enabled", false);
        var privacy = _configuration["Publish:Privacy"] ?? "private";
        var targets = _configuration.GetSection("Publish:Targets").Get<string[]>() ?? Array.Empty<string>();

        builder.RegisterInstance(new ReviewQueue(
                    ReviewRoot(_outputRoot), enabled, _loggerFactory.CreateLogger<ReviewQueue>(),
                    runsDir: Path.Combine(_outputRoot, "runs")))
               .AsSelf().SingleInstance();

        // Storage — only if any configured target needs a URL, and only if
        // credentials are present; PublishService says which is missing.
        var dropbox = _configuration.GetSection("Publish:Dropbox");
        if (dropbox["RefreshToken"] is { Length: > 0 } || dropbox["AccessToken"] is { Length: > 0 })
        {
            builder.RegisterInstance(new DropboxProvider(
                        BuildHttpClient(),
                        new DropboxAuth(dropbox["AppKey"], dropbox["AppSecret"], dropbox["RefreshToken"], dropbox["AccessToken"]),
                        dropbox["Folder"] ?? "LifeOverYears",
                        _loggerFactory.CreateLogger<DropboxProvider>()))
                   .As<IPublicStorage>().SingleInstance();
        }

        // Telegram is the review channel and nothing else — no publishing
        // target reads this section.
        var telegram = _configuration.GetSection("Publish:Telegram");
        if (telegram["BotToken"] is { Length: > 0 } botToken && telegram["ReviewChatId"] is { Length: > 0 } reviewer)
            builder.RegisterInstance(new TelegramReviewProvider(
                        BuildHttpClient(), botToken, reviewer,
                        Path.Combine(ReviewRoot(_outputRoot), ".telegram-offset"),
                        _loggerFactory.CreateLogger<TelegramReviewProvider>()))
                   .As<IReviewChannel>().SingleInstance();

        var instagram = _configuration.GetSection("Publish:Instagram");
        if (instagram["AccessToken"] is { Length: > 0 } igToken && instagram["UserId"] is { Length: > 0 } igUser)
            builder.RegisterInstance(new InstagramProvider(
                        BuildHttpClient(), igToken, igUser, _loggerFactory.CreateLogger<InstagramProvider>()))
                   .As<IPublishTarget>().SingleInstance();

        var facebook = _configuration.GetSection("Publish:Facebook");
        if (facebook["PageAccessToken"] is { Length: > 0 } fbToken && facebook["PageId"] is { Length: > 0 } fbPage)
            builder.RegisterInstance(new FacebookProvider(
                        BuildHttpClient(), fbToken, fbPage, _loggerFactory.CreateLogger<FacebookProvider>()))
                   .As<IPublishTarget>().SingleInstance();

        // YouTube's provider is created asynchronously — the OAuth broker may
        // open a browser once — so it is registered as a factory and built
        // only when IPublishService is resolved, i.e. by --yes and by the
        // loop. Merely queueing a run must not open a consent window.
        var youtube = _configuration.GetSection("Publish:YouTube");
        if (youtube["ClientSecretPath"] is { Length: > 0 } secretPath
            && targets.Contains("youtube", StringComparer.OrdinalIgnoreCase))
        {
            builder.Register(_ => YouTubeProvider
                        .CreateAsync(secretPath, _loggerFactory.CreateLogger<YouTubeProvider>())
                        .GetAwaiter().GetResult())
                   .As<IPublishTarget>().SingleInstance();
        }

        // Music is laid down here and nowhere else. Publish:Music is a folder
        // per family — YouTube, Meta, Telegram, whatever is published to —
        // plus Required; the "_" keys are notes. A family with no entry
        // defaults to data/music/{family}. The ledger of used tracks is read
        // from the publish.json records under the runs folder.
        var musicSection  = _configuration.GetSection("Publish:Music");
        var musicRequired = musicSection.GetValue("Required", true);
        var musicFolders  = musicSection.GetChildren()
            .Where(c => !c.Key.StartsWith('_') && !c.Key.Equals("Required", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(c => c.Key, c => c.Value ?? "", StringComparer.OrdinalIgnoreCase);
        var runsDir       = Path.Combine(_outputRoot, "runs");
        builder.Register(c => new MusicService(
                    c.Resolve<IFfmpegProvider>(), musicFolders, runsDir, musicRequired,
                    _loggerFactory.CreateLogger<MusicService>()))
               .As<IMusicService>().SingleInstance();

        // A platform's own Privacy, when its section carries one, beats the
        // global word for that platform only.
        var privacyByPlatform = new[] { "YouTube", "Instagram", "Facebook" }
            .Select(p => (Platform: p.ToLowerInvariant(), Privacy: _configuration[$"Publish:{p}:Privacy"]))
            .Where(x => !string.IsNullOrWhiteSpace(x.Privacy))
            .ToDictionary(x => x.Platform, x => x.Privacy!, StringComparer.OrdinalIgnoreCase);

        // The family's edit: Publish:Cut is family → "loop" | "chronological".
        // The master is the loop; Meta is re-cut chronological because the
        // looping Reel was measured to draw fewer views. Needs the video
        // service, so the module carries the key-free VideoModule too.
        builder.RegisterModule(new VideoModule(_loggerFactory));
        var cutByFamily = _configuration.GetSection("Publish:Cut").GetChildren()
            .Where(c => !c.Key.StartsWith('_'))
            .ToDictionary(c => c.Key, c => c.Value ?? "", StringComparer.OrdinalIgnoreCase);
        builder.Register(c => new CutService(c.Resolve<IVideoService>(), cutByFamily, _loggerFactory.CreateLogger<CutService>()))
               .As<ICutService>().SingleInstance();

        builder.Register(c => new PublishService(
                    targets,
                    c.Resolve<IEnumerable<IPublishTarget>>().ToList(),
                    c.ResolveOptional<IPublicStorage>(),
                    c.Resolve<IMusicService>(),
                    _loggerFactory.CreateLogger<PublishService>(),
                    privacyByPlatform,
                    c.Resolve<ICutService>()))
               .As<IPublishService>().SingleInstance();

        builder.Register(c => new ReviewLoop(
                    c.Resolve<ReviewQueue>(),
                    c.Resolve<IReviewChannel>(),
                    c.Resolve<IPublishService>(),
                    privacy,
                    _loggerFactory.CreateLogger<ReviewLoop>()))
               .AsSelf().SingleInstance();
    }
}
