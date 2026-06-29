using LifeOverYears.Providers;
using LifeOverYears.Services;
using Microsoft.Extensions.Logging; 

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: LifeOverYears <photoPath> <year>");
    return 1;
}

var photoPath = args[0];
var year      = int.Parse(args[1]);
var nvidiaKey = Environment.GetEnvironmentVariable("NVIDIA_API_KEY")
    ?? throw new InvalidOperationException("NVIDIA_API_KEY environment variable is not set");

using var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));

var fs    = new FileSystemProvider(loggerFactory.CreateLogger<FileSystemProvider>());
var json  = new JsonProvider();
var data  = new DataService(fs, json, loggerFactory.CreateLogger<DataService>());

var nvidia          = new NvidiaProvider(new HttpClient(), nvidiaKey, loggerFactory.CreateLogger<NvidiaProvider>());
var visionProvider  = new VisionProvider(nvidia, loggerFactory.CreateLogger<VisionProvider>());
var promptProvider  = new PromptProvider(nvidia, loggerFactory.CreateLogger<PromptProvider>());

var vision  = new VisionService(visionProvider, data, loggerFactory.CreateLogger<VisionService>());
var prompt  = new PromptService(promptProvider, data, loggerFactory.CreateLogger<PromptService>());

var pipeline = new Pipeline(vision, prompt, data, loggerFactory.CreateLogger<Pipeline>());

await pipeline.RunAsync(photoPath, year);
return 0;
