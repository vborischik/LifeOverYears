# Video Smoke Test Report

Generated: 2026-07-20T04:06:05.0552860+00:00

## Check Results

| Check | Description | Status | Detail |
|-------|-------------|--------|--------|
| V5 | ffmpeg and ffprobe binaries found in PATH | ✅ PASS | Both binaries responded to -version |
| O1 | stamped/{year}.png exists for every test year, same dimensions as source | ✅ PASS | All stamped images present with matching dimensions |
| O2 | stamped output file size differs from the un-stamped source | ✅ PASS | All stamped files differ in size from their source |
| V1 | Video file exists and has non-zero size | ✅ PASS | 408184 bytes at output/smoke-video/video/timeline.mp4 |
| V2 | Video resolution == 1080x1920 | ✅ PASS | actual: 1080x1920 |
| V3 | Duration is 16s ± 0.5s (fixed target; per-frame hold computed dynamically for 6 frames) | ✅ PASS | actual: 16.00s |
| V4 | codec_name == h264, pix_fmt == yuv420p | ✅ PASS | actual: codec_name=h264, pix_fmt=yuv420p |
| V6 | ffmpeg command used filter_complex xfade with a radial transition (not concat) | ✅ PASS | ffmpeg command: -y -loop 1 -t 4.3333 -i "/Users/vlad/Projects/LifeOverYears/src/LifeOverYears/output/smoke-video/stamped/1975.png" -loop 1 -t 4.3333 -i "/Users/vlad/Projects/LifeOverYears/src/LifeOverYears/output/smoke-video/stamped/1985.png" -loop 1 -t 4.3333 -i "/Users/vlad/Projects/LifeOverYears/src/LifeOverYears/output/smoke-video/stamped/1995.png" -loop 1 -t 4.3333 -i "/Users/vlad/Projects/LifeOverYears/src/LifeOverYears/output/smoke-video/stamped/2005.png" -loop 1 -t 4.3333 -i "/Users/vlad/Projects/LifeOverYears/src/LifeOverYears/output/smoke-video/stamped/2015.png" -loop 1 -t 4.3333 -i "/Users/vlad/Projects/LifeOverYears/src/LifeOverYears/output/smoke-video/stamped/2025.png" -filter_complex "[0:v]scale=1080:1920:force_original_aspect_ratio=decrease,pad=1080:1920:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=30,format=yuv420p[v0];[1:v]scale=1080:1920:force_original_aspect_ratio=decrease,pad=1080:1920:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=30,format=yuv420p[v1];[2:v]scale=1080:1920:force_original_aspect_ratio=decrease,pad=1080:1920:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=30,format=yuv420p[v2];[3:v]scale=1080:1920:force_original_aspect_ratio=decrease,pad=1080:1920:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=30,format=yuv420p[v3];[4:v]scale=1080:1920:force_original_aspect_ratio=decrease,pad=1080:1920:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=30,format=yuv420p[v4];[5:v]scale=1080:1920:force_original_aspect_ratio=decrease,pad=1080:1920:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=30,format=yuv420p[v5];[v0][v1]xfade=transition=radial:duration=2:offset=2.3333[x1];[x1][v2]xfade=transition=radial:duration=2:offset=4.6667[x2];[x2][v3]xfade=transition=radial:duration=2:offset=7[x3];[x3][v4]xfade=transition=radial:duration=2:offset=9.3333[x4];[x4][v5]xfade=transition=radial:duration=2:offset=11.6667[outv]" -map "[outv]" -c:v libx264 -pix_fmt yuv420p -r 30 "output/smoke-video/video/timeline.mp4" |
| O3 | Partial year list [1985, 2015] only waits for/stamps those years and produces a 2-frame video | ✅ PASS | stamped exactly [2015, 1985]; duration 16.00s |
| O4 | Overlap guard fires exactly per PlanTimeline: never for n=2, and for n=6 iff the plan was adjusted | ✅ PASS | n=6 warned=False (plan adjusted=False); n=2 warned=False |

