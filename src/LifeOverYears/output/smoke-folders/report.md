# Folder Smoke Test Report

Generated: 2026-09-11T19:22:18.0023320+00:00

## Check Results

| Check | Description | Status | Detail |
|-------|-------------|--------|--------|
| F1 | PipelineFolders.Resolve defaults with no folder keys set | ✅ PASS | matched pre-existing hardcoded paths |
| F2 | PipelineFolders.Resolve honors configured overrides | ✅ PASS | all four keys read back correctly |
| F3 | appsettings.example.json Pipeline section has the four folder keys | ✅ PASS | appsettings.example.json Pipeline section matches defaults |
| F4 | Every image in the configured InputDir is returned in name order, whatever the extension's casing; an explicit argument still means one photo | ✅ PASS | 3 photos picked up in order (a_first.JPG, b_second.jpg, c_third.png), .txt ignored |
| F5 | MoveProcessedPhoto(result==0) moves into the configured ProcessedDir | ✅ PASS | moved and directory auto-created |
| F6 | MoveProcessedPhoto(result!=0 or catch) moves into the configured FailedDir | ✅ PASS | moved and directory auto-created |
| F7 | MoveProcessedPhoto appends a timestamp on name collision instead of overwriting | ✅ PASS | original preserved, timestamped copy created |
| F8 | MoveProcessedPhoto never throws when the move itself fails | ✅ PASS | no exception raised for a missing source |

