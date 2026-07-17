# Video Smoke Test Report

Generated: 2026-07-17T20:25:57.3065260+00:00

## Check Results

| Check | Description | Status | Detail |
|-------|-------------|--------|--------|
| V5 | ffmpeg and ffprobe binaries found in PATH | ❌ FAIL | ffmpeg not found in PATH — missing: ffmpeg, ffprobe |
| V1 | Video file exists and has non-zero size | ❌ FAIL | skipped — ffmpeg not found in PATH |
| V2 | Video resolution == 1080x1920 | ❌ FAIL | skipped — ffmpeg not found in PATH |
| V3 | Duration is 18s ± 0.5s (6 frames × 3s) | ❌ FAIL | skipped — ffmpeg not found in PATH |
| V4 | codec_name == h264, pix_fmt == yuv420p | ❌ FAIL | skipped — ffmpeg not found in PATH |

