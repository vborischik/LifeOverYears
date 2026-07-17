# Video Smoke Test Report

Generated: 2026-07-17T20:44:45.5138320+00:00

## Check Results

| Check | Description | Status | Detail |
|-------|-------------|--------|--------|
| V5 | ffmpeg and ffprobe binaries found in PATH | ✅ PASS | Both binaries responded to -version |
| V1 | Video file exists and has non-zero size | ✅ PASS | 361815 bytes at output/smoke-video/video/timeline.mp4 |
| V2 | Video resolution == 1080x1920 | ✅ PASS | actual: 1080x1920 |
| V3 | Duration is 8s ± 0.5s (6×3s holds - 5×2s xfade overlaps) | ✅ PASS | actual: 8.00s |
| V4 | codec_name == h264, pix_fmt == yuv420p | ✅ PASS | actual: codec_name=h264, pix_fmt=yuv420p |
| V6 | ffmpeg command used filter_complex xfade with a radial transition (not concat) | ✅ PASS | ffmpeg command: -y -loop 1 -t 3 -i "/Users/vlad/Projects/LifeOverYears/src/LifeOverYears/output/smoke-video/images/1975.png" -loop 1 -t 3 -i "/Users/vlad/Projects/LifeOverYears/src/LifeOverYears/output/smoke-video/images/1985.png" -loop 1 -t 3 -i "/Users/vlad/Projects/LifeOverYears/src/LifeOverYears/output/smoke-video/images/1995.png" -loop 1 -t 3 -i "/Users/vlad/Projects/LifeOverYears/src/LifeOverYears/output/smoke-video/images/2005.png" -loop 1 -t 3 -i "/Users/vlad/Projects/LifeOverYears/src/LifeOverYears/output/smoke-video/images/2015.png" -loop 1 -t 3 -i "/Users/vlad/Projects/LifeOverYears/src/LifeOverYears/output/smoke-video/images/2025.png" -filter_complex "[0:v]scale=1080:1920:force_original_aspect_ratio=decrease,pad=1080:1920:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=30,format=yuv420p[v0];[1:v]scale=1080:1920:force_original_aspect_ratio=decrease,pad=1080:1920:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=30,format=yuv420p[v1];[2:v]scale=1080:1920:force_original_aspect_ratio=decrease,pad=1080:1920:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=30,format=yuv420p[v2];[3:v]scale=1080:1920:force_original_aspect_ratio=decrease,pad=1080:1920:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=30,format=yuv420p[v3];[4:v]scale=1080:1920:force_original_aspect_ratio=decrease,pad=1080:1920:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=30,format=yuv420p[v4];[5:v]scale=1080:1920:force_original_aspect_ratio=decrease,pad=1080:1920:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=30,format=yuv420p[v5];[v0][v1]xfade=transition=radial:duration=2:offset=1[x1];[x1][v2]xfade=transition=radial:duration=2:offset=2[x2];[x2][v3]xfade=transition=radial:duration=2:offset=3[x3];[x3][v4]xfade=transition=radial:duration=2:offset=4[x4];[x4][v5]xfade=transition=radial:duration=2:offset=5[outv]" -map "[outv]" -c:v libx264 -pix_fmt yuv420p -r 30 "output/smoke-video/video/timeline.mp4" |

