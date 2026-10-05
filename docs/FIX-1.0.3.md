# Đợt sửa tập trung 1.0.3

Giữ WPF/.NET10, UI tiếng Việt, 14 màn và phạm vi 18 phase. Không mở rộng quyền admin/app, không đổi ngân sách hiệu năng. Năm lỗi có nguyên nhân rõ đã sửa; CPU là nhánh điều tra riêng, chưa tuyên bố đã giải quyết spike cũ.

| Finding | Sửa | Regression |
|---|---|---|
| Scan chồng nhau | Chặn double-click khi busy; CTS/ID mỗi lượt; navigation cancel và vô hiệu progress/result/finally cũ | Real WPF event + deterministic IControlCenter: double-click liên tục, hủy đúng token, chuyển trang/revisit, late progress/result/busy |
| Quota undo/snapshot | Settings → Dữ liệu và bản hoàn tác; số lượng/list; preview TTL, xác nhận, fingerprint recheck khi discard environment/snapshot | 100 backup và history quá31ngày, reload inventory, discard1→ghi mới; stale/cancel/one-use/restore; Startup/quarantine giữ nguyên |
| Báo cáo hiệu năng | Summary ngay trước/sau native capture, mỗi close và human checklist; CPU/frame/RAM gate; lỗi không bị human OK che; exit1 khi FAIL | 13 ca PowerShell5.1: CPU FAIL/native PASS, summary sớm, UI chưa đủ/humanOK/theme mismatch, boundary, malformed/missing/negative/budgetchange |
| HKCU trùng | Shared keys đọc1view, giữ Registry64 IDs; HKLM giữ2views, không dedup theo tên | Artifact1.0.2 native baseline FAIL29App+16Startup duplicates; reader mới đối chiếu native count/IDs; snapshot old32+64 aliases hai chiều, giữ true diff/sameNameDistinct |
| Sort dung lượng | Numeric metadata length/allocatedBytes; unknown luôn cuối, táchzero | size+allocated, asc/desc, display dấu phẩy/chấm không đổi thứ tự |

Baseline lỗi giữ nguyên tại `.evidence/ui-review-scan-overlap`, `.evidence/quota-review-7de675c49d184827a24150cb7089c4a3`, `.evidence/registry-regression-baseline-20261005` và evidence Explorer1.0.2. Lần quota runner đầu có store_write chưa giải thích, không tự coi là lỗi sản phẩm mới. PATH UNC/ổ mạng vẫn là candidate chưa tái hiện.

## Dữ liệu và tính tương thích

Quota vẫn100, không tự xóa theo History retention. Environment undo được quản lý dù History hết hạn; bỏ bản sao không thay đổi Windows. Startup backup chỉ Undo; quarantine chỉ xem và phục hồi thủ công. Snapshot được chủ động xóa sau preview/xác nhận. Backup schema1 cũ không có createdAt vẫn đọc được, UI ghi không có thời điểm.

Snapshot mới chỉ giữ alias hashed `__legacyId`, không raw registry ID hoặc Row.Data. Khi so sánh, canonical Registry64 thắng dòng32 trùng; chuẩn hóa label shared cho đúng những row có alias. Không gộp phần mềm theo tên, không đổi bản hoàn tác key/view cũ. Helper whitelist và Windows mutation guards giữ nguyên; chỉ hai action appdata mới được chạy trong host có redirection.

## Điều tra CPU và kế hoạch đo

Old budgets2500ms/1%/350MiB, Settings settle5s + sample10s giữ nguyên. Số đo là TotalProcessorTime của chính Nyan, chuẩn hóa theo CPU logic. Source mới bổ sung thời điểm/elapsed, sốCPUlogic, page, foreground đầu/cuối, visibility, task state, DPI và deltaCPU từng thread của chính app.

Chốt trước số lượt: **1 baseline trace1.0.2, 3 lượt không profiler trên artifact1.0.3, 1 profiling riêng trên artifact1.0.3**; không retry đến khi PASS. WPF artifact được kiểm riêng trước series, gồm invalid DOTNET_ROOT variants/roll-forward disable.

Baseline profile duy nhất `.evidence/cpu-baseline-trace-86c199118377479e8b49f82e1de1d353`: idle0.03906%, nativePASS, không tái hiện spike. SampleProfiler phần lớn External/idle waits; sample count là thread residency, không phải CPU milliseconds. Không quy nguyên nhân spike2.1–2.2% cho WPF/reader hoặc đổi timer. Collector local `.tools/diagnostics` chỉ công cụ phát triển, không dependency của app hay toolchain global. EventPipe mặc định có ProcessInfo descriptor; không đọc/xuất payload commandline/environment. Lượt mới dùng streaming filter chỉ lưu samples/frames/threads, không raw trace metadata.

Kết quả artifact/series/profile cuối được ghi ở HANDOVER/STATE sau đóng gói. Profiled observations không cộng vào unprofiled budget gate. Nếu không tái hiện spike, kết luận vẫn là nguyên nhân chưa xác định; không nhận một seriesPASS là chứng minh mọi điều kiện ổn định.

## Giới hạn còn lại

User Explorer1.0.2 đã đóng phiên đầu nhưng thiếu reopen/human summary. Explorer1.0.3 và production Windows/UAC chưa chạy; safe suites, native capture và app/file fixtures không thay thế phần này. Không thử trên startup/PATH/service/Temp có sẵn. PR#1 giữ draft, không merge. Không sửa Windows configuration hoặc global security/toolchain.
