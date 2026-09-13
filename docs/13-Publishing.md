# 13 — Publishing

Five platform connectors, a publish service over them, and a review loop
that puts each finished video in front of a person on Telegram before
anything is posted. Traced from the code 12 Sep 2026.

## The flow

```
run / brand / collect finishes
        │  (Publish:Enabled=true)
        ▼
output/on-review/{run}/         ← a copy of video + caption + title + cover
        │
   review mode                  ← the loop, one item at a time
        │  sendVideo to your private chat, buttons [✅ Publish] [⏭ Skip]
        ▼
   your answer                  ← button, or typed yes/no/да/нет
        │
   ┌────┴────┐
 skip      publish → Dropbox (public URL) → Instagram [→ others in Targets]
   │           │
   └─────┬─────┘
         ▼
{run}/publish.json written      ← status, URLs, or the error
output/on-review/{run}/ deleted ← the folder is the queue; gone = decided
```

The original run folder is never touched by the queue except to receive
`publish.json`; a run that carries one is never queued again.

## Modes

```
cd src/LifeOverYears
dotnet run -- publish <runFolder>          # copy it into on-review/ (queue it)
dotnet run -- publish <runFolder> --yes    # post it NOW, no Telegram — the test mode
dotnet run -- review                       # the loop; Ctrl+C stops it
dotnet run -- --smoke-publish              # P1–P9, offline
```

`Publish:Enabled` gates the two automatic paths — the end-of-run enqueue
and the `review` loop. `publish <runFolder> --yes` works regardless: the
folder and the flag were both typed by a person, and that is consent. It
is how Dropbox + Instagram get proven on one existing run before the loop
is switched on.

Both modes build their container from `PublishModule` alone — no
generation keys needed, and `AppModule` never loads a publish provider.

## First contact with the bot

1. In Telegram, talk to **@BotFather** → `/newbot` → name it → copy the
   token into `Publish:Telegram:BotToken`.
2. Open your new bot in Telegram (BotFather gives a `t.me/…` link) and
   send it any message. Until you do, the bot cannot message you.
3. `dotnet run -- review` — with `ReviewChatId` still empty this is not the
   loop: it listens for one message, prints
   `Your chat id is NNNNN`, and stops.
4. Put that number in `Publish:Telegram:ReviewChatId`. From then on only
   that chat's answers count; a stranger who finds the bot and presses
   Publish is ignored (P7).

## Testing by hand, YouTube first

YouTube takes the file directly — no Dropbox — and `Publish:Privacy` is
`private` by default, so nothing goes public until you flip it in Studio.
The title is `title.txt`, checked against YouTube's 100-char limit before
upload (over it, the publish refuses rather than truncates); the
description is the caption body plus the hashtag line; the tags are the
hashtags without `#`.

```json
"Publish": {
  "Enabled": false,
  "Targets": [ "youtube" ],
  "YouTube": { "ClientSecretPath": "../../../YoutubePublisher/perm.json" },
  "Telegram": { "BotToken": "…", "ReviewChatId": "…" }
}
```

Pointing `ClientSecretPath` at YoutubePublisher's file reuses the OAuth
consent already granted on this machine; a different client secret opens
the browser once for a new consent.

**Straight to YouTube, no bot** (proves the upload and the title):

```
dotnet run -- publish output/runs/circuit-city_20260911-1215 --yes
```

**Through the bot** (proves the loop):

```
dotnet run -- publish output/runs/circuit-city_20260911-1215   # queue it
# set Publish:Enabled to true
dotnet run -- review                                            # the bot sends you the video
# press ✅ Publish (or type yes) → uploads to YouTube private → replies with the URL
```

**A video that is not a run:** the modes read a folder, not a file. Three
files make one:

```
myvideo/video/timeline.mp4
myvideo/caption.txt      body, blank line, one #hashtag per line
myvideo/title.txt        one line, under 100 chars
```

Then `publish myvideo --yes` or `publish myvideo` + `review`. A cover is
optional (`images/{year}.png`, newest year wins).

The loop persists its `getUpdates` offset in `on-review/.telegram-offset`,
so a restart does not re-read old answers, and an item already sent is not
sent again (P4) — the message id is in its `review.json`.

## The shape

```
IPublishTarget            one platform; PublishAsync(PublishRequest) → Publication
  TelegramProvider        Bot API sendVideo to a CHANNEL — takes the bytes
  YouTubeProvider         Data API v3 via Google SDK — takes the bytes
  InstagramProvider       Graph API Reels — pulls from a URL
  FacebookProvider        Graph API Page Reels — pulls from a URL
IPublicStorage            the URL those two pull from
  DropboxProvider         upload → shared link → direct-download URL
IReviewChannel            the human in the loop
  TelegramReviewProvider  sendVideo to YOUR chat with buttons; getUpdates long-poll
IPublishService           storage once, then every target in Publish:Targets
  PublishService          one failing target does not stop the others; state records both
ReviewQueue               output/on-review/ — copy in, review.json, publish.json back, delete
ReviewLoop                one item at a time: send → wait → act → next
RunPublishSource          a run folder → PublishRequest (the one definition)
```

`PublishRequest` (Models) carries the `Video`, the `Caption`, an optional
thumbnail, the privacy word, an optional scheduled time, and — filled in by
the storage step — the public video URL. `Publication` (already in Models) is
what every target returns: platform, URL, timestamps.

`PublishText` (Services) is the one definition of "body, then a line of
hashtags", so every platform assembles the caption the same way.

## Where each connector came from

| connector | source | changed on the way |
|---|---|---|
| Telegram | this repo, Jun 2026 sketch | `parse_mode=HTML` on an unescaped caption dropped (an `&` would have rejected the post); `t.me/c/` link no longer built from the `-100` chat id; a caption over 1024 chars goes as title+tags with the body as a reply, never cut |
| Dropbox | `HouseTimelineApp/Features/Storage/DropboxStorageProvider.cs` | refresh-token auth added — access tokens live four hours, which is why that project has hard-coded ones in commented-out source; `files/upload` direct instead of a temp link (a 1 MB clip is two orders under the 150 MB single-request limit) |
| Instagram | `HouseTimelineApp/Features/Instagram/InstagramApiClient.cs` | token moved out of the query string into the form body (a URL with the token in it ends up in every log line); polling bounded at 5 min; caption limits (2200 chars / 30 hashtags) enforced before the container is created, since an orphaned container counts against the daily quota; permalink fetched after publish |
| Facebook | written from the Graph API reference — neither source project posts to Facebook | Page **Reels** (`video_reels` start → upload-by-URL → finish), not the plain Page video endpoint: a 9:16 sixteen-second clip is a Reel, and that is the surface Facebook shows one on |
| YouTube | `YoutubePublisher/YoutubeUploader.cs` — the version that shipped a month of daily uploads | `BuildVideo` rules kept exactly; `publishAt` on a non-private video is now a caller error (YouTube rejects it anyway); thumbnail failure is a warning, not a failed publish |

`PublishSchedule` and `QuotaGovernor` come across from YoutubePublisher as
`Services/`. The governor takes today's upload count from whoever keeps the
ledger — the state file that project had is not ported.

## What each platform needs

| platform | credential | how to get it |
|---|---|---|
| Telegram | bot token + chat id | BotFather for the token; the bot must be an admin of the channel. Chat id is `@name` or the `-100…` id. |
| Dropbox | app key, app secret, **refresh token** | App console → create app with `files.content.write` + `sharing.write`; run the OAuth flow once with `token_access_type=offline` to get a refresh token. An access token alone works for four hours. |
| Instagram | long-lived user token + IG user id | Facebook Developer app with `instagram_content_publish`; the account must be professional and linked to a Facebook Page. The id is numeric, not the username. |
| Facebook | **Page** access token + Page id | Same app, `pages_manage_posts` + `publish_video`; exchange the user token for the Page token via `/me/accounts`. |
| YouTube | `client_secret.json` | Google Cloud project with YouTube Data API v3; OAuth desktop client. First run opens a browser once; consent is cached in `~/.local/share/google-filedatastore` under the key `user` and reused as long as the client secret is the same. |

Config keys are in `appsettings.example.json` under `Publish:`, each with a
`_` note. The ones the loop needs today: `Enabled`, `Targets`, `Privacy`,
`Telegram:BotToken`, `Telegram:ReviewChatId`, `Dropbox:*`, `Instagram:*`.

## Sequencing

`PublishService` runs storage first, once, then the targets in `Targets`
order. Instagram and Facebook refuse without `PublicVideoUrl` before making
a call (P6). A target that fails is recorded in `publish.json` as
`failed` with the error naming it, alongside whatever did succeed (P8) —
the reviewer gets both in the reply.

Privacy is never defaulted. `PublishRequest.Privacy` is YouTube's word
(`private` / `unlisted` / `public`); Facebook maps it to `DRAFT` /
`PUBLISHED` (or `SCHEDULED` with a time); Telegram and Instagram have no
private state and publish on call. The safe pattern from YoutubePublisher
holds: upload `private` with a schedule, and the platform flips each video
public on its own clock.

## Music

Laid down by `publish` and by nothing else. The run's `timeline.mp4` is the
silent master; publishing writes `timeline.{family}.mp4` beside it and posts
that. Two families, two libraries, because two licences:

| family | platforms | library |
|---|---|---|
| `youtube` | youtube | `data/music/youtube/` — 14 CC-BY tracks from YoutubePublisher |
| `meta` | instagram, facebook, telegram | `data/music/meta/` — **empty until Meta-cleared tracks are dropped in** |

`Publish:Music:Required` is `true`: a family with an empty library is
refused, not published silent (P12). So today YouTube publishes and a Meta
target reports "no music" until `data/music/meta/` has files in it.

The table above is the rule in code, and it applies whenever the config
says nothing. Two optional sections override it entry by entry:

```json
"Music": {
  "Families":  { "telegram": "youtube" },          // platform → family
  "Libraries": { "meta": "/Volumes/music/meta" }   // family → folder
}
```

An entry not mentioned keeps the default — `instagram` stays `meta`,
`youtube` stays under `{Dir}/youtube` (P10). Move a platform only if its
library is cleared for it; the split exists because of licences.

The track is picked by hash of the run id from the tracks this family has
not used yet — every track is heard once before any repeats — and the
ledger is the `Music` field of every `publish.json` under `output/runs/`
(P10, P12). The bed starts at a hash-derived offset into the track, is
trimmed to the picture, faded 0.5 s at both ends and normalised to −14 LUFS;
the video stream is copied, never re-encoded (P11, real ffmpeg). The
track's credit line — `Music: Reawakening by Scott Buckley (CC BY)`, built
from the file's own tags — is appended to the description on every
platform, because a CC-BY track without attribution is a licence breach.

## Checks — `--smoke-publish`, P1–P12

| | |
|---|---|
| P1 | a run folder reads into a request: title, body and tags split back from `caption.txt`, newest frame as cover |
| P2 | queue copies only what publishing needs, is idempotent, writes `publish.json` back, deletes the copy, never re-queues |
| P3 | button callback data carries the item id; Telegram links drop the `-100` prefix |
| P4 | loop sends once, survives restart without re-sending, skip/yes act correctly, a decision for another message is ignored |
| P5 | Dropbox mints one token per refresh, uploads as overwrite, rewrites both link shapes, handles the 409 |
| P6 | Instagram refuses without URL before any call, enforces limits, token in body, permalink returned |
| P7 | review channel accepts buttons and typed yes/no from the reviewer only |
| P8 | storage first, one failure does not stop the rest, empty/unknown targets refused at construction |
| P9 | `publish.json` round-trips; a partial run is refused by name |

| P10 | platform → family; deterministic pick from the unused set first; offset inside the track |
| P11 | **real ffmpeg**: a generated clip and tone muxed; audio present, length kept, video copied, credit from tags, second call reuses the file |
| P12 | empty library refused when required, passed through when not; a README is not a track; ledger per family |

P4, P7, P11 and P12 were proven able to fail by reintroducing the bugs
they describe.

## Not done

- **Meta music library is empty** — drop cleared tracks into `data/music/meta/`.
- **Only one decision per run** — all configured targets, or none.
- **No upload ledger** for `QuotaGovernor`; YouTube is not in `Targets` yet.
- **Instagram cover image.** The container can take a `cover_url`; it would
  need the thumbnail on Dropbox too. Instagram's own first-frame pick is the
  2025 frame already.
- **No reminder** for an item the reviewer has not answered; it waits.
