# Nghiệm thu Nyan Control Center 1.0.0

Có bản Windows desktop chạy được, dữ liệu thật, đủ luồng cho 18 phase. **PARTIAL về nghiệm thu thao tác Windows thực tế**: 17 phase VERIFIED trong phạm vi ghi dưới đây; Phase07 IMPLEMENTED vì SCM mutation/UAC thật chưa chạy. Không công bố 18/18 đầy đủ hoặc mọi nhánh quyền cao đã PASS.

## Mở ngay

Nhấp đúp **Mo-Nyan.cmd từ File Explorer**, hoặc mở `artifacts/NyanControlCenter-1.0.0-win-x64/NyanControlCenter.exe`. Giữ nguyên cả thư mục release; không chép riêng EXE. ZIP cùng tên ở `artifacts/`. Không cần cài .NET/SDK cho bản này; runtime .NET/WindowsDesktop10.0.12 nằm trong folder. Không installer hoặc auto-update server; cập nhật portable folder thủ công.

App chính asInvoker, có launcher xác minh user thường khi nguồn mở đang elevated. Lần đầu light, dark được lưu; UI tiếng Việt. Khi mở từ host Codex, filesystem vẫn chuyển hướng AppData dù child elevation=false/package=false: production mutation bị từ chối ở backend. **Mở ngoài host từ Explorer là bước nghiệm thu còn lại**, chưa được xác nhận trong phiên này; không thay đổi host/UAC để vượt giới hạn.

WPF/C# .NET10, Windows native x64; UI → typed Core → Windows adapters + DPAPI AppStore. Không server/HTTP/LAN, WebView2, AI runtime hay dependency NuGet bên thứ ba. Chọn WPF vì native DPI/keyboard/bảng và giảm toolchain/IPC so với JS/Rust trên máy đích. SDK10.0.401 official ZIP pin local, không cài global.

## Phạm vi 18 phase

Evidence đường dẫn tương đối dưới workspace, đều ignored. `V` dưới đây luôn có giới hạn đã công bố; fixture mutation khác kiểm thử cấu hình thật.

| Phase | Trạng thái | Chức năng thật và kiểm chứng | Commit mốc | Giới hạn |
|---|---|---|---|---|
| 01 | VERIFIED | Native discovery/build, typed contract, DPAPI/errors; launcher10 checks + native user window | 3588d39, 0de59bb | Host AppData redirection; Explorer context chưa test |
| 02 | VERIFIED | CPU400ms, RAM/drive/uptime/build; Windows read smoke | 12268ce | Hardware CIM bị từ chối trong user token, hiện Partial; không nhiệt độ |
| 03 | VERIFIED | Registry user/machine32/64 + Appx adapter, filter/sort | 76a4864 | Appx có nguồn denied; không inventory tuyệt đối, không uninstall |
| 04 | VERIFIED | Run User/folder User backup, disable/enable, exact fixture bytes/type | ce6813f | RunOnce/tasks/services/System chỉ đọc; không sửa StartupApproved |
| 05 | VERIFIED | CPU/RAM, PID+UTC identity, preview/end owned child, vanished/PIDreuse | 1330542 | Protected/Windows/khác owner/session từ chối; không kill tree |
| 06 | VERIFIED | Async metadata scan, drilldown/progress/cancel, logical/allocated, StoreChecks31 | 556c6bc | Local only; skip cloud/reparse/denied, hardlink dedup; rows2000 |
| 07 | IMPLEMENTED | SCM enum/config user321rows; start/stop/restart adapter/policy/helper nối UI | 738b47f, c29823d | **Không test service thử thật hoặc SCM mutation/UAC**; chỉ own-process third-party |
| 08 | VERIFIED | Resolve/version allowlist, signed trusted probes, local Docker status | aeba77d | Untrusted tool metadata-only; daemon không khởi chạy; nguồn thiếu hiện Partial |
| 09 | VERIFIED | IP Helper TCP/UDP IPv4/IPv6, owner stable, process link/recheck; owned4socket fixtures | e3d72d3, c29823d | Owner của protected/vanished PID có thể không đọc được; không đoán theo port |
| 10 | VERIFIED | User/System/Process scopes, mask, raw PATH order/type, stale preview/DPAPI undo | 4a89b39 | Mutation trên HKCU fixture riêng; System UAC và PATH thật chưa test |
| 11 | VERIFIED | NetworkInterface adapter/IP/DNS local, refresh/mask | 1981ff3 | Không đổi DNS/firewall/routing; không public IP request |
| 12 | VERIFIED | Create/select2/diff/import/export configuration snapshot | 185c897 | DPAPI cùng account; coverage Partial không kết luận Removed/Added; không Restore Point |
| 13 | VERIFIED | Temp>7days scan/select/preview/native identity/quarantine/recycle; rename6 + recycle5 checks | a04b63d | Không default selection/Temp User cleanup thật; recycle không hứa undo tự động |
| 14 | VERIFIED | Outcome history success/failed/cancelled/partial, mask/bounds/retention, undo khi khả thi | 7e544c8 | Chỉ hoạt động app; registry ngoài app còn race best-effort |
| 15 | VERIFIED | Ctrl+K navigation/cache search, keyboard/empty, danger qua preview, reveal-off clear | 73eb73b | Không index nội dung ổ đĩa; dữ liệu cache có thể stale |
| 16 | VERIFIED | Independent review/reproduction, IPC/expiry/enum/undo/history/redirection regressions | 499f370 | Không chống malware đã chiếm account/admin; UAC thật còn manual |
| 17 | VERIFIED | Release metrics/budgets, 4000files/12000rows, polling/cache/virtualization/disposal | 8d30212 | First-frame child không gồm elevated-launcher overhead; idle khác polling load |
| 18 | VERIFIED | Portable self-contained win-x64, source/hash/ZIP, actual artifact UI/native smoke, runtime lookup isolation | 15ca7b4 + docs phase18 | Unsigned; manual update backup/migration fixture; native writes/Explorer/UAC chưa nghiệm thu |

Source nền nhiều module tích hợp chung ở Phase01 theo ownership tách biệt; sau đó từng phase có commit đóng kiểm chứng. Không có 18 bản UI placeholder. `git log --oneline --grep=phase` liệt kê mốc; commit bàn giao chỉ cập nhật docs, khác commit artifact.

## Kiểm tra đã chạy

```powershell
./scripts/verify.ps1
./scripts/package.ps1
./scripts/smoke-release.ps1
```

Locked restore + Release build **0 warnings/0 errors**. Console suite mới nhất **135 PASS, 0 FAIL, 0 SKIP**: unit/contract, Windows read-only, DPAPI/migration/import/corruption, Unicode/long/denied/cloud/hardlinks/reparse, vanished/PIDreuse/stale/timeout/cancel, fixture mutations và app disposal. `.evidence/verify/summary.json`, `tests.txt`, `performance.json`. WPF thật **45 PASS, 0 FAIL, 0 SKIP**, `.evidence/verify/ui/result.txt`; dòng METRIC không tính test. Dùng event/control thật và RenderTargetBitmap, không OS UIAutomation.

Artifact cuối: `.evidence/release-4be36d156493468ba7ac8f81a92acec8`: cùng45 UI checks, native14pages + dark, `elevated=False`, Services/Ports Ready có dữ liệu thật. Một số nguồn Dashboard/Apps/Startup/Processes/DevTools/Cleanup Partial được hiển thị trung thực. Storage chưa chọn folder là Empty; snapshot/history mới là0rows hợp lệ. Lead đã xem PNG light/dark và Ports/Services, worker xem các màn/resize/states/preview; ảnh máy thật không upload.

`smoke-summary.json` ghi PASS native read-only/user/UI/performance; **UNVERIFIED_HOST_REDIRECTION** cho context mutation, **NOT_RUN** cho real mutations. Lần probe trước failure ở context này được giữ local; script cuối tách phạm vi read-only đã PASS khỏi phần chưa nghiệm thu, backend guard không nới.

Portable dependency test riêng: `.evidence/portable-no-sdk-c72151581c6948a48eb4d05ced6f3f87/runtime-proof.txt`: chính artifact chạy native captures khi DOTNET_ROOT/ROOT_X64/ROOT_X86 trỏ tới thư mục fixture không tồn tại và ROLL_FORWARD=Disable. Runtimeconfig có includedFrameworks, không cần SDK/runtime global.

Native token tests ngoài suite: `.evidence/native-user-read/results.txt`8PASS/0FAIL (đủ TCP/UDP v4/v6 và SCM dưới restricted Medium token); `.evidence/launcher-checks/result.txt`10PASS; `.evidence/rename-owned/results.txt`6PASS; `.evidence/recycle-owned/results.txt`5PASS gồm recycle callback và restore đúng owned bin item. Những số này là bộ riêng, không cộng thành một tổng nhằm che overlap.

Review độc lập: worker storage đọc diff/viết runner riêng để tái hiện findings và chạy123PASS/0FAIL/0SKIP trước IPC tightening; tiếp tục source audit các sửa. Lead tích hợp, sửa tuple redirection edge và chạy suite135 cuối. Chi tiết ở REVIEW.md; không còn P0/P1 đã xác định chưa xử lý.

## Hiệu năng thực

| Phép đo | Kết quả | Ngân sách/điều kiện |
|---|---|---|
| Artifact first ContentRendered |1124ms|2500ms; normal child, không gồm launcher elevated |
| Idle CPU |0.013%|1%; Settings settle5s + sample10s, chuẩn hóa theo CPU logic |
| Working set / private memory |151.0 / 107.0MiB|Working set350MiB; trước capture-image allocations |
| Scaling |125%|Native DPI1.25; light/dark/resize đã xem |
| Metadata scan |526ms /4000files|15s; local owned fixture, rows≤2000 |
| Filter |198.38ms /12000rows|WPF Unicode fixture, virtualized rows/columns |
| Pre-cancel scan |0ms|≤2000ms; không đại diện hủy giữa tác vụ; StoreChecks kiểm thêm active cancel |

Không gọi nặng để vẽ chart; polling5s chỉ active Dashboard/Processes, hardware cache20min, subprocess max2, output/timeouts bounded. Native collector disposal test xác nhận đúng helper đã thoát, không kill process ngoài sở hữu.

## Artifact và Git

Artifact version1.0.0 source **15ca7b457150b39ea80029e2641f034a09d6cfbf**, branch **nyan/control-center**, đúng remote `NhanDuong21/window-system` PUBLIC. Branch đã push; [draft PR #1](https://github.com/NhanDuong21/window-system/pull/1) đã mở và gắn vào chat. Commit bàn giao chính697922f; commit sau chỉ chốt trạng thái Git trong docs. Binaries/evidence/runtime không đưa lên Git; không push main, merge hoặc public release.

EXE SHA-256: `EDCB63E8C4BACEDD1525D417E36779D836F2EFC2F63940D23F1169616EF250FE`.

ZIP SHA-256: `5973B77DFCD5EFA3B9CF9F3A49F4E7AE6C0A6C159792ABAE2D0711B0531672F3`.

Manifest trong folder artifact ghi version/source/RID/hash/unsigned. App chưa ký; không tắt Defender/SmartScreen/UAC. Sau commit artifact chỉ docs thay đổi, không cần giả rằng artifact build từ commit docs sau đó.

## Dữ liệu, tài nguyên và phần còn lại

Mở bình thường từ Explorer: `%LOCALAPPDATA%\NyanControlCenter`; Windows host redirection có thể chuyển dữ liệu app thử sang package cache như OPERATIONS.md. State schema1 DPAPI CurrentUser/exclusive lock/atomic write/max20MiB, history≤1000/30days, snapshots/undo≤100. Giá trị env/paths/IP/DNS che mặc định; reveal in-memory chủ động. Snapshot/backup mã hóa chỉ cùng account; không restore hệ điều hành. Settings → backup `.nccbackup`/restore có xác nhận. Update: đóng app, backup, giải nén folder mới, kiểm hash, mở EXE mới; giữ folder cũ để rollback. Xem OPERATIONS.md.

Không thay đổi startup/PATH/DNS/firewall/service/Temp thật, UAC/policy/driver/security settings. SDK first-run đã tạo ASP.NET dev certificate ngoài dự kiến; đã xóa đúng certificate và key vừa sinh theo timestamp, không đụng certificate khác. Chỉ fixture HKCU random subtree, file/ACL/process/listener/app-owned GUID helper files được tạo/thay đổi: registry subtree/GUID files và processes/listeners đã dọn. `.tools`, `.evidence`, `.runtime`, artifacts và fixture files giữ local ignored; cleanup integration để một số **file thử sở hữu** trong Recycle Bin, không xóa file người dùng. Separate recycle test đã restore đúng item trả về, không enumerate/purge bin người dùng.

Còn cần nghiệm thu: mở ngoài Codex từ Explorer; SCM service thử/UAC thật và alternate-admin cancellation trong môi trường được phép; System variables action thật; selected User startup/PATH/Temp action nếu người dùng tự cho phép. Những bước này không PASS/skipped tự động. Explorer token/linked-token launcher branches chưa exercised vì máy hiện tại dùng own LUA restricted-token fallback. Không có lỗi P0/P1 còn biết; PARTIAL đến khi scope native mutation/UAC được xác nhận. Không yêu cầu token/installer/global policy để tiếp tục.

## Checklist ngắn

- Mở từ Explorer, xem CPU/RAM/build thật; làm mới bằng F5, kiểm Ports/Services/DevTools và nguồn Partial.
- Đổi dark/light, đóng/mở lại để kiểm lựa chọn lưu.
- Dung lượng → chọn cây fixture dưới `.evidence/verify` có marker, scan/drilldown/cancel; không chọn network/cloud-only.
- Ctrl+K tìm trang hoặc dữ liệu đã có; xem chi tiết/cuộn ngang bảng dài.
- Tạo hai ảnh chụp cấu hình, chọn so sánh; export/import cùng account. Snapshot không phải Restore Point.
- Chọn đối tượng phù hợp, mở preview và **Hủy** để kiểm đối tượng/phạm vi; chưa cần execute thao tác thật để đọc UI.
- Settings backup/restore dữ liệu app; mở lại, giữ dữ liệu/theme. Không đổi Windows bằng restore.
- Chỉ khi muốn nghiệm thu native mutation, chủ động xác nhận action hẹp trên tài nguyên thử phù hợp; service/System cần UAC riêng. Host redirection phải bị từ chối rõ.
