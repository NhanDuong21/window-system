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
Phase 13 — VERIFIED: Cleanup fixture preview/change/cancel/native recycle; FileLease rename6PASS, RecycleFile5PASS gồm restore đúng bin item sở hữu. Native directory pin, no-overwrite, cloud/reparse/identity recheck. Không cleanup Temp User thật. Source sửa native mutation boundary trong commit phase13.
Phase 14 — VERIFIED: History phản ánh success/failed/cancelled/partial sau native verification; lỗi ghi history không đổi kết quả Windows đã biết. Target mask/truncate; retention30days/1000entries; StoreChecks và regression long-name/cancel/history đã chạy, UI filter/detail/undo nối Core.
Phase 15 — VERIFIED: Ctrl+K điều hướng+tìm cache app, keyboard/empty state; chọn mục mở trang/đối tượng. Không thực thi danger từ palette; preview có Cancel mặc định. WPF45PASS (fixture mode gắn nhãn), 12000rows filter và reveal-off clears sensitive cache.
Phase 16 — VERIFIED: Review độc lập đã tái hiện/sửa P1 helper/history/expiry; regression IPC/enum/undo/cancel/restore. Suite mới135PASS/0FAIL/0SKIP; WPF45PASS. Host có file redirection dù API package=false: own app root canonical; token/native boundary từ chối production mutation trong host đó. Physical target được nhận diện kể cả SH path đã canonical. Không còn identified P0/P1; UAC/service mutation vẫn manual.

Native integration follow-up Phase07/09: CIM/NetTCPIP denied trong app user thường. Thay read inventory bằng IP Helper owner tables và SCM enum/config. `.evidence/native-user-read/results.txt`8PASS/0FAIL, restricted Medium token, đủ4 TCP/UDP v4/v6 fixtures; native capture cuối Ports/Services Ready hơn300rows. Startup auto-service source cũng SCM. Phase07 mutation/UAC vẫn IMPLEMENTED, không đổi thành VERIFIED.
