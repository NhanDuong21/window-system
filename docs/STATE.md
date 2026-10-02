# Checkpoint

Branch nyan/control-center từ origin/main; repo PUBLIC. Windows native x64 build 22631. Không có code hoặc thay đổi người dùng. SDK global chỉ 6.0.428 (hết hỗ trợ): bootstrap SDK 10.0.401 official vào .tools/dotnet; không cài global. Stack .NET 10/WPF, không dependency bên thứ ba.

Phase01 VERIFIED: launcher trên máy có cả Explorer/host elevated đã mở native child cùng SID/session, Medium, elevation=false/admin=false. 10 scalar/argv checks; `.evidence/launcher-verified-native/result.txt` PASS14 màn thật + dark. Native rename6PASS và RecycleFile5PASS, chỉ tài nguyên sở hữu. Full Release gần nhất126PASS/0FAIL/0SKIP; WPF45PASS. Đang chạy lại sau thêm7 regression IPC và đo self-contained release; số cuối lấy từ lần verify mới nhất. Review độc lập không còn identified P0/P1 sau sửa.

Không tác động startup/PATH/service/temp thật. SDK first-run đã tạo một ASP.NET development certificate; Lead xóa đúng certificate và key mới sinh theo timestamp, không đụng cert khác. Fixture HKCU random subtree đã dọn; RecycleFile test restore đúng file sở hữu. Tài nguyên còn: .tools SDK, artifacts/native-probe, .evidence/.runtime ignored, không native collector nền cố định.

Tiếp theo: đóng Phase13–17 bằng kiểm chứng, package từ commit sạch, smoke artifact cuối, HANDOVER Phase18, push branch riêng/PR khi auth sẵn có. Source nền nhiều module được tích hợp cùng lúc theo ownership; mỗi phase có commit đóng nghiệm thu riêng và status trung thực. Checkpoint không tự khởi động lại agent.

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
