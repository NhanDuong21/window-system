# Nghiệm thu hiện tại — Nyan Control Center 1.0.2

**PARTIAL; lượt Explorer 1.0.2 đã qua native read và persistence riêng, còn UI và hiệu năng.** Bản 1.0.2 sửa lỗi JSON của lần nghiệm thu 1.0.1 bạn vừa báo. Launcher vẫn là `Nghiem-Thu-Nyan.cmd`; không sửa/xóa evidence cũ, không reset state hoặc đổi policy. Các workflow production Windows/UAC vẫn NOT_RUN.

## Evidence người dùng bổ sung — Explorer 1.0.2

Người dùng cung cấp `.evidence/acceptance-858cceaea5ce4551936a796aaa272bc7`. Ownership/root, source **e2c964d700856968fe86ff23daabaece5eea00c0** và EXE SHA256 khớp artifact 1.0.2. `entry-declaration.json` ghi **USER_DECLARED Explorer**, không suy nguồn mở từ process. Read context lúc 04:44:18 +07 ngày 2026-10-03: main PID 1668, **elevated=false, packaged=false, redirectedAppData=false**. Session context cũng ghi cả ba false. Đây là bằng chứng mới ngoài host của lượt do người dùng chủ động mở, không thay bằng capture của agent.

| Khả năng trong lượt này | Kết quả mới |
|---|---|
| Ownership JSON BOM, artifact identity | PASS; lỗi JsonException của 1.0.1 không tái diễn |
| Native read và 14 page captures | `result.txt` PASS; Services 321/Ports 222 Ready; Dashboard/Applications/Startup/Processes/DevTools/Cleanup vẫn Partial; Storage Empty trước chọn folder |
| Persistence/backup/restore dữ liệu app | PASS ở store GUID riêng, cùng profile; firstLight/reload/restored đều true. Không chứng minh live state hoặc phục hồi sau reinstall |
| Mở giao diện user thường ngoài host | Session mở thật, USER_DECLARED Explorer; elevated/package/redirection đều false |
| Hai lần đóng/mở, theme, Ctrl+K/F5/resize/chữ Việt | WAITING_FOR_USER: tại lúc đọc evidence chỉ có một session-open, chưa có session-first-close/session-closed/acceptance-summary; cửa sổ đầu PID 30168 còn chạy và Responding=true |
| Hiệu năng | First frame **757ms** và working **187.8MiB** đạt budgets; **idle CPU 2.2257% > 1%: FAIL** ở sample settle 5s + đo 10s, DPI 100%. Đây là lần vượt ngưỡng mới; giữ cả evidence host FAIL/recheck PASS trước đó. Nguyên nhân chưa xác định |
| Production Windows mutation/UAC | NOT_RUN: chưa có resource ownership/results. Redirection=false chỉ cho thấy context phù hợp, không phải mutation PASS |

Đã rà luồng đo và polling trong source; chưa xác định được nguyên nhân CPU từ evidence hiện có, không sửa source hoặc thay ngưỡng/điều kiện đo để gọi PASS. Launcher hiện kiểm native read/persistence rồi tiếp tục UI, chưa đưa performance gate vào `acceptance-summary.json`; vì vậy native PASS hoặc human OK không đồng nghĩa performance PASS. Không chạy thêm artifact để tìm phép đo đạt. Tất cả file evidence giữ nguyên, không đóng cửa sổ hoặc thao tác thay người dùng, không tạo resource Windows.

Bước tiếp theo: tiếp tục cửa sổ đầu đang mở, đổi theme và thử checklist UI; đóng cửa sổ đầu, kiểm theme khi launcher mở lại, đóng cửa sổ thứ hai rồi ghi OK hoặc lỗi trong console. Enter ở menu tùy chọn kết thúc chỉ đọc. Không cần chạy lại launcher chỉ để bổ sung hai session còn thiếu.

## Lỗi Explorer đã tái hiện và sửa

Evidence `.evidence/acceptance-9d81694925794325b328483b70e25b0a` giữ nguyên: USER_DECLARED Explorer, artifact 1.0.1/source 9c72e4b; launch ghi parent elevated=true rồi main child=false. `result.txt` FAIL JsonException, chưa có read-context/persistence/performance. Không tính lần này là native smoke PASS; package/AppData context ngoài host chưa được ghi lại.

`ownership.json` do Windows PowerShell 5.1 tạo bắt đầu bằng **EF-BB-BF** (UTF-8 BOM). Parser 1.0.1 đọc trực tiếp byte bằng JsonSerializer nên từ chối marker này. Smoke trước chạy dưới PS7 tạo JSON không BOM, vì vậy bỏ sót tương thích của launcher PS5.1.

Sửa: đọc JSON có giới hạn qua native handle, chấp nhận đúng một UTF-8 BOM ở đầu; dùng cho ownership và service manifests. Raw byte reader/DPAPI, size/path/reparse guards không đổi. JSON sai định dạng, quá giới hạn, BOM kép/UTF16 vẫn bị từ chối. Failures ghi rõ stage và launcher in chi tiết read result. Bộ test cũng sửa imported FAIL bị bỏ qua trong exit/summary; probe chủ động tạo một FAIL và kiểm exit1, không tính sentinel này thành lỗi của suite thật.

## Kiểm chứng mới — đúng artifact 1.0.2

- `.evidence/verify-38a8572508f84fae96fac521b552988f`: **62 PASS / 0 FAIL / 0 SKIP**, gồm 9 JSON regressions (writer PowerShell 5.1 thật, tái hiện parser cũ, BOM/non-BOM/Vietnamese, service array/numeric, malformed/size/encoding), 13 service mô phỏng, 31 store/files riêng, 9 read-only. WPF 46 PASS; Release build 0 warnings/errors. `expected-failure-probe.txt` là failure sentinel cố ý, exit 1 đã được kiểm.
- `.evidence/ps51-json-fix-88cb5eeb645d4ba7a657b583e2cccdec`: syntax của các script đổi được parse bằng Windows PowerShell 5.1; không mở Explorer hoặc mutate Windows.
- `.evidence/release-0c38f0866cb047089990133db0384c1e`: **artifact 1.0.2** với ownership JSON BOM; WPF 46, 14 native/light-dark, context và private persistence/backup/restore PASS. Main elevated=false/package=false/redirection=true trong host. Tuy nhiên script smoke **exit 1 vì idle CPU 2.1215% > budget 1%**; first frame 1023ms, working 176.1MiB, DPI 100%. Giữ nguyên FAIL hiệu năng, không gọi toàn bộ lần smoke này PASS.
- `.evidence/performance-recheck-9a151b08866a423793e6ed66aed282fe`: kiểm lại **một lần cùng artifact/ngưỡng** vì phép đo trên thất bại; native read PASS, performance PASS (815ms, idle CPU 0.0130%, working 153.3MiB, private 103.7MiB, DPI 100%). Có một phép đo FAIL và một PASS; nguyên nhân biến động CPU chưa xác định, không tuyên bố mọi điều kiện hiệu năng ổn định. Không chạy vòng lặp để tìm PASS.

BOM fix đã được chứng minh trong host bằng đúng wire format PS5.1. Các scope native read/fixture/private store của capability matrix baseline phía dưới giữ nguyên; cột production Windows vẫn NOT_RUN. Việc bạn đã khai báo mở Explorer 1.0.1 không thay cho nghiệm thu 1.0.2. Mở launcher lại, kiểm dòng **Release 1.0.2**, rồi chờ read smoke và hai UI sessions. Kết thúc chỉ đọc bằng Enter; gửi tên thư mục evidence mới.

## Artifact và Git 1.0.2

Source **e2c964d700856968fe86ff23daabaece5eea00c0**, folder `artifacts/NyanControlCenter-1.0.2-win-x64`, ZIP cùng tên; self-contained/unsigned. Đã kiểm SHA256 của toàn bộ 403 file trong manifest và ZIP. Launcher đọc release.json đã trỏ 1.0.2. Hash EXE/ZIP 1.0.0, 1.0.1 và ownership/result của evidence lỗi giữ nguyên; commit bàn giao sau chỉ docs, không phải source artifact.

EXE SHA256: `0D522E5A8F3B00A94502D9C5EE98B3603C5BA83505C72A750AAC31552EA5895D`.

ZIP SHA256: `BF33F5079CFB00776F0D737A430035F94739273A979B7EA3173CBCE05D4DF93B`.

Không Windows configuration/resource mutation mới trong lượt sửa lỗi này. Chỉ test writer/build/read-only processes và app/file/evidence riêng; không dùng lại GUID lỗi để overwrite kết quả. Các capture processes đã kết thúc. PR #1 branch nyan/control-center giữ draft, không merge. User retry ngoài host/UAC/production vẫn chờ; không tìm đường mở ngoài host thay bạn.

## Baseline nghiệm thu — 1.0.1

**PARTIAL**. Phần tự động an toàn hoàn tất; Explorer, persistence ngoài host và production mutation/UAC còn **WAITING_FOR_USER / NOT_RUN**. Không khởi động lại 18 phase. Các phần 1.0.0 phía dưới giữ làm baseline lịch sử, không phải số đo/source của 1.0.1.

## Bạn mở gì

Trong **File Explorer**, nhấp đúp **Nghiem-Thu-Nyan.cmd** ở gốc repository; gõ EXPLORER nếu chính bạn mở từ đó. Mặc định launcher kiểm manifest/checksum toàn bộ file, mở release thật, đọc native, kiểm persistence/backup/restore ở store GUID riêng, rồi mở UI hai lần. Đổi theme, Ctrl+K, F5 giữ selection, resize và xem chữ Việt/đường dẫn dài; đóng hai cửa sổ, ghi OK hoặc lỗi. **Enter ở menu cuối để kết thúc chỉ đọc.** `Mo-Nyan.cmd` mở app bình thường với dữ liệu hiện có.

Menu 1–6 tùy chọn: User Environment, User Run Startup, owned process/localhost TCP, owned Temp recycle, System Environment, registered service. Mỗi bài hiện đối tượng `NYAN_ACCEPTANCE_<GUID>`, tác động và chuỗi dọn, mặc định No; chưa xác nhận thì không tạo resource Windows. Không PATH/JAVA_HOME, startup đang dùng, service đang dùng hay temp có sẵn. Script service Register/Remove cần bạn tự mở PowerShell elevated và gõ đúng tên service để xác nhận riêng; không auto-approve UAC. Xem OPERATIONS.md. Không chạy các bước đó trong host để thay thế Explorer.

Launcher không cần SDK, không đổi execution policy, task/service để vượt host, không tự migrate/reset dữ liệu giữa AppData roots. Ngữ cảnh package/redirection chỉ là điều kiện guard; `False` không phải production mutation PASS. EXPLORER được ghi **USER_DECLARED**, kết quả người quan sát lưu riêng; process context không tự chứng minh nguồn mở.

## Capability matrix

`F0` là fixture/evidence bản1.0.0 giữ nguyên; `N1` là read-only artifact1.0.1 lần này; `S1` là 13 service orchestration regressions mô phỏng; `A1` là store riêng/DPAPI cùng profile. Cột production chỉ tính backend fixture=null trên resource Windows sở hữu trong đúng context. Không đổi phase VERIFIED để lấp cột này.

| Khả năng | Fixture / app data | Native read-only 1.0.1 | Production Windows mutation | Phần cần bạn / không hỗ trợ |
|---|---|---|---|---|
| Portable release, main non-elevated | UI46 | N1 PASS, elevation=false | Không áp dụng | Explorer WAITING_FOR_USER; ảnh/capture không phải thao tác desktop đầy đủ |
| Ngữ cảnh package/AppData | Guard F0 | package=false, redirectedAppData=true | NOT_RUN, guard giữ nguyên | Mở launcher từ Explorer; không tự gộp roots |
| Theme/persistence | A1 PASS; UI controls lưu light/dark | Store GUID reload/backup/restore PASS trong host | Không áp dụng | Live user root và hai lần UI ngoài host WAITING_FOR_USER |
| Dashboard / Applications / DevTools | UI/filter F0 +46 | N1 Partial đúng nguồn bị thiếu quyền | Không áp dụng | Không uninstall hoặc khởi daemon; hardware CIM vẫn Partial |
| Startup inventory | F0 | N1 Partial, có dữ liệu thật | Không áp dụng | Không suy registration thành StartupApproved |
| Startup Run User disable/enable | F0 exact bytes/type | N1 đọc | NOT_RUN | Menu2; target owned thoát ngay; cleanup value đúng identity |
| Startup Folder User disable/enable | F0 file/undo | N1 đọc | NOT_RUN | Bài Run không chứng minh Folder production; vẫn chờ user |
| Startup RunOnce/tasks/services/System/approval | Không mutation fixture | N1 đọc nguồn khả dụng | UNSUPPORTED | Các nguồn này chỉ đọc |
| Process CPU/RAM/PID time | F0 UI | N1 Partial đúng permissions/vanished | Không áp dụng | Không command line inventory/log |
| End owned process / port owner | F0 native fixture, guard override | N1 Ports Ready, PID/time có đối chiếu | NOT_RUN | Menu3, image GUID + localhost TCP + manifest, recheck PID/time/endpoint |
| TCP/UDP v4/v6 inventory | F0 4 socket/native checks cũ | N1 Ports Ready | Không áp dụng | Menu3 TCP không chứng minh production terminate cho mọi transport |
| Services inventory | F0 protected/dependency policy | N1 Ready321 rows | Không áp dụng | Không chọn service đang dùng để test |
| Service start/stop/restart | S1 PASS: stop timeout, failed start sau stop, cancel sau send, stale, unknown reread | N1 chỉ đọc | NOT_RUN | Register riêng demand-start/LocalService/no network/dependency; menu6 và Remove riêng |
| UAC cancel / permission errors / helper outcome | S1 unconfirmed helper reread/unknown, simulated access denied | Không chạy UAC | NOT_RUN | Bạn tự huỷ UAC đầu tiên; native denial/alternate-admin chưa chứng minh |
| User environment create/update/delete/undo | F0 raw value/type/PATH/stale/undo | N1 scopes/masked Ready | NOT_RUN | Menu1 biến mới GUID; không thử biến sẵn có |
| System environment | F0 validation, không actual SCM/helper | N1 chỉ đọc | NOT_RUN | Menu5, UAC riêng; isolated System undo không chạy/import vào live store |
| Process environment / PATH ordering | F0 PATH raw order/type | N1 Process đọc, giá trị che | Process write UNSUPPORTED; real PATH NOT_RUN | Bài nghiệm thu không sửa PATH |
| Cleanup age/preview/cancel/stale/recycle | F0 native fixture; 1.0.1 receipt mới chưa production | N1 Partial/metadata | NOT_RUN | Menu4 file owned đúng Temp policy; giữ exact Recycle Bin receipt, không purge |
| Storage scan/drilldown/cancel/Unicode/links | StoreChecks31 + UI46 | N1 chưa chọn folder là Empty | Không cấu hình Windows | Local metadata only; cloud/reparse/network content không hỗ trợ |
| Network adapters / IP/DNS | F0 privacy/UI | N1 Ready, mask mặc định | UNSUPPORTED | Không đổi DNS/firewall/routing |
| Snapshot create/diff/export/import | A1/F0 DPAPI same-profile PASS | Coverage không đủ vẫn Partial | Không áp dụng | Không Windows Restore Point; cross-machine/reinstall recovery không được hỗ trợ bảo đảm |
| Backup/restore dữ liệu app | A1 PASS, chỉ store riêng | N1 persistence PASS | Không áp dụng | Không overwrite live state để test; cần cùng profile/DPAPI keys còn nguyên |
| History / preview / cancel / Ctrl+K / refresh selection | UI46 + S1 encrypted partial history | N1 native window captures | Mutation outcome thực tế NOT_RUN | Checklist Explorer/resize/DPI/preview cancel bằng người quan sát còn chờ |

## Evidence mới và lỗi đã sửa

- `.evidence/verify-6d0708cdc436431e9217b33fb70f7de4`: **53 PASS /0 FAIL /0 SKIP** (13 service mô phỏng,31 store/files riêng,9 module read-only), WPF **46 PASS**. Build Release0 warnings/errors. Không chạy suite Windows mutation fixture cũ trong lượt này; số135 trước đây vẫn chỉ thuộc baseline1.0.0. Sau thay đổi cuối ở acceptance preflight đã build lại solution0 warnings/errors; actual artifact gate phía dưới dùng chính source cuối.
- `.evidence/script-checks-d6f0d8ce042342a2aac490eb33457937`: PowerShell5.1 parser và compile SCM interop. Register/Remove **NOT_RUN**; không dùng syntax check làm production PASS.
- `.evidence/release-99cdc2dfec6a48c688fbc30ddda5eb66`: **chính artifact1.0.1**, UI46 PASS,14 native pages + dark PASS, isolated persistence/backup/restore PASS. Services321/Ports284 Ready. Dashboard/Applications/Startup/Processes/DevTools/Cleanup Partial, Storage Empty trước chọn folder, snapshot/history0 hợp lệ. Main elevation=false/package=false/**redirection=true**. PNG Services/light và Dashboard/dark đã xem; RenderTargetBitmap/controls không chứng minh Explorer, UAC hoặc mọi tương tác OS.
- Số đo mới1.0.1: first ContentRendered **1157ms**, idle CPU **0.013%**, working set **154.7MiB**, private **108.7MiB**, DPI **125%**. Đạt budgets2500ms/1%/350MiB; Settings settle5s+sample10s trước allocation PNG. Không tái dùng scan4000files hoặc no-SDK lookup của1.0.0 như benchmark mới.

Bug Services tái hiện bằng adapter: Stop được nhận nhưng timeout, restart đã stop rồi Start lỗi, cancel sau send đều mất bước đã hoàn tất trong outcome cũ. Đã sửa `PerformAsync/WaitAsync` trả **partial**, ghi lệnh đã nhận/bước đã xác minh/bước lỗi, đọc lại native state hoặc ghi unknown; không tự khôi phục. Preview stale không gửi command. Helper đã mở nhưng timeout/mất/không decode được result cũng partial, reread khi có thể; UAC cancel trước helper vẫn cancelled. History giữ nguyên thông tin partial; lỗi ghi history không biến thông tin unknown thành “đã xác minh”. Regression ở commit **06174ba**; chưa điều khiển service thật để chứng minh các nhánh SCM/UAC.

Không tái hiện mất selection sau refresh; bổ sung stable-row regression PASS, không sửa/redesign UI. Cleanup outcome thêm receipt exact native bin item để lần thử tương lai có ownership/leftovers; **chưa chạy production recycle1.0.1**.

## Artifact, Git và tài nguyên

Release `artifacts/NyanControlCenter-1.0.1-win-x64/`, ZIP cùng tên, self-contained/unsigned; manifest gồm version/RID/source và SHA256 **tất cả file**. Source **9c72e4ba619e105a867c6153684f91fec5960635**. Commit bàn giao sau chỉ docs; không đồng nhất HEAD docs với artifact source.

EXE SHA256: `DD2B14BB5924112C068BC5E44CDC46C06A0DCA79B8BE1BA92E24851CB80081BB`.

ZIP SHA256: `B24F1D873FE7F955190ED50AF4339D25152526D9043DE633E41B7797AEAB8595`.

Bản1.0.0/source15ca7b4/evidence cũ giữ nguyên, hashes đã đối chiếu không đổi trước lượt này. Branch **nyan/control-center**, [PR#1](https://github.com/NhanDuong21/window-system/pull/1) giữ **draft**, không merge. Mốc service06174ba, mốc launcher/package/source9c72e4b, mốc docs nghiệm thu cuối xem `git log`.

Lượt này **không tạo/đăng ký hoặc sửa Windows resource thử thật**: không service, User/System environment, startup, listener hay temp recycle. Chỉ build/runtime/read-only helper processes và file/app-store/ACL/link fixtures dưới `.evidence` riêng; UI/native capture đã đóng. Artifact/evidence/app files giữ local ignored; không public inventory hay binaries. Không reset/restore state đang dùng, không thay policy/UAC hoặc gộp AppData.

Fixture cũ trong Recycle Bin thuộc baseline trước lượt này vẫn giữ nguyên. Một số bài legacy không lưu exact bin receipt/File ID nên **không có đủ căn cứ để tự động purge/cleanup chúng**. Chỉ restore thủ công item có tên GUID/path khớp evidence sở hữu; không dọn theo tên chung hoặc toàn bin. Bài cleanup mới sẽ lưu receipt riêng và liệt kê item retained; lỗi khác giữ original/quarantine với identity và hướng dẫn trong `*-result.json`.

DPAPI backup/snapshot chỉ đã chứng minh cùng máy/profile hiện tại còn keys. Không bảo đảm khôi phục sau cài lại Windows, profile mới hoặc máy khác; cùng tên account không đủ. Đây là giới hạn sản phẩm, không xây recovery đa máy ở lượt này. [Microsoft DPAPI](https://learn.microsoft.com/en-us/windows/win32/api/dpapi/nf-dpapi-cryptprotectdata).

Phần còn lại cần người dùng đã có launcher/quy trình concrete. Dừng tự động ở đây với **PARTIAL**, không thử cách mở khác trong host để đổi thành PASS.

## Baseline lưu nguyên — 1.0.0

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
