# Kiến trúc

WPF/.NET10 Windows x64, SDK10.0.401 pin local; không dependency bên thứ ba. Native bảng/DPI/keyboard, không WebView2/server. Tauri đòi thêm JS/Rust/IPC; WinForms ít linh hoạt layout/theme hơn. WPF giảm dependency trên máy đích.

App → IControlCenter typed contract → Core → WindowsReader/Mutations/AppStore/StorageScanner. UI không chạy script. Không HTTP/debug bridge. Reader chỉ đọc; mutation chỉ action enum.

Registry, Win32 CPU/RAM, Process, NetworkInterface và Windows PowerShell executable cố định/script source-owned. Timeout/output/concurrency2, không raw stderr. Tool version chỉ signed executable ở trusted install roots; không Win32_Product/command lines.

Mutation: cached identity → fresh preview one-use TTL120s → UI confirm → serialized backend recheck → native → verify → history. File action dùng handle identity; cleanup rename vào quarantine cùng volume rồi Recycle Bin. Service giới hạn Win32OwnProcess bên thứ ba ngoài Windows, không dừng dependency dây chuyền.

Helper cùng EXE `--elevated-action GUID`: request DPAPI/ACL/max1MiB/expiry/one-use, chỉ System env/service. Confirm lại decoded action, active lease + expiry trước execution; trả kết quả mã hóa. Alternate admin không giải DPAPI được. File IPC tạo bằng CreateNew, parent directory được giữ bằng native handle; đọc qua handle kiểm tra danh tính và giới hạn kích thước.

App asInvoker. Nếu launcher đang elevated, thử token Explorer cùng user/session, linked token hoặc tạo LUA restricted token từ token của chính app. Child được tạo suspended, kiểm tra lại SID/session, elevation=false, không thuộc nhóm Administrators đang enabled, integrity Medium rồi mới resume. Trên máy đích cả Explorer lẫn host đều elevated; nhánh LUA đã chạy native thành công. Không thay đổi UAC, ACL Windows hay policy global. Không thể tạo/kiểm tra token phù hợp thì dừng với thông báo.

State DPAPI ngoài repo, schema1, atomic save/exclusive lock, retention30days/1000history, 100snapshots/undo, max20MiB. Import dữ liệu inert; undo native validate lại. Manual portable-folder update + backup/migration, không server update hoặc signing secret.
