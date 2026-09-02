using LifeOverYears.Models;

namespace LifeOverYears.Services.Interfaces;

// The brand-series counterpart to IPromptService. Same output type, so the run
// folder, the provider and the video tail cannot tell the two paths apart —
// only the input differs: a BrandSeries file instead of a SceneDna read off a
// photograph. Synchronous because it is pure assembly: no template file, no
// era JSON, no model call.
public interface IBrandSeriesPromptService
{
    // There is no separate base prompt. The first era is drawn from text and is
    // itself the frame the rest of the run edits, so an extra empty-premises
    // image would be one generation spent on something only that era looked at.
    // centerReplacements is data/brands/center-replacements.txt: the trades that
    // move into a dead retail box. Passed in rather than loaded here so the
    // builder stays a pure assembler and the file is read once per run.
    Prompt Build(
        BrandSeries series, int year, GenerationContext context,
        IReadOnlyList<(string Name, int From, int To, string Category)> centerReplacements);
}
