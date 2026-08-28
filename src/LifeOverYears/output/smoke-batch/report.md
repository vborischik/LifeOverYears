# Batch Smoke Test Report

Generated: 2026-08-28T16:44:54.5817890+00:00

## Check Results

| Check | Description | Status | Detail |
|-------|-------------|--------|--------|
| B1 | SubmitEraAsync writes a one-line per-era input file and persists the real batch id | ✅ PASS | input file, custom_id, prompt, base file id and job state all correct |
| B2 | Re-submitting a year with a live batch reuses it instead of creating a second | ✅ PASS | one batch (batch_3) across two SubmitEraAsync calls |
| B3 | Chained mode: each era gets its own batch and collects independently | ✅ PASS | two eras submitted at different times, two batches, both collected |
| B4 | Pending batch statuses report not-ready rather than throwing | ✅ PASS | validating, in_progress and finalizing all return false |
| B5 | Terminal failure statuses throw with the batch's own error-file text | ✅ PASS | failed, expired and cancelled all surface the provider complaint |
| B6 | Completed batch decodes the line matching this year's custom_id, not the first line | ✅ PASS | correct payload chosen out of three out-of-order output lines |
| B7 | An unknown batch status is treated as pending rather than fatal | ✅ PASS | unrecognised status returned false |
| B8 | Collecting a year that was never submitted throws naming that year | ✅ PASS | missing job state reported against the year |
| B9 | An era image already on disk collects with no API call and is not overwritten | ✅ PASS | existing file honoured, zero provider calls |
| B10 | A completed batch whose line failed reports the provider's message, with or without an output file | ✅ PASS | rejection surfaced with the API's own wording in both shapes |
| B11 | OpenAi:BatchInlineImage swaps the image file_id for an inline base64 data URL and uploads no image file; the default keeps the documented file_id form | ✅ PASS | both image-delivery shapes hold, and inline uploads nothing but the batch input |

