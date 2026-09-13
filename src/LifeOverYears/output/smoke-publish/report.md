# Publish Smoke Test Report

| Check | Description | Status | Detail |
|---|---|---|---|
| P1 | A run folder reads into a PublishRequest: title, body and hashtags split back from caption.txt, the newest frame as cover | ✅ PASS | title, 2-line body, 3 tags, images/2025.png cover |
| P2 | The review queue copies only what publishing needs, is idempotent, records the decision on the original run, deletes the copy, and never re-queues a decided run | ✅ PASS | copy = video+caption+title+cover+review.json; publish.json written back; copy deleted; hook inert when off |
| P3 | Review buttons carry the item id in their callback data, and Telegram links drop the -100 namespace prefix | ✅ PASS | publish:/skip: callback data; @name and -100 link forms |
| P4 | The review loop sends each item once, survives a restart without re-sending, skips on no, publishes on yes, ignores a decision for another message, and reports the outcome | ✅ PASS | sent once; skip → skipped, no publish; text yes → published, URL reported; foreign decision ignored |
| P5 | Dropbox mints an access token from the refresh token once, uploads with it as overwrite, and turns the shared link — including the 409 already-exists reply — into a direct-download URL | ✅ PASS | one token call for two uploads; bearer set; both link shapes rewritten; 409 handled |
| P6 | Instagram refuses without a public URL before any call, enforces the caption limits, keeps the token in the body, and returns the permalink | ✅ PASS | no-URL refused with zero requests; 31 tags and 2300 chars rejected; token in body; permalink returned |
| P7 | The Telegram review channel accepts buttons and typed yes/no from the reviewer's chat only, and ignores strangers and chatter | ✅ PASS | button → approve(55); stranger ignored; да → approve; no → skip; chatter ignored |
| P8 | PublishService runs storage once before the URL targets, lets one failing target not stop the others, records what succeeded, and refuses misconfiguration at construction | ✅ PASS | storage>telegram>instagram>facebook; 2 published, status failed naming facebook; bad config refused |
| P9 | The decision record round-trips, and a run missing caption or title is refused by name | ✅ PASS | publish.json round-trips; partial run refused naming caption.txt |
