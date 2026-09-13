using System.Net;
using System.Text;
using System.Text.Json;
using LifeOverYears.Models;
using LifeOverYears.Providers;
using LifeOverYears.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LifeOverYears.Services;

// P1–P9 over the publish path, offline. The review loop and the publish
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

        // Decided runs never come back.
        if (await queue.EnqueueAsync(run) is not null) errs.Add("a run with publish.json was queued again");

        // The hook is inert when off, and never throws.
        var off = new ReviewQueue(Path.Combine(work, "on-review-off"), autoEnqueue: false, lf.CreateLogger<ReviewQueue>());
        var runB = MakeRun(work, "run-b");
        await off.TryEnqueueAfterRunAsync(runB);
        if (Directory.Exists(Path.Combine(work, "on-review-off"))) errs.Add("AutoEnqueue=false still queued");
        await off.TryEnqueueAfterRunAsync(Path.Combine(work, "does-not-exist"));

        f.Add(("P2", "The review queue copies only what publishing needs, is idempotent, records the decision on the original run, deletes the copy, and never re-queues a decided run",
            errs.Count == 0, errs.Count == 0 ? "copy = video+caption+title+cover+review.json; publish.json written back; copy deleted; hook inert when off" : string.Join("; ", errs)));
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

        if (TelegramProvider.MessageUrl("@chan", 7) != "https://t.me/chan/7") errs.Add("@channel link");
        if (TelegramProvider.MessageUrl("-1001234", 7) != "https://t.me/c/1234/7") errs.Add("-100 link still carries the prefix");

        f.Add(("P3", "Review buttons carry the item id in their callback data, and Telegram links drop the -100 namespace prefix",
            errs.Count == 0, errs.Count == 0 ? "publish:/skip: callback data; @name and -100 link forms" : string.Join("; ", errs)));
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

        f.Add(("P4", "The review loop sends each item once, survives a restart without re-sending, skips on no, publishes on yes, ignores a decision for another message, and reports the outcome",
            errs.Count == 0, errs.Count == 0 ? "sent once; skip → skipped, no publish; text yes → published, URL reported; foreign decision ignored" : string.Join("; ", errs)));
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
            "/2/sharing/create_shared_link_with_settings" => new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = new StringContent("{\"error\":{\"shared_link_already_exists\":{\"metadata\":{\"url\":\"https://www.dropbox.com/scl/fi/q/x.mp4?rlkey=r&dl=0\"}}}}"),
            },
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

        f.Add(("P5", "Dropbox mints an access token from the refresh token once, uploads with it as overwrite, and turns the shared link — including the 409 already-exists reply — into a direct-download URL",
            errs.Count == 0, errs.Count == 0 ? "one token call for two uploads; bearer set; both link shapes rewritten; 409 handled" : string.Join("; ", errs)));
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

        f.Add(("P6", "Instagram refuses without a public URL before any call, enforces the caption limits, keeps the token in the body, and returns the permalink",
            errs.Count == 0, errs.Count == 0 ? "no-URL refused with zero requests; 31 tags and 2300 chars rejected; token in body; permalink returned" : string.Join("; ", errs)));
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
        var telegram  = new FakeTarget("telegram", log, requireUrl: false);
        var failing   = new FakeTarget("facebook", log, requireUrl: true) { Fail = true };
        var file = Path.Combine(work, "p8.mp4");
        File.WriteAllBytes(file, new byte[8]);

        var svc = new PublishService(new[] { "telegram", "instagram", "facebook" },
            new IPublishTarget[] { instagram, telegram, failing }, storage, lf.CreateLogger<PublishService>());
        var state = await svc.PublishAsync(SampleRequest(file));

        if (log.Count == 0 || log[0] != "storage") errs.Add($"storage did not run first: {string.Join(">", log)}");
        if (!log.SequenceEqual(new[] { "storage", "telegram", "instagram", "facebook" })) errs.Add($"order: {string.Join(">", log)}");
        if (state.Status != "failed") errs.Add($"status with one failing target: {state.Status}");
        if (state.Publications.Count != 2) errs.Add($"{state.Publications.Count} publications recorded, expected the two that succeeded");
        if (state.Error is null || !state.Error.Contains("facebook")) errs.Add("error does not name the failing target");

        // Config naming an unknown platform fails at construction, not on
        // the first publish.
        try { _ = new PublishService(Array.Empty<string>(), new IPublishTarget[] { instagram }, storage, lf.CreateLogger<PublishService>()); errs.Add("empty target list accepted"); }
        catch (InvalidOperationException) { }
        try { _ = new PublishService(new[] { "tiktok" }, Array.Empty<IPublishTarget>(), null, lf.CreateLogger<PublishService>()); errs.Add("unknown target accepted"); }
        catch (InvalidOperationException) { }
        try { _ = new PublishService(new[] { "instagram" }, new IPublishTarget[] { instagram }, null, lf.CreateLogger<PublishService>()); errs.Add("URL target without storage accepted"); }
        catch (InvalidOperationException) { }

        f.Add(("P8", "PublishService runs storage once before the URL targets, lets one failing target not stop the others, records what succeeded, and refuses misconfiguration at construction",
            errs.Count == 0, errs.Count == 0 ? "storage>telegram>instagram>facebook; 2 published, status failed naming facebook; bad config refused" : string.Join("; ", errs)));
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
        public IReadOnlyList<string> Targets { get; }
        public FakePublisher(IReadOnlyList<string> targets) => Targets = targets;
        public Task<PublishState> PublishAsync(PublishRequest request, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new PublishState("published", "2026-01-01T00:00:00Z",
                Targets.Select(t => new Publication("p", request.Video.Id, request.Caption.Id, t, $"https://fake/{t}/{Calls}", "2026-01-01T00:00:00Z")).ToList()));
        }
    }

    private sealed class FakeStorage : IPublicStorage
    {
        private readonly List<string> _log;
        public FakeStorage(List<string> log) => _log = log;
        public Task<string> UploadPublicAsync(string localPath, string remoteName, CancellationToken ct = default)
        { _log.Add("storage"); return Task.FromResult("https://dl.fake/" + remoteName); }
    }

    private sealed class FakeTarget : IPublishTarget
    {
        private readonly List<string> _log;
        private readonly bool _requireUrl;
        public bool Fail { get; init; }
        public string Platform { get; }
        public FakeTarget(string platform, List<string> log, bool requireUrl) { Platform = platform; _log = log; _requireUrl = requireUrl; }
        public Task<Publication> PublishAsync(PublishRequest request, CancellationToken ct = default)
        {
            _log.Add(Platform);
            if (_requireUrl && request.PublicVideoUrl is null) throw new InvalidOperationException("no url");
            if (Fail) throw new InvalidOperationException("boom");
            return Task.FromResult(new Publication("p", request.Video.Id, request.Caption.Id, Platform, $"https://{Platform}/1", "2026-01-01T00:00:00Z"));
        }
    }
}
