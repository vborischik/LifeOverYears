using System.Net;
using System.Text;
using System.Text.Json;
using LifeOverYears.Models;
using LifeOverYears.Providers;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Services;

// P1–P12 over the publish path, offline (P11 runs real ffmpeg on a generated clip). The review loop and the publish
// service run against fake channel/target/storage; the three HTTP providers
// run against a fake handler that records every request, so what is
// asserted is the request the platform would have received — the token in
// the body, the button markup, the URL rewrite — not a log line.
public static class PublishSmokeTest
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static async Task<int> RunAsync(ILoggerFactory loggerFactory, ILogger logger)
    {
        var f = new List<(string Id, string Desc, bool? Pass, string Detail)>();
        var work = Path.Combine(Path.GetTempPath(), "loy-smoke-publish-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(work);

        try
        {
            var run = MakeRun(work, "run-a");

            await DoP1(run, f);
            await DoP2(work, run, loggerFactory, f);
            await DoP3(f);
            await DoP4(work, loggerFactory, f);
            await DoP5(work, loggerFactory, f);
            await DoP6(loggerFactory, f);
            await DoP7(loggerFactory, f);
            await DoP8(work, loggerFactory, f);
            await DoP9(work, loggerFactory, f);
            await DoP10(work, loggerFactory, f);
            await DoP11(work, loggerFactory, f);
            await DoP12(work, loggerFactory, f);
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch { /* temp */ }
        }

        var passed = f.Count(x => x.Pass == true);
        foreach (var (id, _, pass, detail) in f)
            logger.LogInformation("[SmokePublish]   {Id} {Status}: {Detail}", id, pass == true ? "PASS" : "FAIL", detail);

        var reportDir = Path.Combine("output", "smoke-publish");
        Directory.CreateDirectory(reportDir);
        var sb = new StringBuilder("# Publish Smoke Test Report\n\n| Check | Description | Status | Detail |\n|---|---|---|---|\n");
        foreach (var (id, desc, pass, detail) in f)
            sb.AppendLine($"| {id} | {desc} | {(pass == true ? "✅ PASS" : "❌ FAIL")} | {detail.Replace("|", "\\|").Replace("\n", " ")} |");
        await File.WriteAllTextAsync(Path.Combine(reportDir, "report.md"), sb.ToString());

        logger.LogInformation("Publish smoke test: {Passed}/{Total} checks passed", passed, f.Count);
        return passed == f.Count ? 0 : 1;
    }

    // ── fixtures ─────────────────────────────────────────────────────────────

    // A run folder as the pipeline leaves it: video, caption in CaptionRunner's
    // exact form, title, era images.
    private static string MakeRun(string work, string name)
    {
        var root = Path.Combine(work, "runs", name);
        Directory.CreateDirectory(Path.Combine(root, "video"));
        Directory.CreateDirectory(Path.Combine(root, "images"));
        File.WriteAllBytes(Path.Combine(root, "video", "timeline.mp4"), new byte[] { 0, 0, 0, 0x18, 0x66, 0x74, 0x79, 0x70 });
        File.WriteAllText(Path.Combine(root, "caption.txt"),
            "Look at 1975. Then look at 2025.\nDo you remember it?\n\n#nostalgia\n#thenandnow\n#1975");
        File.WriteAllText(Path.Combine(root, "title.txt"), "The Building Outlived the Company. 1975-2025\n");
        foreach (var y in new[] { 1975, 1985, 2025 })
            File.WriteAllBytes(Path.Combine(root, "images", $"{y}.png"), new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        File.WriteAllText(Path.Combine(root, "prompts.txt"), "not copied");
        return root;
    }

    private static Caption SampleCaption(int tagCount = 3, int bodyLength = 80) => new(
        "id", "A title",
        new string('x', bodyLength),
        Enumerable.Range(1, tagCount).Select(i => $"#tag{i}").ToList());

    private static PublishRequest SampleRequest(string videoPath, string? url = null) => new(
        new Video("run-a", Array.Empty<string>(), videoPath, "2026-01-01T00:00:00Z"),
        SampleCaption(), null, "private", null, url);

    // ── P1 ───────────────────────────────────────────────────────────────────

    private static async Task DoP1(string run, List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();
        var req = await RunPublishSource.ReadAsync(run, "private");

        if (req.Caption.Title != "The Building Outlived the Company. 1975-2025") errs.Add($"title: '{req.Caption.Title}'");
        if (req.Caption.Description != "Look at 1975. Then look at 2025.\nDo you remember it?") errs.Add($"body: '{req.Caption.Description}'");
        if (!req.Caption.Hashtags.SequenceEqual(new[] { "#nostalgia", "#thenandnow", "#1975" })) errs.Add($"tags: {string.Join(",", req.Caption.Hashtags)}");
        if (!req.ThumbnailPath!.EndsWith(Path.Combine("images", "2025.png"))) errs.Add($"cover: {req.ThumbnailPath} (newest year expected)");
        if (req.Video.Id != "run-a") errs.Add($"video id: {req.Video.Id}");
        if (req.Privacy != "private") errs.Add($"privacy: {req.Privacy}");

        // A caption with no tag block is all body.
        var (body, tags) = RunPublishSource.SplitCaption("Just words.\nTwo lines.");
        if (body != "Just words.\nTwo lines." || tags.Count != 0) errs.Add("tagless caption split wrongly");

        f.Add(("P1", "A run folder reads into a PublishRequest: title, body and hashtags split back from caption.txt, the newest frame as cover",
            errs.Count == 0, errs.Count == 0 ? "title, 2-line body, 3 tags, images/2025.png cover" : string.Join("; ", errs)));
    }

    // ── P2 ───────────────────────────────────────────────────────────────────

    private static async Task DoP2(string work, string run, ILoggerFactory lf, List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();
        var queue = new ReviewQueue(Path.Combine(work, "on-review"), autoEnqueue: true, lf.CreateLogger<ReviewQueue>());

        var item = await queue.EnqueueAsync(run) ?? throw new InvalidOperationException("enqueue returned null");
        var dir  = queue.ItemFolder(item);
        foreach (var rel in new[] { "video/timeline.mp4", "caption.txt", "title.txt", "images/2025.png", ReviewQueue.ReviewFileName })
            if (!File.Exists(Path.Combine(dir, rel))) errs.Add($"copy lacks {rel}");
        if (File.Exists(Path.Combine(dir, "prompts.txt"))) errs.Add("copied a file publishing does not need");
        if (File.Exists(Path.Combine(dir, "images", "1975.png"))) errs.Add("copied an era image that is not the cover");
        if (item.RunFolder != Path.GetFullPath(run)) errs.Add("item does not point back at the run");

        // Idempotent: a second enqueue of the same run changes nothing.
        var again = await queue.EnqueueAsync(run);
        if (again is null || again.Id != item.Id) errs.Add("second enqueue did not return the existing item");
        if ((await queue.ListAsync()).Count != 1) errs.Add("second enqueue created a second item");

        await queue.MarkSentAsync(item, 4242);
        var listed = (await queue.ListAsync()).Single();
        if (listed.ReviewMessageId != 4242) errs.Add("MarkSent did not persist the message id");

        var state = new PublishState("skipped", "2026-01-01T00:00:00Z", Array.Empty<Publication>());
        await queue.CompleteAsync(listed, state);
        if (Directory.Exists(dir)) errs.Add("complete left the review copy in place");
        var record = Path.Combine(run, RunPublishSource.PublishFileName);
        if (!File.Exists(record)) errs.Add("complete wrote no publish.json to the run");
        else if (!File.ReadAllText(record).Contains("\"skipped\"")) errs.Add("publish.json does not carry the status");

        // Decided runs never come back — but a FAILED attempt is not a
        // decision, and must be queueable again.
        if (await queue.EnqueueAsync(run) is not null) errs.Add("a run with publish.json was queued again");
        File.WriteAllText(record, JsonSerializer.Serialize(new PublishState("failed", "2026-01-01T00:00:00Z", Array.Empty<Publication>(), "youtube: invalid_grant"), Json));
        var retry = await queue.EnqueueAsync(run);
        if (retry is null) errs.Add("a run whose publish FAILED could not be queued again");
        else await queue.CompleteAsync(retry, state);

        // A whole run copied into the queue by hand, no review.json: adopted
        // on the next scan, and pointed back at its original by name.
        var runsDir  = Path.Combine(work, "runs");
        var handRun  = MakeRun(work, "by-hand");
        var handCopy = Path.Combine(queue.Root, "by-hand");
        CopyDir(handRun, handCopy);
        var adoptQueue = new ReviewQueue(queue.Root, true, lf.CreateLogger<ReviewQueue>(), runsDir: runsDir);
        var adopted = (await adoptQueue.ListAsync()).SingleOrDefault(i => i.Id == "by-hand");
        if (adopted is null) errs.Add("a hand-placed run folder was not adopted");
        else
        {
            if (adopted.RunFolder != Path.GetFullPath(handRun)) errs.Add($"adopted item points at {adopted.RunFolder}, not the original run");
            if (!File.Exists(Path.Combine(handCopy, ReviewQueue.ReviewFileName))) errs.Add("adoption did not write review.json");
            await adoptQueue.CompleteAsync(adopted, state);
            if (Directory.Exists(handCopy)) errs.Add("adopted copy not deleted on complete");
            if (!File.Exists(Path.Combine(handRun, RunPublishSource.PublishFileName))) errs.Add("adopted item's decision did not reach the original run");
        }

        // The hook is inert when off, and never throws.
        var off = new ReviewQueue(Path.Combine(work, "on-review-off"), autoEnqueue: false, lf.CreateLogger<ReviewQueue>());
        var runB = MakeRun(work, "run-b");
        await off.TryEnqueueAfterRunAsync(runB);
        if (Directory.Exists(Path.Combine(work, "on-review-off"))) errs.Add("AutoEnqueue=false still queued");
        await off.TryEnqueueAfterRunAsync(Path.Combine(work, "does-not-exist"));

        f.Add(("P2", "The review queue copies only what publishing needs, is idempotent, records the decision on the original run, deletes the copy, never re-queues a published or skipped run but does re-queue a failed one, and adopts a run folder dropped in by hand",
            errs.Count == 0, errs.Count == 0 ? "copy = video+caption+title+cover+review.json; publish.json written back; copy deleted; hook inert when off; failed re-queued; hand-placed run adopted and mapped to its original" : string.Join("; ", errs)));
    }

    // ── P3 ───────────────────────────────────────────────────────────────────

    private static Task DoP3(List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();
        var (body, tags) = RunPublishSource.SplitCaption("body\n\n#a #b\n#c");
        if (body != "body" || tags.Count != 3) errs.Add("tags on one line not all read");

        var markup = TelegramReviewProvider.ReplyMarkup("run-a");
        using var doc = JsonDocument.Parse(markup);
        var row = doc.RootElement.GetProperty("inline_keyboard")[0];
        if (row.GetArrayLength() != 2) errs.Add("keyboard is not two buttons");
        if (row[0].GetProperty("callback_data").GetString() != "publish:run-a") errs.Add("approve button carries wrong data");
        if (row[1].GetProperty("callback_data").GetString() != "skip:run-a") errs.Add("skip button carries wrong data");

        f.Add(("P3", "Review buttons carry the item id in their callback data",
            errs.Count == 0, errs.Count == 0 ? "publish:/skip: callback data on both buttons" : string.Join("; ", errs)));
        return Task.CompletedTask;
    }

    // ── P4 ───────────────────────────────────────────────────────────────────

    // The loop against a scripted channel and a counting publisher.
    private static async Task DoP4(string work, ILoggerFactory lf, List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();
        var runA = MakeRun(work, "loop-a");
        var runB = MakeRun(work, "loop-b");
        var queue = new ReviewQueue(Path.Combine(work, "loop-queue"), true, lf.CreateLogger<ReviewQueue>());
        await queue.EnqueueAsync(runA);
        await queue.EnqueueAsync(runB);

        var channel   = new FakeChannel();
        var publisher = new FakePublisher(new[] { "instagram" });
        var loop      = new ReviewLoop(queue, channel, publisher, "private", lf.CreateLogger<ReviewLoop>());

        // 1. First pass: sent, no answer yet → not complete, message id persisted.
        var items = await queue.ListAsync();
        var done  = await loop.ProcessOneAsync(items[0], CancellationToken.None);
        if (done) errs.Add("completed with no decision");
        if (channel.Sent.Count != 1) errs.Add($"sent {channel.Sent.Count} times on first pass");
        var sentId = (await queue.ListAsync())[0].ReviewMessageId;
        if (sentId is null) errs.Add("message id not persisted after send");

        // 2. Second pass (a restart, say): NOT re-sent; skip answered → skipped.
        channel.Queue(new ReviewDecision(sentId, false));
        items = await queue.ListAsync();
        done  = await loop.ProcessOneAsync(items[0], CancellationToken.None);
        if (!done) errs.Add("skip decision did not complete the item");
        if (channel.Sent.Count != 1) errs.Add("item was re-sent on the second pass");
        if (publisher.Calls != 0) errs.Add("skip published");
        if (!RecordSays(runA, "skipped")) errs.Add("skip not recorded on the run");
        if (channel.Reports.LastOrDefault() != "Skipped.") errs.Add("reviewer not told about the skip");

        // 3. Next item: approve by bare text (no message id) → published.
        items = await queue.ListAsync();
        if (items.Count != 1 || items[0].Id != "loop-b") errs.Add("queue did not advance to the next run");
        await loop.ProcessOneAsync(items[0], CancellationToken.None);          // sends
        channel.Queue(new ReviewDecision(null, true));                        // "yes"
        done = await loop.ProcessOneAsync((await queue.ListAsync())[0], CancellationToken.None);
        if (!done) errs.Add("approve did not complete the item");
        if (publisher.Calls != 1) errs.Add($"approve published {publisher.Calls} times");
        if (publisher.LastRequest is null || !publisher.LastRequest.Video.FilePath.StartsWith(Path.GetFullPath(runB)))
            errs.Add("published from the review copy, not the original run — the muxed file would die with the copy");
        if (!RecordSays(runB, "published")) errs.Add("publish not recorded on the run");
        if (channel.Reports.LastOrDefault() is not { } rep || !rep.Contains("instagram: https://fake/")) errs.Add("reviewer not sent the URL");
        if ((await queue.ListAsync()).Count != 0) errs.Add("queue not empty after both decisions");

        // 4. A decision for a different message is not applied to this item.
        var runC = MakeRun(work, "loop-c");
        await queue.EnqueueAsync(runC);
        await loop.ProcessOneAsync((await queue.ListAsync())[0], CancellationToken.None);
        channel.Queue(new ReviewDecision(999999, true));
        done = await loop.ProcessOneAsync((await queue.ListAsync())[0], CancellationToken.None);
        if (done) errs.Add("a decision for another message was applied");
        if (publisher.Calls != 1) errs.Add("stray decision published");

        f.Add(("P4", "The review loop sends each item once, survives a restart without re-sending, skips on no, publishes on yes from the ORIGINAL run, ignores a decision for another message, and reports the outcome",
            errs.Count == 0, errs.Count == 0 ? "sent once; skip → skipped, no publish; text yes → published from the original, URL reported; foreign decision ignored" : string.Join("; ", errs)));
    }

    // ── P5 ───────────────────────────────────────────────────────────────────

    private static async Task DoP5(string work, ILoggerFactory lf, List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();

        if (DropboxProvider.ToDirectLink("https://www.dropbox.com/scl/fi/abc/x.mp4?rlkey=k&dl=0")
            != "https://dl.dropboxusercontent.com/scl/fi/abc/x.mp4?rlkey=k&dl=1") errs.Add("scl link not rewritten");
        if (DropboxProvider.ToDirectLink("https://www.dropbox.com/s/abc/x.mp4?dl=0")
            != "https://dl.dropboxusercontent.com/s/abc/x.mp4?dl=1") errs.Add("legacy link not rewritten");

        // Refresh token → one token call, then upload, then share; the 409 on
        // share is the success path for a re-upload.
        var handler = new FakeHandler(req => req.RequestUri!.AbsolutePath switch
        {
            "/oauth2/token" => Json200("{\"access_token\":\"tok-1\",\"expires_in\":14400}"),
            "/2/files/upload" => Json200("{\"path_lower\":\"/lifeoveryears/x.mp4\"}"),
            // The live 409 carries only the tag — no metadata, no url — and
            // the link has to be listed by path. Modelled on the real body.
            "/2/sharing/create_shared_link_with_settings" => new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = new StringContent("{\"error_summary\":\"shared_link_already_exists/..\",\"error\":{\".tag\":\"shared_link_already_exists\"}}"),
            },
            "/2/sharing/list_shared_links" => Json200("{\"links\":[{\".tag\":\"file\",\"url\":\"https://www.dropbox.com/scl/fi/q/x.mp4?rlkey=r&dl=0\"}],\"has_more\":false}"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        var dropbox = new DropboxProvider(new HttpClient(handler), new DropboxAuth("key", "secret", "refresh", null), "LifeOverYears", lf.CreateLogger<DropboxProvider>());
        var file = Path.Combine(work, "p5.mp4");
        File.WriteAllBytes(file, new byte[16]);

        var url = await dropbox.UploadPublicAsync(file, "x.mp4");
        if (url != "https://dl.dropboxusercontent.com/scl/fi/q/x.mp4?rlkey=r&dl=1") errs.Add($"url: {url}");
        await dropbox.UploadPublicAsync(file, "x.mp4");
        if (handler.Requests.Count(r => r.Path == "/oauth2/token") != 1) errs.Add("access token not cached across uploads");
        var upload = handler.Requests.First(r => r.Path == "/2/files/upload");
        if (upload.Authorization != "Bearer tok-1") errs.Add("upload without the minted bearer");
        if (!upload.Headers.Contains("\"mode\":\"overwrite\"")) errs.Add("upload is not overwrite");
        if (!handler.Requests.Any(r => r.Path == "/2/sharing/list_shared_links")) errs.Add("the existing link was not looked up by path after the 409");

        f.Add(("P5", "Dropbox mints an access token from the refresh token once, uploads with it as overwrite, and turns the shared link into a direct-download URL — looking the link up by path when the 409 carries only the tag, as the live API does",
            errs.Count == 0, errs.Count == 0 ? "one token call for two uploads; bearer set; both link shapes rewritten; bare-tag 409 → list_shared_links" : string.Join("; ", errs)));
    }

    // ── P6 ───────────────────────────────────────────────────────────────────

    private static async Task DoP6(ILoggerFactory lf, List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();

        try { InstagramProvider.BuildCaption(SampleCaption(tagCount: 31)); errs.Add("31 hashtags accepted"); }
        catch (ArgumentException) { }
        try { InstagramProvider.BuildCaption(SampleCaption(bodyLength: 2300)); errs.Add("2300-char caption accepted"); }
        catch (ArgumentException) { }
        var ok = InstagramProvider.BuildCaption(SampleCaption());
        if (!ok.EndsWith("#tag1 #tag2 #tag3")) errs.Add("caption does not end on the tag line");

        var handler = new FakeHandler(req =>
        {
            var p = req.RequestUri!.AbsolutePath;
            if (p.EndsWith("/media"))         return Json200("{\"id\":\"c1\"}");
            if (p.EndsWith("/c1"))            return Json200("{\"status_code\":\"FINISHED\"}");
            if (p.EndsWith("/media_publish")) return Json200("{\"id\":\"m1\"}");
            if (p.EndsWith("/m1"))            return Json200("{\"permalink\":\"https://www.instagram.com/reel/XYZ/\"}");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var ig = new InstagramProvider(new HttpClient(handler), "ig-token", "17841", lf.CreateLogger<InstagramProvider>());

        try { await ig.PublishAsync(SampleRequest("v.mp4", url: null)); errs.Add("published with no public URL"); }
        catch (InvalidOperationException) { }
        if (handler.Requests.Count != 0) errs.Add("a request went out before the URL check");

        var pub = await ig.PublishAsync(SampleRequest("v.mp4", url: "https://dl.example/x.mp4"));
        if (pub.Url != "https://www.instagram.com/reel/XYZ/") errs.Add($"url: {pub.Url}");
        if (pub.Platform != "instagram") errs.Add("platform");
        var create = handler.Requests.First(r => r.Path.EndsWith("/media"));
        if (create.Url.Contains("ig-token")) errs.Add("token in the URL");
        if (!create.Body.Contains("access_token=ig-token")) errs.Add("token not in the body");
        if (!create.Body.Contains("media_type=REELS")) errs.Add("not a Reel");
        if (!create.Body.Contains("is_ai_generated=true")) errs.Add("the container does not self-disclose AI content — Meta requires it for photorealistic synthetic video");

        f.Add(("P6", "Instagram refuses without a public URL before any call, enforces the caption limits, keeps the token in the body, discloses AI content on the container, and returns the permalink",
            errs.Count == 0, errs.Count == 0 ? "no-URL refused with zero requests; 31 tags and 2300 chars rejected; token in body; is_ai_generated=true; permalink returned" : string.Join("; ", errs)));
    }

    // ── P7 ───────────────────────────────────────────────────────────────────

    private static async Task DoP7(ILoggerFactory lf, List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();

        // Callback from the reviewer's chat → decision; from a stranger → null;
        // typed "yes" → approve with the replied-to id; unknown text → null.
        var handler = new FakeHandler(_ => Json200("{\"ok\":true,\"result\":true}"));
        var tg = new TelegramReviewProvider(new HttpClient(handler), "bot", "111", Path.Combine(Path.GetTempPath(), "loy-offset-" + Guid.NewGuid().ToString("N")), lf.CreateLogger<TelegramReviewProvider>());

        static JsonElement U(string json) => JsonDocument.Parse(json).RootElement;
        var callback = U("{\"update_id\":1,\"callback_query\":{\"id\":\"q\",\"data\":\"publish:run-a\",\"message\":{\"message_id\":55,\"chat\":{\"id\":111}}}}");
        var d = await tg.ParseUpdateAsync(callback, CancellationToken.None);
        if (d is not { Approved: true, ReviewMessageId: 55 }) errs.Add("approve button not parsed");
        if (!handler.Requests.Any(r => r.Path.EndsWith("/answerCallbackQuery"))) errs.Add("callback not answered");

        var stranger = U("{\"update_id\":2,\"callback_query\":{\"id\":\"q\",\"data\":\"publish:run-a\",\"message\":{\"message_id\":55,\"chat\":{\"id\":222}}}}");
        if (await tg.ParseUpdateAsync(stranger, CancellationToken.None) is not null) errs.Add("a stranger's button press was accepted");

        var yes = U("{\"update_id\":3,\"message\":{\"chat\":{\"id\":111},\"text\":\"да\",\"reply_to_message\":{\"message_id\":55}}}");
        d = await tg.ParseUpdateAsync(yes, CancellationToken.None);
        if (d is not { Approved: true, ReviewMessageId: 55 }) errs.Add("typed да not parsed");

        var no = U("{\"update_id\":4,\"message\":{\"chat\":{\"id\":111},\"text\":\"no\"}}");
        d = await tg.ParseUpdateAsync(no, CancellationToken.None);
        if (d is not { Approved: false, ReviewMessageId: null }) errs.Add("bare no not parsed");

        var chatter = U("{\"update_id\":5,\"message\":{\"chat\":{\"id\":111},\"text\":\"what is this\"}}");
        if (await tg.ParseUpdateAsync(chatter, CancellationToken.None) is not null) errs.Add("unrelated text produced a decision");

        f.Add(("P7", "The Telegram review channel accepts buttons and typed yes/no from the reviewer's chat only, and ignores strangers and chatter",
            errs.Count == 0, errs.Count == 0 ? "button → approve(55); stranger ignored; да → approve; no → skip; chatter ignored" : string.Join("; ", errs)));
    }

    // ── P8 ───────────────────────────────────────────────────────────────────

    private static async Task DoP8(string work, ILoggerFactory lf, List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();
        var log  = new List<string>();
        var storage   = new FakeStorage(log);
        var instagram = new FakeTarget("instagram", log, requireUrl: true);
        var youtube   = new FakeTarget("youtube", log, requireUrl: false);
        var failing   = new FakeTarget("facebook", log, requireUrl: true) { Fail = true };
        var file = Path.Combine(work, "p8.mp4");
        File.WriteAllBytes(file, new byte[8]);

        var music = new FakeMusic(log);
        var svc = new PublishService(new[] { "youtube", "instagram", "facebook" },
            new IPublishTarget[] { instagram, youtube, failing }, storage, music, lf.CreateLogger<PublishService>());
        var state = await svc.PublishAsync(SampleRequest(file));

        // Two families in target order: youtube alone (mux, post — it takes
        // the bytes), then Meta (mux once, upload once, post twice).
        if (!log.SequenceEqual(new[] { "music:youtube", "youtube", "music:meta", "storage", "instagram", "facebook" })) errs.Add($"order: {string.Join(">", log)}");
        if (storage.LastUploaded is null || !storage.LastUploaded.EndsWith(".meta.mp4")) errs.Add($"storage got {storage.LastUploaded}, not the muxed meta file");
        if (instagram.LastRequest?.Caption.Description.EndsWith("Music: fake (meta)") != true) errs.Add("credit line did not reach the target");
        if (state.Status != "failed") errs.Add($"status with one failing target: {state.Status}");
        if (state.Publications.Count != 2) errs.Add($"{state.Publications.Count} publications recorded, expected the two that succeeded");
        if (state.Error is null || !state.Error.Contains("facebook")) errs.Add("error does not name the failing target");
        if (state.Music?.GetValueOrDefault("meta") != "fake-meta.mp3" || state.Music.GetValueOrDefault("youtube") != "fake-youtube.mp3") errs.Add("tracks not recorded per family in the state");

        // A platform's own privacy word beats the request's, for that
        // platform only; --targets narrows a run to the named platforms.
        var log2 = new List<string>();
        var fb2 = new FakeTarget("facebook", log2, requireUrl: true);
        var ig2 = new FakeTarget("instagram", log2, requireUrl: true);
        var svc2 = new PublishService(new[] { "instagram", "facebook" }, new IPublishTarget[] { fb2, ig2 },
            new FakeStorage(log2), new FakeMusic(log2), lf.CreateLogger<PublishService>(),
            new Dictionary<string, string> { ["facebook"] = "public" });
        var st2 = await svc2.PublishAsync(SampleRequest(file), only: new[] { "facebook" });
        if (ig2.LastRequest is not null) errs.Add("--targets facebook still published to instagram");
        if (fb2.LastRequest?.Privacy != "public") errs.Add($"facebook privacy override not applied: {fb2.LastRequest?.Privacy}");
        if (st2.Publications.Count != 1 || st2.Publications[0].Platform != "facebook") errs.Add("subset publish recorded the wrong platforms");
        var st3 = await svc2.PublishAsync(SampleRequest(file), only: new[] { "tiktok" });
        if (st3.Status != "failed" || st3.Error?.Contains("tiktok") != true) errs.Add("an unknown --targets platform was not reported");

        // Config naming an unknown platform fails at construction, not on
        // the first publish.
        try { _ = new PublishService(Array.Empty<string>(), new IPublishTarget[] { instagram }, storage, music, lf.CreateLogger<PublishService>()); errs.Add("empty target list accepted"); }
        catch (InvalidOperationException) { }
        try { _ = new PublishService(new[] { "tiktok" }, Array.Empty<IPublishTarget>(), null, music, lf.CreateLogger<PublishService>()); errs.Add("unknown target accepted"); }
        catch (InvalidOperationException) { }
        try { _ = new PublishService(new[] { "instagram" }, new IPublishTarget[] { instagram }, null, music, lf.CreateLogger<PublishService>()); errs.Add("URL target without storage accepted"); }
        catch (InvalidOperationException) { }

        f.Add(("P8", "PublishService muxes once per family, uploads that family's muxed file once before its URL targets, lets one failing target not stop the others, records what succeeded and which track, applies a per-platform privacy, honours a --targets subset, and refuses misconfiguration at construction",
            errs.Count == 0, errs.Count == 0 ? "youtube family then meta family; storage got the .meta.mp4 once; credit in caption; 2 published, failed naming facebook; a track per family recorded; bad config refused" : string.Join("; ", errs)));
    }

    // ── P9 ───────────────────────────────────────────────────────────────────

    private static async Task DoP9(string work, ILoggerFactory lf, List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();

        // A published state written by the loop round-trips as JSON the
        // reviewer or a later tool can read.
        var state = new PublishState("published", "2026-01-01T00:00:00Z",
            new[] { new Publication("p", "v", "c", "instagram", "https://x/1", "2026-01-01T00:00:00Z") });
        var json = JsonSerializer.Serialize(state, Json);
        var back = JsonSerializer.Deserialize<PublishState>(json, Json);
        if (back is null || back.Publications.Count != 1 || back.Publications[0].Url != "https://x/1") errs.Add("PublishState does not round-trip");

        // A run missing any required file is not publishable — and says which.
        var partial = Path.Combine(work, "partial");
        Directory.CreateDirectory(Path.Combine(partial, "video"));
        File.WriteAllBytes(Path.Combine(partial, "video", "timeline.mp4"), new byte[4]);
        if (RunPublishSource.IsPublishable(partial)) errs.Add("a run without caption/title reported publishable");
        try { await RunPublishSource.ReadAsync(partial, "private"); errs.Add("read a partial run"); }
        catch (FileNotFoundException ex) when (ex.Message.Contains("caption.txt")) { }

        f.Add(("P9", "The decision record round-trips, and a run missing caption or title is refused by name",
            errs.Count == 0, errs.Count == 0 ? "publish.json round-trips; partial run refused naming caption.txt" : string.Join("; ", errs)));
    }

    // ── P10 ──────────────────────────────────────────────────────────────────

    private static Task DoP10(string work, ILoggerFactory lf, List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();
        var svc = new MusicService(new NullFfmpeg(), null, null, required: true, lf.CreateLogger<MusicService>());

        // Meta is exactly Instagram and Facebook; everyone else is their own
        // family, so a platform added later never inherits another's tracks.
        if (svc.FamilyOf("youtube") != "youtube") errs.Add("youtube family");
        foreach (var p in new[] { "instagram", "facebook" })
            if (svc.FamilyOf(p) != "meta") errs.Add($"{p} should be meta");
        if (svc.FamilyOf("TikTok") != "tiktok") errs.Add("a new platform did not get its own family — it fell into meta");

        // Folders from config, per family; a family with no entry defaults
        // to data/music/{family}.
        var configured = new MusicService(new NullFfmpeg(),
            new Dictionary<string, string> { ["YouTube"] = Path.Combine(work, "yt"), ["Meta"] = Path.Combine(work, "mt") },
            null, true, lf.CreateLogger<MusicService>());
        if (configured.LibraryDir("youtube") != Path.Combine(work, "yt")) errs.Add("YouTube folder from config not used (case-insensitive key)");
        if (configured.LibraryDir("meta") != Path.Combine(work, "mt")) errs.Add("Meta folder from config not used");
        if (configured.LibraryDir("tiktok") != Path.Combine(MusicService.DefaultRoot, "tiktok")) errs.Add("a family without an entry did not default to data/music/{family}");

        // Deterministic for a run id; drains the unused set before repeating.
        var files = Enumerable.Range(1, 5).Select(i => $"/lib/t{i}.mp3").ToList();
        var a = MusicService.Pick(files, "run-x", new HashSet<string>());
        var b = MusicService.Pick(files, "run-x", new HashSet<string>());
        if (a != b) errs.Add("pick is not deterministic for the same run id");

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var heard = new HashSet<string>();
        for (var i = 0; i < 5; i++)
        {
            var pick = MusicService.Pick(files, $"run-{i}", used);
            heard.Add(pick);
            used.Add(Path.GetFileName(pick));
        }
        if (heard.Count != 5) errs.Add($"after 5 runs over 5 tracks only {heard.Count} distinct were heard — unused set not drained first");
        var sixth = MusicService.Pick(files, "run-6", used);
        if (!files.Contains(sixth)) errs.Add("with everything used, the next lap did not restart over the full set");

        // The offset stays inside the track and clear of its tail.
        var off = MusicService.StartOffsetFor("run-x", 240, 16);
        if (off < 0 || off > 240 - 16 - 1) errs.Add($"start offset {off} outside the usable range");
        if (MusicService.StartOffsetFor("run-x", 10, 16) != 0) errs.Add("a track shorter than the clip should start at 0");

        f.Add(("P10", "Meta is exactly Instagram and Facebook and every other platform is its own family; folders come from Publish:Music per family with data/music/{family} defaults; a track is picked deterministically from the unused set first, and the start offset stays inside the track",
            errs.Count == 0, errs.Count == 0 ? "instagram/facebook→meta, youtube and a future tiktok each their own; config folders used, missing → data/music/{family}; 5 of 5 heard before a repeat; offset in range" : string.Join("; ", errs)));
        return Task.CompletedTask;
    }

    // ── P11 ──────────────────────────────────────────────────────────────────

    // Real ffmpeg: a generated 3-second silent clip and a 6-second tone, muxed;
    // the output is probed for an audio stream of the clip's length. Skipped
    // as a FAIL if ffmpeg is not installed — this is the one check whose
    // subject is the actual mux.
    private static async Task DoP11(string work, ILoggerFactory lf, List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();
        var ffmpeg = new FfmpegProvider(lf.CreateLogger<FfmpegProvider>());
        var lib = Path.Combine(work, "music", "youtube");
        Directory.CreateDirectory(lib);
        var clip  = Path.Combine(work, "clip", "video", "timeline.mp4");
        var track = Path.Combine(lib, "tone.mp3");
        Directory.CreateDirectory(Path.GetDirectoryName(clip)!);

        var made = await Shell("ffmpeg", $"-y -f lavfi -i color=c=black:s=64x64:d=3 -f lavfi -i anullsrc=r=48000:cl=stereo -t 3 -c:v libx264 -pix_fmt yuv420p -an \"{clip}\"")
                && await Shell("ffmpeg", $"-y -f lavfi -i sine=frequency=440:duration=6 -metadata title=\"Tone (CC-BY)\" -metadata artist=\"Smoke\" -metadata copyright=cc-by -c:a libmp3lame \"{track}\"");
        if (!made)
        {
            f.Add(("P11", "A music bed is muxed under a silent clip by real ffmpeg: audio stream present, clip length kept, video copied", false, "ffmpeg not available to build the fixtures"));
            return;
        }

        var svc = new MusicService(ffmpeg, new Dictionary<string, string> { ["youtube"] = lib }, null, required: true, lf.CreateLogger<MusicService>());
        var request = SampleRequest(clip) with { Video = new Video("clip-1", Array.Empty<string>(), clip, "2026-01-01T00:00:00Z") };
        var (withMusic, trackFile) = await svc.WithMusicAsync("youtube", request);

        if (trackFile != "tone.mp3") errs.Add($"track file: {trackFile}");
        if (!withMusic.Video.FilePath.EndsWith("timeline.youtube.mp4")) errs.Add($"muxed path: {withMusic.Video.FilePath}");
        if (!File.Exists(withMusic.Video.FilePath)) errs.Add("muxed file not written");
        if (!withMusic.Caption.Description.EndsWith("Music: Tone by Smoke (CC BY)")) errs.Add($"credit: '{withMusic.Caption.Description.Split('\n').Last()}' — licence not normalised or (CC-BY) not stripped from the title");
        if (File.Exists(clip) && new FileInfo(clip).Length == 0) errs.Add("the silent master was touched");

        var streams = await ShellOut("ffprobe", $"-v error -show_entries stream=codec_type -of csv=p=0 \"{withMusic.Video.FilePath}\"");
        if (!streams.Contains("audio")) errs.Add("no audio stream in the muxed file");
        if (!streams.Contains("video")) errs.Add("video stream lost");
        var dur = await ffmpeg.ProbeDurationAsync(withMusic.Video.FilePath);
        if (Math.Abs(dur - 3.0) > 0.3) errs.Add($"muxed duration {dur:F2}s, clip was 3s — the bed was not trimmed to the picture");

        // Second call reuses the file rather than re-encoding.
        var stamp = File.GetLastWriteTimeUtc(withMusic.Video.FilePath);
        await svc.WithMusicAsync("youtube", request);
        if (File.GetLastWriteTimeUtc(withMusic.Video.FilePath) != stamp) errs.Add("second call re-muxed the same family");

        f.Add(("P11", "A music bed is muxed under a silent clip by real ffmpeg: audio stream present, clip length kept, video copied, credit built from the tags, second call reuses the file",
            errs.Count == 0, errs.Count == 0 ? $"timeline.youtube.mp4 {dur:F2}s with audio; credit 'Music: Tone by Smoke (CC BY)'; master untouched; reused on repeat" : string.Join("; ", errs)));
    }

    // ── P12 ──────────────────────────────────────────────────────────────────

    private static async Task DoP12(string work, ILoggerFactory lf, List<(string, string, bool?, string)> f)
    {
        var errs = new List<string>();
        var empty = Path.Combine(work, "music-empty");
        Directory.CreateDirectory(Path.Combine(empty, "meta"));
        File.WriteAllText(Path.Combine(empty, "meta", "README.txt"), "not a track");

        var metaOnly = new Dictionary<string, string> { ["meta"] = Path.Combine(empty, "meta") };
        var required = new MusicService(new NullFfmpeg(), metaOnly, null, required: true, lf.CreateLogger<MusicService>());
        var request  = SampleRequest(Path.Combine(work, "p12.mp4"));
        try { await required.WithMusicAsync("meta", request); errs.Add("an empty library published silent"); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("meta")) { }

        var optional = new MusicService(new NullFfmpeg(), metaOnly, null, required: false, lf.CreateLogger<MusicService>());
        var (same, track) = await optional.WithMusicAsync("meta", request);
        if (track.Length != 0 || same.Video.FilePath != request.Video.FilePath) errs.Add("optional music with an empty library should pass the request through");

        // A README in the folder is not a track; an .mp4 — Meta Sound
        // Collection's container — is.
        if (required.Files("meta").Count != 0) errs.Add("a .txt was counted as a track");
        File.WriteAllBytes(Path.Combine(empty, "meta", "Civil Twilight.mp4"), new byte[] { 0, 0, 0, 0x18 });
        if (required.Files("meta").Count != 1) errs.Add("an .mp4 audio file was not counted as a track");

        // A numeric title tag (Meta's track id) is not a credit; the file
        // name is used instead.
        var tagged = new MusicService(new TaggedFfmpeg(new Dictionary<string, string> { ["title"] = "732138739084979" }),
            metaOnly, null, true, lf.CreateLogger<MusicService>());
        var credit = await tagged.CreditAsync(Path.Combine(empty, "meta", "Civil Twilight.mp4"));
        if (credit != "Music: Civil Twilight") errs.Add($"numeric title tag leaked into the credit: '{credit}'");

        // The ledger: a publish.json under runs/ that names a track makes it used.
        var runs = Path.Combine(work, "ledger-runs", "r1");
        Directory.CreateDirectory(runs);
        File.WriteAllText(Path.Combine(runs, RunPublishSource.PublishFileName),
            JsonSerializer.Serialize(new PublishState("published", "2026-01-01T00:00:00Z", Array.Empty<Publication>(), null,
                new Dictionary<string, string> { ["youtube"] = "t3.mp3" }), Json));
        var ledgered = new MusicService(new NullFfmpeg(), metaOnly, Path.Combine(work, "ledger-runs"), true, lf.CreateLogger<MusicService>());
        var used = ledgered.UsedTracks("youtube");
        if (!used.Contains("t3.mp3")) errs.Add("ledger did not read the track from publish.json");
        if (ledgered.UsedTracks("meta").Count != 0) errs.Add("ledger leaked a youtube track into meta");

        f.Add(("P12", "An empty library refuses to publish silent when music is required and passes through when not; a README is not a track; the ledger reads used tracks per family from publish.json",
            errs.Count == 0, errs.Count == 0 ? "required → refused naming meta; optional → passed through; .txt ignored, .mp4 counted; numeric title → file name; ledger per family" : string.Join("; ", errs)));
    }

    private static async Task<bool> Shell(string exe, string args)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(exe, args) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null) return false;
            await p.WaitForExitAsync();
            return p.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    private static async Task<string> ShellOut(string exe, string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(exe, args) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        using var p = System.Diagnostics.Process.Start(psi)!;
        var outText = await p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();
        return outText;
    }

    private static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, f));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(f, target, overwrite: true);
        }
    }

    // A missing record is a finding, not a crash: the check has to report a
    // loop that never completed its item, which is exactly when the file
    // does not exist.
    private static bool RecordSays(string runFolder, string status)
    {
        var path = Path.Combine(runFolder, RunPublishSource.PublishFileName);
        return File.Exists(path) && File.ReadAllText(path).Contains($"\"{status}\"");
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    private static HttpResponseMessage Json200(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed record Seen(string Path, string Url, string Body, string? Authorization, string Headers);

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public List<Seen> Requests { get; } = new();
        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            var headers = string.Join(";", request.Headers.Select(h => $"{h.Key}={string.Join(",", h.Value)}"));
            Requests.Add(new Seen(request.RequestUri!.AbsolutePath, request.RequestUri.ToString(), body,
                request.Headers.Authorization?.ToString(), headers));
            return _respond(request);
        }
    }

    private sealed class FakeChannel : IReviewChannel
    {
        private readonly Queue<ReviewDecision> _pending = new();
        private long _next = 100;
        public List<string> Sent { get; } = new();
        public List<string> Reports { get; } = new();
        public void Queue(ReviewDecision d) => _pending.Enqueue(d);

        public Task<long> SendForReviewAsync(ReviewItem item, PublishRequest request, CancellationToken ct = default)
        { Sent.Add(item.Id); return Task.FromResult(_next++); }

        public Task<IReadOnlyList<ReviewDecision>> PollDecisionsAsync(TimeSpan timeout, CancellationToken ct = default)
        {
            var all = new List<ReviewDecision>();
            while (_pending.Count > 0) all.Add(_pending.Dequeue());
            return Task.FromResult<IReadOnlyList<ReviewDecision>>(all);
        }

        public Task ReportAsync(ReviewItem item, string text, CancellationToken ct = default)
        { Reports.Add(text); return Task.CompletedTask; }
    }

    private sealed class FakePublisher : IPublishService
    {
        public int Calls { get; private set; }
        public PublishRequest? LastRequest { get; private set; }
        public IReadOnlyList<string> Targets { get; }
        public FakePublisher(IReadOnlyList<string> targets) => Targets = targets;
        public Task<PublishState> PublishAsync(PublishRequest request, IReadOnlyList<string>? only = null, CancellationToken ct = default)
        {
            Calls++;
            LastRequest = request;
            return Task.FromResult(new PublishState("published", "2026-01-01T00:00:00Z",
                Targets.Select(t => new Publication("p", request.Video.Id, request.Caption.Id, t, $"https://fake/{t}/{Calls}", "2026-01-01T00:00:00Z")).ToList()));
        }
    }

    private sealed class FakeStorage : IPublicStorage
    {
        private readonly List<string> _log;
        public string? LastUploaded { get; private set; }
        public FakeStorage(List<string> log) => _log = log;
        public Task<string> UploadPublicAsync(string localPath, string remoteName, CancellationToken ct = default)
        { _log.Add("storage"); LastUploaded = localPath; return Task.FromResult("https://dl.fake/" + remoteName); }
    }

    // Stands in for the mux: renames the path and appends a credit, so the
    // service's ordering and plumbing can be asserted without ffmpeg.
    private sealed class FakeMusic : IMusicService
    {
        private readonly List<string> _log;
        public FakeMusic(List<string> log) => _log = log;
        public string FamilyOf(string platform) => platform is "instagram" or "facebook" ? "meta" : platform;
        public Task<(PublishRequest Request, string TrackFile)> WithMusicAsync(string family, PublishRequest request, CancellationToken ct = default)
        {
            _log.Add("music:" + family);
            var muxed = Path.ChangeExtension(request.Video.FilePath, $".{family}.mp4");
            return Task.FromResult((request with
            {
                Video   = request.Video with { FilePath = muxed },
                Caption = request.Caption with { Description = request.Caption.Description + "\n\nMusic: fake (" + family + ")" },
            }, $"fake-{family}.mp3"));
        }
    }

    private sealed class TaggedFfmpeg : IFfmpegProvider
    {
        private readonly Dictionary<string, string> _tags;
        public TaggedFfmpeg(Dictionary<string, string> tags) => _tags = tags;
        public Task<Video?> ComposeAsync(IReadOnlyList<HistoricalImage> images, string outputPath) => Task.FromResult<Video?>(null);
        public Task<double> ProbeDurationAsync(string path) => Task.FromResult(16.0);
        public Task<IReadOnlyDictionary<string, string>> ProbeTagsAsync(string path) => Task.FromResult<IReadOnlyDictionary<string, string>>(_tags);
        public Task MuxMusicAsync(string videoPath, string trackPath, double startSeconds, string outputPath) => Task.CompletedTask;
    }

    private sealed class NullFfmpeg : IFfmpegProvider
    {
        public Task<Video?> ComposeAsync(IReadOnlyList<HistoricalImage> images, string outputPath) => Task.FromResult<Video?>(null);
        public Task<double> ProbeDurationAsync(string path) => Task.FromResult(16.0);
        public Task<IReadOnlyDictionary<string, string>> ProbeTagsAsync(string path) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
        public Task MuxMusicAsync(string videoPath, string trackPath, double startSeconds, string outputPath) => Task.CompletedTask;
    }

    private sealed class FakeTarget : IPublishTarget
    {
        private readonly List<string> _log;
        private readonly bool _requireUrl;
        public bool Fail { get; init; }
        public string Platform { get; }
        public PublishRequest? LastRequest { get; private set; }
        public FakeTarget(string platform, List<string> log, bool requireUrl) { Platform = platform; _log = log; _requireUrl = requireUrl; }
        public Task<Publication> PublishAsync(PublishRequest request, CancellationToken ct = default)
        {
            _log.Add(Platform);
            LastRequest = request;
            if (_requireUrl && request.PublicVideoUrl is null) throw new InvalidOperationException("no url");
            if (Fail) throw new InvalidOperationException("boom");
            return Task.FromResult(new Publication("p", request.Video.Id, request.Caption.Id, Platform, $"https://{Platform}/1", "2026-01-01T00:00:00Z"));
        }
    }
}
