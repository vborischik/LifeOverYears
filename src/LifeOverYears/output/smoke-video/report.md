# Video Smoke Test Report

Generated: 2026-07-27T13:43:38.1035791+00:00

## Check Results

| Check | Description | Status | Detail |
|-------|-------------|--------|--------|
| V5 | ffmpeg and ffprobe binaries found in PATH | ❌ FAIL | ffmpeg not found in PATH — missing: ffmpeg, ffprobe |
| O1 | stamped/{year}.png exists for every test year, same dimensions as source | ❌ FAIL | skipped — ffmpeg not found in PATH |
| O2 | stamped output file size differs from the un-stamped source | ❌ FAIL | skipped — ffmpeg not found in PATH |
| V1 | Video file exists and has non-zero size | ❌ FAIL | skipped — ffmpeg not found in PATH |
| V2 | Video resolution == 1080x1920 | ❌ FAIL | skipped — ffmpeg not found in PATH |
| V3 | Duration is 16s ± 0.5s (fixed target; per-frame hold computed dynamically for 6 frames) | ❌ FAIL | skipped — ffmpeg not found in PATH |
| V4 | codec_name == h264, pix_fmt == yuv420p | ❌ FAIL | skipped — ffmpeg not found in PATH |
| V6 | ffmpeg command used filter_complex xfade with a radial transition (not concat) | ❌ FAIL | skipped — ffmpeg not found in PATH |
| O3 | Partial year list [1985, 2015] only waits for/stamps those years and produces a 2-frame video | ❌ FAIL | skipped — ffmpeg not found in PATH |
| O4 | Overlap guard fires exactly per PlanTimeline: never for n=2, and for n=6 iff the plan was adjusted | ❌ FAIL | skipped — ffmpeg not found in PATH |

