# 1.0.3-docs1 — ứng viên dùng thử, phần còn lại do user nghiệm thu

**PACKAGE_INTEGRITY: PASS — user xác nhận kiểm độc lập.** Report `BAO-CAO-KIEM-GOI-1.0.3-docs1.md`:404CRC/403manifest payload PASS,398EXE-DLL khớp manifest, guide/docs1 và cấu trúcZIP hợp lệ trong scope đã kiểm. Report đối chiếu GitHubHEAD1670bae/PRdraft; snapshot `.evidence/user-package-review-eba5407193c64a0ca680c5980e1e0413`. Người kiểm chưa cóZIPcũ để so cả398binary và không thực thi app; không gán xác nhận này cho byte-identical/runtime/CPU/live-state/UAC. Gói giữ nguyên, không cần sửa/build/đo lại. Bước còn lại là hai lượt Explorer/private-store và Mo-Nyan/live-state; Windows/UAC vẫnNOT_RUN, spikeCPU vẫn chưa xác định nguyên nhân.

Theo quyết định mới của user: dừng vòng sửa lớn/audit toàn repo và không thêm profiling. Gói mới `NyanControlCenter-1.0.3-win-x64-docs1` có hướng dẫn đúng trong ZIP, không rebuild app. Binary source **3dddf48ceec4c288ebcc309f9831f64c730ec5bd**; documentation commit **df516b07fcabc3ec8d1a6178225d445163904808**. EXE SHA256 không đổi `35796B6AC77E6B2538F6DE68967B5CB5B42954A5FA526CE5FDA186FFD5C4BE09`; ZIP mới **`B3A3EBAD86847B5AB621B0067AB17BD96BCA6A2CAA583B2488691926D628530E`**, ZIP cũ giữ nguyên.

Package proof `.evidence/repackage-docs-191b775d581c4c7ea587450c0b837129`:403payload/404ZIPentry PASS, chỉguide thay đổi,398EXE/DLL và toàn bộ runtime/config khác giữ hash. Artifact docs1 WPF70/self-contained PASS `.evidence/docs1-artifact-ui-be38348f31c448c390d353686012fd50`; PS5.1 syntax/report13/identity và launcher path preflight PASS `.evidence/docs1-preflight-d41cb3a7ddfa4b8f97b6fb711f1074a6`. Không coi dry-run đường mở là Explorer/live-state PASS. Ba lượt performance cũ áp dụng cho chính binary giữ nguyên, không đo lại.

Chỉ source/docs/config/scripts đưa lên nhánh review `nyan/control-center` sau staged diff check; PR#1 draft, không merge/main/force-push. Artifacts/evidence/runtime/database không tracked hoặc push. HANDOVER ghi checksum/metadata và hai bài user còn lại. Các câu chưapush dưới đây là mốc lịch sử.

Quota được mô tả đúng100backup dùng chung +100snapshot riêng; bỏ bản sao khác hoàn tác Windows. Explorer/private-store hai phiên và Mo-Nyan/live-user-state là hai bài riêng, chờ user mở thật. Windows/UAC tiếp tụcNOT_RUN, không thử cấu hình đang dùng. CPU cũ là vấn đề chưa xác định nguyên nhân, không tự chặn dùng thử vô thời hạn; phép đo mới của launcher vẫn phải báoFAIL nếu vượtbudget. Các phần dưới là mốc lịch sử trước quyết định này.

# Đợt sửa 1.0.3 — kiểm artifact xong, nghiệm thu PARTIAL

Năm finding rõ nguyên nhân đã sửa; chi tiết ở `docs/FIX-1.0.3.md`. Giữ18phase/WPF/basic tiếng Việt, 100backup dùng chung các loại, 100snapshot riêng và budgetCPU1%. Verify cuối `.evidence/verify-500d1ec1770949aeb6d375fa63241b5d`:99PASS/0FAIL/0SKIP, PS5.1 report13PASS, WPF70PASS, build0warnings/errors. Windowsguard/helper whitelist giữ nguyên; Startup/quarantine không discard hoặc tự prune.

Artifact1.0.3 từ source **3dddf48ceec4c288ebcc309f9831f64c730ec5bd**; cả403manifesthashes được kiểm. EXE SHA256 `35796B6AC77E6B2538F6DE68967B5CB5B42954A5FA526CE5FDA186FFD5C4BE09`; ZIP `5B919134A4E6547B3B64F81959F24A644FD96A80761A72011C09E13ADBF5FB58`. Source/docs commit sau không thay executable hoặc ZIP đã đo.

Plan chốt trước đo:1baselineprofile1.0.2 +3unprofiled1.0.3 +1profile1.0.3. Series `.evidence/performance-series-ab559c34fb4546fc8abbfaf604840bad`:3/3 native/privatepersistence/user/performancePASS; artifactWPF70/self-contained invalidSDKrootPASS. Lượt1:981ms/CPU0%/146.37MiB; lượt2:695ms/CPU0.013019%/138.28MiB; lượt3:774ms/CPU0%/147.48MiB. Đều Settings foreground/visible, DPI125%, taskfalse, settle5s/sample10s,12CPUlogic; CPU0 là độ phân giải counter, không khẳng định tuyệt đối không dùngCPU. Riêng summary nghiệm thu vẫnPARTIAL vì Explorer/UI/Windows/UAC chưa đủ.

Profile1.0.3 duy nhất `.evidence/cpu-artifact3-stream-c3dd2b4318014038bfead303507d3e3f`:nativePASS/collector0, CPU0.039064% (46.875ms);2managedsamples của threadCPU liên quan WPFautomation/Dispatcher, không đủ để quy nguyên nhân spike cũ. Không rawtrace/ETLX mới hoặc exportProcessInfo payload. Baselineprofile cũng không tái hiện spike. **CPU2.1215%/2.2257% cũ vẫnFAIL, nguyên nhân chưa xác định**; DPI100% cũ khác125% mới. Không retry hoặc đổibudget để đạt.

Explorer1.0.2 đã có firstsessionclose nhưng thiếu reopen/human summary; các mô tả cửa sổ đầu còn mở dưới đây là lịch sử tại thời điểm đọc cũ. Explorer1.0.3, live-user-state và productionWindows/UAC vẫnWAITING_FOR_USER/NOT_RUN. Chỉfixtures/appfiles/read-only, không sửa Windows thật. Hướng dẫn mới ởOPERATIONS và sidecar `artifacts/HUONG-DAN-1.0.3.md`; guide bên trong ZIP giữ nguyên bản đóng gói. PR#1 giữdraft, khôngmerge; hai commit phiên nàylocal, chưapush.

# Baseline 1.0.2 — sửa lỗi PS5.1 BOM

**PARTIAL / WAITING_FOR_USER_UI**. Lượt mới `.evidence/acceptance-858cceaea5ce4551936a796aaa272bc7` do user cung cấp đã ghi USER_DECLARED Explorer 1.0.2; source/hash/root khớp artifact. Native read và isolated persistence/backup/restore PASS; main/session đều elevated=false/package=false/**redirectedAppData=false**. Native Services 321/Ports 222 Ready, các nguồn Partial/Empty được giữ đúng scope. Đây là bằng chứng ngoài host mới; không tự suy Explorer hoặc mutation PASS từ child context.

Tại lúc đọc evidence chỉ có session-open đầu, chưa có first-close/second-close/acceptance-summary. Cửa sổ đầu PID 30168 vẫn Responding=true; người dùng tiếp tục checklist, đóng/mở lại theme và ghi OK/lỗi. Không đóng thay hoặc tự chạy lại launcher. Resource Windows ownership/results chưa có: production/UAC NOT_RUN.

Hiệu năng lượt Explorer: first frame 757ms/working 187.8MiB/DPI 100% đạt budgets, **idle CPU 2.2257% > 1%: FAIL**. Giữ điều kiện settle 5s + sample 10s và toàn bộ evidence; nguyên nhân chưa xác định, không chạy thêm để tìm PASS. Launcher chưa đưa performance gate vào acceptance-summary, nên native read PASS không đóng gap CPU. Lượt này chỉ đọc evidence/rà source và cập nhật docs; không đổi executable, không tạo/dọn resource Windows.

Lượt lỗi cũ: user khai báo mở Explorer 1.0.1, evidence `.evidence/acceptance-9d81694925794325b328483b70e25b0a`: parent elevated=true/main=false, FAIL JsonException trước read-context/persistence. Root manifest có UTF-8 BOM EF-BB-BF do PS5.1; smoke PS7 trước đó không tạo BOM nên bỏ sót. Giữ nguyên evidence lỗi, không gán context/PASS của lượt 1.0.2 cho lần lỗi này.

Source fix/package **e2c964d700856968fe86ff23daabaece5eea00c0**, artifact1.0.2. JSON reader nhận đúng một BOM, giữ native bounds/path guards, không sửa raw DPAPI reader. Stage/error rõ hơn; imported FAIL trong harness giờ làm exit thất bại, sentinel probe exit1. Launcher release.json trỏ1.0.2; giữ1.0.0/1.0.1, không reset dữ liệu hoặc policy.

Safe verify `.evidence/verify-38a8572508f84fae96fac521b552988f`: **62 PASS / 0 FAIL / 0 SKIP**, trong đó 9 JSON regressions dùng Windows PowerShell 5.1 thật; WPF 46 PASS, build 0 warnings/errors. PS5.1 syntax evidence `.evidence/ps51-json-fix-88cb5eeb645d4ba7a657b583e2cccdec`.

Artifact BOM native/private persistence PASS tại `.evidence/release-0c38f0866cb047089990133db0384c1e`; main=false/package=false/redirection=true. Script smoke exit1 vì idleCPU2.1215% >1%, giữ FAIL. Một recheck cùng artifact/ngưỡng `.evidence/performance-recheck-9a151b08866a423793e6ed66aed282fe` native/performance PASS:815ms/0.0130%/153.3MiB/DPI100%. Không xóa phép đo đầu hoặc khẳng định nguyên nhân CPU/độ ổn định đã giải quyết.

EXE SHA256 `0D522E5A8F3B00A94502D9C5EE98B3603C5BA83505C72A750AAC31552EA5895D`; ZIP `BF33F5079CFB00776F0D737A430035F94739273A979B7EA3173CBCE05D4DF93B`. Đã kiểm toàn bộ 403 manifest hashes, ZIP và hash artifact/evidence cũ; capture processes đã kết thúc. Commit sau source chỉ docs.

Không Windows mutation/resource mới; chỉ own app/files/read-only/build processes. Guard giữ nguyên, production Windows/UAC NOT_RUN. Bước tiếp theo: bạn mở lại Nghiem-Thu-Nyan.cmd từ Explorer, kiểm Release1.0.2 và gửi evidence mới. PR#1 giữ draft, không merge; không chạy thay user ngoài host.

## Baseline1.0.1 — 2026-10-03

**PARTIAL / WAITING_FOR_USER**: tự động an toàn xong; Explorer, live user persistence ngoài host và production Windows mutation/UAC chưa chạy. Không bắt đầu lại 18 phase; bảng phase lịch sử phía dưới giữ scope1.0.0. Capability matrix hiện hành ở HANDOVER.md.

Source artifact **9c72e4ba619e105a867c6153684f91fec5960635**, release `NyanControlCenter-1.0.1-win-x64`; bản1.0.0 và evidence cũ giữ nguyên. EXE `DD2B14BB5924112C068BC5E44CDC46C06A0DCA79B8BE1BA92E24851CB80081BB`; ZIP `B24F1D873FE7F955190ED50AF4339D25152526D9043DE633E41B7797AEAB8595`. Manifest full-file hashes. Commit sau artifact chỉ docs.

Mốc sửa service **06174ba**: accepted commands/completed steps/failed step/native reread/unknown; post-send cancel hoặc timeout partial, stale gửi0 commands, helper unconfirmed partial; không tự repair. Regression13 service mô phỏng, history DPAPI. **53 PASS/0 FAIL/0 SKIP** safe verify, WPF46PASS; build0 warnings/errors. Evidence `.evidence/verify-6d0708cdc436431e9217b33fb70f7de4`; source acceptance preflight cuối đã build lại và actual artifact smoke.

Mốc launcher/source **9c72e4b**: `Nghiem-Thu-Nyan.cmd` human opens Explorer, all-file checksums, native read/private persistence, hai UI sessions + human checklist. GUID ownership/confirmation/resource workflows prepared; service Register/Remove riêng demand-start/LocalService; chưa chạy production. No policy bypass/auto-UAC/outside-host launch by agent. DPAPI recovery sau reinstall/cross-machine không được bảo đảm.

Artifact `.evidence/release-99cdc2dfec6a48c688fbc30ddda5eb66`: UI46 +14native/dark + private persistence/backup/restore PASS. Main elevated=false/package=false/**redirectedAppData=true**; guard giữ nguyên, Explorer WAITING_FOR_USER, realMutations NOT_RUN. Services321/Ports284 Ready; một số nguồn Partial trung thực. New metrics1157ms/idleCPU0.013%/working154.7MiB/private108.7MiB/DPI125%, đạt budgets. Không lấy benchmark1.0.0 làm số đo mới.

Không Windows resource mutation thử mới trong lượt này. Chỉ app/file fixtures/evidence ignored và read-only/build processes đã kết thúc; bản cũ/legacy Recycle Bin fixtures giữ nguyên, thiếu receipt nên không purge. PR#1 branch nyan/control-center giữ draft, không merge; docs HANDOVER/OPERATIONS ghi checklist và exact ownership/leftovers cho user cases. Tự động dừng; bước tiếp theo do bạn nhấp đúp launcher từ File Explorer.

## State lịch sử 1.0.0

# Checkpoint

Branch nyan/control-center từ origin/main; repo PUBLIC. Windows native x64 build 22631. Không có code hoặc thay đổi người dùng. SDK global chỉ 6.0.428 (hết hỗ trợ): bootstrap SDK 10.0.401 official vào .tools/dotnet; không cài global. Stack .NET 10/WPF, không dependency bên thứ ba.

Phase01 VERIFIED: launcher trên máy có cả Explorer/host elevated đã mở native child cùng SID/session, Medium, elevation=false/admin=false. 10 scalar/argv checks; native14 màn thật + dark. Native rename6PASS và RecycleFile5PASS, chỉ tài nguyên sở hữu. Full Release mới nhất135PASS/0FAIL/0SKIP; WPF45PASS/0FAIL/0SKIP. Review findings đã sửa, không còn identified P0/P1. Native host còn AppData redirection nên Windows mutations bị chặn theo policy; nghiệm thu mở Explorer/manual còn lại.

Không tác động startup/PATH/service/temp thật. SDK first-run đã tạo một ASP.NET development certificate; Lead xóa đúng certificate và key mới sinh theo timestamp, không đụng cert khác. Fixture HKCU random subtree đã dọn; RecycleFile test restore đúng file sở hữu. Tài nguyên còn: .tools SDK, artifacts/native-probe, .evidence/.runtime ignored, không native collector nền cố định.

Branch đã push, draft PR #1 mở tại https://github.com/NhanDuong21/window-system/pull/1 và gắn chat. Handover697922f, artifact15ca7b4; commit sau chỉ docs Git-status. Code/artifact đã kiểm xong; chỉ nghiệm thu manual ngoài host/UAC/service còn lại, không chạy trên cấu hình thật trong phát triển. Source nền nhiều module được tích hợp cùng lúc theo ownership; mỗi phase có commit đóng nghiệm thu riêng và status trung thực. Checkpoint không tự khởi động lại agent.

Phase 02 — VERIFIED: WindowsChecks + integration: CPU interval400ms, RAM/drives/uptime/build thật; .evidence/reader-checks/results.txt. Source nền 3588d39; checkpoint commit tìm bằng git log --grep=phase02.
Phase 03 — VERIFIED: Inventory registry32/64 user/machine + Appx thật Ready; filter/sort UI; không Win32_Product. Source nền 3588d39; checkpoint commit tìm bằng git log --grep=phase03.
Phase 04 — VERIFIED: Run User/folder fixture disable-enable bytes/type + read-only startup thật; StartupApproved ngoài scope. Source nền 3588d39; checkpoint commit tìm bằng git log --grep=phase04.
Phase 05 — VERIFIED: Owned process terminate, PID reuse/vanished/protection; CPU interval/RAM inventory thật. Source nền 3588d39; checkpoint commit tìm bằng git log --grep=phase05.
Phase 06 — VERIFIED: StoreChecks31PASS: Unicode/long/denied/cloud/links/cancel, metadata scan bounded2000rows. Source nền 3588d39; checkpoint commit tìm bằng git log --grep=phase06.
Phase 07 — IMPLEMENTED: Native services inventory Ready, protected/dependency policy tests; SCM start-stop/UAC thật chưa chạy. Source nền 3588d39; checkpoint commit tìm bằng git log --grep=phase07.
Phase 08 — VERIFIED: Tool inventory Ready, signed/trusted probe, child hooks stripped; Docker only local npipe. Source nền 3588d39; checkpoint commit tìm bằng git log --grep=phase08.
Phase 09 — VERIFIED: Owned localhost TCP/UDP native checks, v4/v6, PID time stability, process link; endpoint recheck implemented. Source nền 3588d39; checkpoint commit tìm bằng git log --grep=phase09.
Phase 10 — VERIFIED: Fixture HKCU env/PATH raw order/type, stale/cancel/DPAPI undo; System UAC còn manual. Source nền 3588d39; checkpoint commit tìm bằng git log --grep=phase10.
Phase 11 — VERIFIED: NetworkInterface thật, IP/DNS che mặc định, refresh; không public network requests. Source nền 3588d39; checkpoint commit tìm bằng git log --grep=phase11.
Phase 12 — VERIFIED: Store/Core snapshot create-diff-export-import, coverage partial, schema/size/DPAPI; cùng account. Source nền 3588d39; checkpoint commit tìm bằng git log --grep=phase12.
Phase 13 — VERIFIED: Cleanup fixture preview/change/cancel/native recycle; FileLease rename6PASS, RecycleFile5PASS gồm restore đúng bin item sở hữu. Native directory pin, no-overwrite, cloud/reparse/identity recheck. Không cleanup Temp User thật. Source sửa native mutation boundary trong commit phase13.
Phase 14 — VERIFIED: History phản ánh success/failed/cancelled/partial sau native verification; lỗi ghi history không đổi kết quả Windows đã biết. Target mask/truncate; retention30days/1000entries; StoreChecks và regression long-name/cancel/history đã chạy, UI filter/detail/undo nối Core.
Phase 15 — VERIFIED: Ctrl+K điều hướng+tìm cache app, keyboard/empty state; chọn mục mở trang/đối tượng. Không thực thi danger từ palette; preview có Cancel mặc định. WPF45PASS (fixture mode gắn nhãn), 12000rows filter và reveal-off clears sensitive cache.
Phase 16 — VERIFIED: Review độc lập đã tái hiện/sửa P1 helper/history/expiry; regression IPC/enum/undo/cancel/restore. Suite mới135PASS/0FAIL/0SKIP; WPF45PASS. Host có file redirection dù API package=false: own app root canonical; token/native boundary từ chối production mutation trong host đó. Physical target được nhận diện kể cả SH path đã canonical. Không còn identified P0/P1; UAC/service mutation vẫn manual.

Native integration follow-up Phase07/09: CIM/NetTCPIP denied trong app user thường. Thay read inventory bằng IP Helper owner tables và SCM enum/config. `.evidence/native-user-read/results.txt`8PASS/0FAIL, restricted Medium token, đủ4 TCP/UDP v4/v6 fixtures; native capture cuối Ports/Services Ready hơn300rows. Startup auto-service source cũng SCM. Phase07 mutation/UAC vẫn IMPLEMENTED, không đổi thành VERIFIED.

Phase 17 — VERIFIED: Self-contained probe `.evidence/release-9642cb1a56a74ae8be15b08bdc738ac9`: first ContentRendered1434ms, idle Settings5s settle+10s sample CPU0.013%/152.7MiB working set, DPI1.25. Ngân sách2500ms/1%/350MiB. Scan4000files526ms (budget15s), pre-cancel0ms, filter12000rows198.38ms; active cancellation/disposal native helper đã test. Poll5s chỉ active Dashboard/Processes, CIM cache20min, output concurrency2, virtualized/bounded rows. Probe khác artifact cuối; screenshot allocation không nằm trong idle sample. Host elevated launcher overhead không tính vào first-frame của child.

Phase18 VERIFIED portable/read-only scope: artifact source15ca7b4; `.evidence/release-4be36d156493468ba7ac8f81a92acec8`45UI +14native pages/dark, firstframe1124ms/idleCPU0.013%/RAM151MiB. Self-contained no-SDK lookup probe PASS (DOTNET_ROOT variants invalid, RollForward Disable). `smoke-summary.json` Native/User/UI/Performance PASS nhưng mutation context UNVERIFIED_HOST_REDIRECTION, real mutations NOT_RUN. Handover PARTIAL native mutation/UAC, Phase07 IMPLEMENTED. HANDOVER có18phase/commits/hash/manual/checklist. Chỉ docs sau commit artifact; không rebuild hoặc tuyên bố cùng source commit docs.
