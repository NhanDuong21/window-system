# Quy tắc cho người tiếp tục

Sản phẩm Windows local một người. Đọc PRODUCT.md, docs/PLAN.md, docs/STATE.md trước khi tiếp tục. Không giả định checkpoint tự khởi động agent.

Không sửa cấu hình Windows thật trong phát triển; mutation tests chỉ dùng tài nguyên fixture có marker sở hữu. Không đọc credentials, toàn bộ environment hay command lines vào log. Evidence, binaries, runtime chỉ ở thư mục ignored. Không push main, force-push hoặc tự merge.

Source: Core có contract kiểu, adapter Windows, policy mutation; App chỉ gọi IControlCenter. Không thêm shell tùy ý, listener hoặc quyền admin cho app chính. Input UI không được ghép vào script. Mọi mutation qua preview có thời hạn, xác nhận và kiểm tra lại native.

Commands (PowerShell): ./scripts/dev.ps1; ./scripts/verify.ps1; ./scripts/package.ps1. SDK local .tools/dotnet, pin global.json; không đổi toolchain global.

Ownership trong phiên: Lead quản lý Contracts.cs, ControlCenter.cs, Mutations.cs, NativeSecurity.cs, Program.cs, tests/Program.cs, scripts, cấu hình và tài liệu. Worker Windows chỉ WindowsReader.cs và ReadProcess.cs. Worker UI chỉ MainWindow.cs, Theme.cs, UiChecks.cs. Worker storage chỉ AppStore.cs, StorageScanner.cs, StoreChecks.cs. Không sửa lockfiles của nhau.
