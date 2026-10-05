# Kiểm thử

`./scripts/verify.ps1`: locked restore, Release build, console assertions/exit code, WindowsChecks/StoreChecks và WPF UiChecks. Evidence ignored `.evidence/verify`: tests.txt, summary.json, performance.json, UI PNG/result. -SkipUI báo SKIP. Không NuGet test framework; số PASS/FAIL/SKIP tổng hợp JSON.

Owned fixtures marker .nyan-owned/.nyan-fixture; random HKCU Software/NyanControlCenter.Tests subtree được xóa cuối test. Chỉ process/listener tự tạo được kill/close. Cleanup test chỉ file fixture cũ10days, không Temp User. Fixtures giữ local để kiểm tra.

Coverage: native metadata, timeout/cancel/output cap, Unicode/long path, deniedACL, cloudOFFLINE, links, PIDreuse/disappeared, stale file/registry, migration/corrupt, DPAPI restore/import, partial snapshot, theme restart, large scan/filter. UI14pages/light-dark/resize/search/states/preview default Cancel/confirm/cleanup unselected.

Manual còn lại: UAC thật/alternate admin, service thật, PATH/startup thật, Temp User cleanup; Windows StartupApproved policy. Không tính PASS. Screenshot thật local ignored.

`./scripts/smoke-release.ps1`: chạy chính EXE self-contained cuối ở hai chế độ tường minh: WPF fixture45 checks và native read-only14pages/light-dark. Ghi trạng thái/số dòng từng màn, fail nếu Ports/Services denied/empty, đo first ContentRendered và stable idle5s+10s trước capture allocations, kiểm ngân sách. Kết quả trong `.evidence/release-<GUID>`; không upload ảnh/dữ liệu máy. Có thể truyền `-AppPath` để kiểm bản dev publish; kết quả probe khác artifact cuối.

`smoke-summary.json` tách PASS read-only/UI/budget khỏi NOT_RUN real mutation. Nếu host còn native AppData redirection, ghi `UNVERIFIED_HOST_REDIRECTION`: production guard từ chối thực thi Windows writes; đây là giới hạn chưa nghiệm thu, không phải mutation PASS. Mở từ Explorer bên ngoài host cần kiểm thủ công. Lần smoke trước đã thất bại ở điều kiện context này; kết quả native read-only vẫn thành công, không nới backend policy.

Giới hạn: UI tests gọi event/controls của WPF thật và RenderTargetBitmap, không dùng OS UIAutomation; không kiểm interaction UAC thật. CPU idle Settings không đại diện tải Dashboard polling. Metric cancellation fixture pre-cancel1ms; StoreChecks bổ sung hủy scan đang chạy. App disposal test xác nhận collector PowerShell do app tạo đã kết thúc; không tìm/kill process ngoài sở hữu.
