# 13 — Publishing connectors

Five platform connectors, written and compiling, **not wired**: nothing in
`AppModule` registers one and no CLI mode calls one. This is the groundwork
for a `publish` mode that is a separate change. Traced from the code 11 Sep
2026.

## The shape

```
IPublishTarget            one platform; PublishAsync(PublishRequest) → Publication
  TelegramProvider        Bot API sendVideo — takes the bytes
  YouTubeProvider         Data API v3 via Google SDK — takes the bytes
  InstagramProvider       Graph API Reels — pulls from a URL
  FacebookProvider        Graph API Page Reels — pulls from a URL
IPublicStorage            the URL those two pull from
  DropboxProvider         upload → shared link → direct-download URL
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

Config keys are laid out in `appsettings.example.json` under `Publish:` —
documented, not yet read by anything.

## Sequencing, when the publish mode is written

Instagram and Facebook cannot start without `PublicVideoUrl`, and they say so
rather than fail three calls in. So a full publish is: Dropbox first, then
the four targets in any order. Telegram is the cheapest end-to-end test —
one multipart POST, no storage, no OAuth.

Privacy is never defaulted. `PublishRequest.Privacy` is YouTube's word
(`private` / `unlisted` / `public`); Facebook maps it to `DRAFT` /
`PUBLISHED` (or `SCHEDULED` with a time); Telegram and Instagram have no
private state and publish on call. The safe pattern from YoutubePublisher
holds: upload `private` with a schedule, and the platform flips each video
public on its own clock.

## Not done

- **No smoke suite.** Every provider takes an `HttpClient`, so a fake handler
  drops in the way `BatchSmokeTest` does it — but `--smoke-publish` is a CLI
  flag, and that is wiring. `YouTubeProvider.BuildVideo` and
  `InstagramProvider.BuildCaption` are static and checkable without a token.
- **No orchestrator.** Reading a run folder into a `PublishRequest`
  (`video/timeline.mp4`, `caption.txt`, `title.txt`, `images/2025.png`) and
  running the targets is the publish mode itself.
- **No upload ledger** for the quota governor.
- **Instagram cover image.** The container can take a `cover_url`; it would
  need the thumbnail on Dropbox too. Instagram's own first-frame pick is the
  2025 frame already, so this was left out.
