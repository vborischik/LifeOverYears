# Folder Smoke Test Report

Generated: 2026-08-18T04:47:19.0223341+00:00

## Check Results

| Check | Description | Status | Detail |
|-------|-------------|--------|--------|
| F1 | PipelineFolders.Resolve defaults with no folder keys set | ✅ PASS | matched pre-existing hardcoded paths |
| F2 | PipelineFolders.Resolve honors configured overrides | ✅ PASS | all four keys read back correctly |
| F3 | appsettings.example.json Pipeline section has the four folder keys | ✅ PASS | appsettings.example.json Pipeline section matches defaults |
| F4 | ResolvePhotoPath reads from the configured InputDir | ✅ PASS | found seeded photo via custom InputDir |
| F5 | MoveProcessedPhoto(result==0) moves into the configured ProcessedDir | ✅ PASS | moved and directory auto-created |
| F6 | MoveProcessedPhoto(result!=0 or catch) moves into the configured FailedDir | ✅ PASS | moved and directory auto-created |
| F7 | MoveProcessedPhoto appends a timestamp on name collision instead of overwriting | ✅ PASS | original preserved, timestamped copy created |
| F8 | MoveProcessedPhoto never throws when the move itself fails | ✅ PASS | no exception raised for a missing source |

