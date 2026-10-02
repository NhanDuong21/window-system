# Kiểm thử

`./scripts/verify.ps1`: locked restore, Release build, console assertions/exit code, WindowsChecks/StoreChecks và WPF UiChecks. Evidence ignored `.evidence/verify`: tests.txt, summary.json, performance.json, UI PNG/result. -SkipUI báo SKIP. Không NuGet test framework; số PASS/FAIL/SKIP tổng hợp JSON.

Owned fixtures marker .nyan-owned/.nyan-fixture; random HKCU Software/NyanControlCenter.Tests subtree được xóa cuối test. Chỉ process/listener tự tạo được kill/close. Cleanup test chỉ file fixture cũ10days, không Temp User. Fixtures giữ local để kiểm tra.

Coverage: native metadata, timeout/cancel/output cap, Unicode/long path, deniedACL, cloudOFFLINE, links, PIDreuse/disappeared, stale file/registry, migration/corrupt, DPAPI restore/import, partial snapshot, theme restart, large scan/filter. UI14pages/light-dark/resize/search/states/preview default Cancel/confirm/cleanup unselected.

Manual còn lại: UAC thật/alternate admin, service thật, PATH/startup thật, Temp User cleanup; Windows StartupApproved policy. Không tính PASS. Screenshot thật local ignored.
