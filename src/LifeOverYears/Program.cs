using LifeOverYears.Providers;
using LifeOverYears.Services;
using Microsoft.Extensions.Logging;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: LifeOverYears <photoPath>");
    return 1;
}

var photoPath = args[0];
var apiKey = Environment.GetEnvironmentVariable("NVIDIA_API_KEY")
    ?? throw new InvalidOperationException("NVIDIA_API_KEY environment variable is not set");

using var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));

var http           = new HttpClient();
var nvidia         = new NvidiaProvider(http, apiKey, loggerFactory.CreateLogger<NvidiaProvider>());
var visionProvider = new VisionProvider(nvidia, loggerFactory.CreateLogger<VisionProvider>());
var fs             = new FileSystemProvider(loggerFactory.CreateLogger<FileSystemProvider>());
var json           = new JsonProvider();
var data           = new DataService(fs, json, loggerFactory.CreateLogger<DataService>());
var vision         = new VisionService(visionProvider, data, loggerFactory.CreateLogger<VisionService>());
var pipeline       = new Pipeline(vision, loggerFactory.CreateLogger<Pipeline>());

await pipeline.RunAsync(photoPath);
return 0;
