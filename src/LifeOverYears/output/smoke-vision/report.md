# Vision Smoke Test Report

Generated: 2026-09-10T02:15:12.9510360+00:00

Offline: the real VisionProvider against a fake INvidiaProvider.
Covers reading the answer, not the accuracy of what the model saw.

| Check | Description | Status | Detail |
|-------|-------------|--------|--------|
| N1 | A snake_case answer parses into a complete SceneDna | ✅ PASS | every field read back correctly |
| N2 | A PascalCase answer parses too — key casing is normalized, not trusted | ✅ PASS | PascalCase read back identically to snake_case |
| N3 | An empty stream is retried and then thrown, never turned into an 'unknown' SceneDna | ✅ PASS | retried 3 times, then threw with the cause named |
| N4 | A <think> block, prose and code fences around the JSON are stripped before parsing | ✅ PASS | wrapped answer read back correctly |
| N5 | A non-JSON answer falls back to the 'unknown' stub, which the spend guard then refuses | ✅ PASS | stub returned and refused by IsRenderableSceneType |
| N6 | Verification applies only the five fields it asked about and leaves the rest of the SceneDna alone | ✅ PASS | five fields applied, id/buildings/immutable untouched |
| N7 | A failed verification keeps the first pass rather than throwing or corrupting it | ✅ PASS | unparseable and empty answers both left the SceneDna untouched |
| N8 | Enrichment shows the model the SceneDna in the same snake_case its schema documents | ✅ PASS | request body carries snake_case keys only |

