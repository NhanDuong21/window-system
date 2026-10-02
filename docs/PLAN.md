# Kế hoạch nghiệm thu

Mỗi phase: NOT_STARTED → IN_PROGRESS → IMPLEMENTED → VERIFIED hoặc BLOCKED. IMPLEMENTED không có nghĩa mọi nhánh quyền cao đã test. Kết quả cuối ở HANDOVER; bằng chứng thật ở .evidence ignored.

| Phase | Phạm vi/tiêu chí | Phụ thuộc | Trạng thái |
|---|---|---|---|
| 01 | Windows native, skeleton, storage, dữ liệu thật, build | — | VERIFIED |
| 02 | CPU interval, RAM, ổ, uptime, build, lỗi | 01 | VERIFIED |
| 03 | Registry 32/64 user/machine, Appx, filter | 01 | VERIFIED |
| 04 | Registry Run user và folder user enable/disable; nguồn khác read-only | 01,14 | VERIFIED |
| 05 | CPU/RAM/PID, identity creation-time, terminate fixture | 01 | VERIFIED |
| 06 | Scan async, progress/cancel, reparse/cloud/hardlinks, partial | 01 | VERIFIED |
| 07 | Services inventory; policy third-party, dependencies, narrow UAC | 01,14 | IMPLEMENTED |
| 08 | Allowlisted tool versions, resolve path, daemon state | 01 | VERIFIED |
| 09 | TCP/UDP v4/v6 owner, link process, terminate fixture | 05 | VERIFIED |
| 10 | Scope, secrets, PATH order, conflict, DPAPI undo, narrow UAC | 01,14 | VERIFIED |
| 11 | Adapter/IP/DNS local only | 01 | VERIFIED |
| 12 | Snapshot create/diff/validated import/export DPAPI | 03,04,08 | VERIFIED |
| 13 | Temp scan/select/preview; identity recheck; Recycle Bin | 06,14 | VERIFIED |
| 14 | Outcome history, bounds/retention/redaction | 01 | VERIFIED |
| 15 | Ctrl+K cached search/navigation, danger preview only | 02–14 | VERIFIED |
| 16 | Independent diff security review/regressions | 01–15 | VERIFIED |
| 17 | Release metrics, large fixture/filter/cancel, disposal | 01–16 | VERIFIED |
| 18 | Self-contained artifact, hash/source, manual update/backup | 01–17 | NOT_STARTED |
