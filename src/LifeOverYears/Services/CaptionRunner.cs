using System.Text.Json;
using LifeOverYears.Models;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Services;

// The caption tail, shared by the pipeline and by standalone 'collect' the same
// way VideoAssemblyRunner shares the video tail. A batch run normally finishes
// through collect — the pipeline's own wait is only one of the ways the images
// arrive — so the caption cannot live in Pipeline alone or every resumed run
// silently ends up without one.
public static class CaptionRunner
{
    private const string NarrativeFileName = "narrative.json";
    private const string CaptionFileName   = "caption.txt";
    private const string TitleFileName     = "title.txt";

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    // The arc facts live in the GenerationContext, which exists only while the
    // prompts are being built — a later process has no way to recompute them.
    // Persisted next to run.json so collect can caption a run it did not build.
    public static Task SaveNarrativeAsync(string runRoot, SceneNarrative narrative) =>
        File.WriteAllTextAsync(
            Path.Combine(runRoot, NarrativeFileName),
            JsonSerializer.Serialize(narrative, Json));

    public static async Task<SceneNarrative?> ReadNarrativeAsync(string runRoot)
    {
        var path = Path.Combine(runRoot, NarrativeFileName);
        if (!File.Exists(path))
            return null;
        return JsonSerializer.Deserialize<SceneNarrative>(await File.ReadAllTextAsync(path), Json);
    }

    public static async Task<SceneDna?> ReadSceneAsync(string runRoot)
    {
        var path = Path.Combine(runRoot, "scene.json");
        if (!File.Exists(path))
            return null;
        return JsonSerializer.Deserialize<SceneDna>(await File.ReadAllTextAsync(path), Json);
    }

    // Best-effort by design: the images, stamped frames and video are already on
    // disk by the time this runs, so a caption failure must not discard a
    // finished run. Returns whether caption.txt was written.
    public static async Task<bool> WriteAsync(
        ICaptionService captions, SceneDna scene, SceneNarrative narrative,
        string runRoot, ILogger logger)
    {
        try
        {
            var caption = await captions.GenerateAsync(scene, narrative);
            var path    = Path.Combine(runRoot, CaptionFileName);
            await File.WriteAllTextAsync(path,
                caption.Description + "\n\n" + string.Join("\n", caption.Hashtags));
            logger.LogInformation("Caption written: {Path}", path);

            // The YouTube title is a separate artefact: caption.txt stays exactly
            // the Facebook/Instagram payload. Guarded on its own so a failed title
            // write cannot undo the caption already on disk or fail the run.
            try
            {
                var titlePath = Path.Combine(runRoot, TitleFileName);
                await File.WriteAllTextAsync(titlePath, caption.Title);
                logger.LogInformation("Title written: {Path}", titlePath);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Title not written; caption.txt is unaffected: {Root}", runRoot);
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Caption not written; the run is otherwise complete: {Root}", runRoot);
            return false;
        }
    }
}
