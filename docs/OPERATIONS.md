# Vận hành

Mở `Mo-Nyan.cmd` hoặc `artifacts/NyanControlCenter-1.0.0-win-x64/NyanControlCenter.exe`. Copy cả folder release tới thư mục riêng nếu muốn. Không installer/startup/service được cài. .NET/WindowsDesktop nằm cùng app; Windows PowerShell5.1/CIM/NetTCPIP dùng thành phần sẵn trên máy. Thiếu nguồn hiện lỗi/partial, không mock.

Lần đầu light, nút theme lưu dark. `%LOCALAPPDATA%\NyanControlCenter`: state.ncc, state.lock, requests, quarantine nếu có. DPAPI CurrentUser; max20MiB, 1000history/30days, 100snapshots/undo. Không raw commandline/env/hostname logs.

Cài đặt → Sao lưu `.nccbackup`; Khôi phục → chọn file và xác nhận. Chỉ dữ liệu app, không tự đổi Windows. Undo riêng action cần preview/recheck. Snapshot `.nccsnapshot` cùng tài khoản Windows, không gửi public.

Update: đóng app, backup, build artifact mới, giải nén folder mới, kiểm SHA-256 từ manifest local tin cậy, mở EXE mới. Giữ folder cũ để rollback cùng schema. Migration fixture được test; schema tương lai bị từ chối. Không ghi đè file app đang chạy.

Gỡ: đóng app, xóa đúng folder portable do bạn chọn. Dữ liệu giữ nguyên. Người dùng có thể backup rồi xóa đúng thư mục appdata riêng; app không tự dọn dữ liệu cá nhân/global dependencies.

State hỏng: app báo lỗi, giữ file gốc. Đóng app, sao lưu cả thư mục; người dùng di chuyển state hỏng sang tên khác, mở state mới rồi restore backup tốt. Không xóa âm thầm. Plaintext v0 chỉ fixture migration.

Cleanup Temp User cũ7days, không default selection. Chỉ người dùng chọn mới recycle; app không hứa undo. Crash có thể để file trong quarantine với bản ghi original path/identity DPAPI; không tự xóa quarantine. Giữ file và xem history/hỗ trợ khôi phục thủ công. Không Downloads/browser/source/database/Windows cleanup.

App chưa ký. Không tắt SmartScreen/UAC/Defender. UAC hủy/DPAPI khác account hiện cancelled/failed. App chính user thường; action cao xác nhận riêng.
