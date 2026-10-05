# Đợt sửa tập trung 1.0.3

Quyết định tiếp theo: dừng vòng sửa lớn và chuyển sang dùng thử. Gói mới `NyanControlCenter-1.0.3-win-x64-docs1` sửa hướng dẫn trong ZIP, giữ nguyên binary đã kiểm; thông tin hiện hành ở [HANDOVER.md](HANDOVER.md). Các kết quả dưới đây thuộc đúng binary1.0.3 được giữ lại; không nhận đã giải quyết spikeCPU hoặc hoàn tất Explorer/live-state/Windows/UAC.

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

Profiled observations không cộng vào unprofiled budget gate. Không tái hiện spike thì kết luận vẫn là nguyên nhân chưa xác định; không nhận một seriesPASS là chứng minh mọi điều kiện ổn định.

## Kết quả cuối — artifact 1.0.3

Source `3dddf48ceec4c288ebcc309f9831f64c730ec5bd`; EXE/ZIP và toàn bộ 403 file đã kiểm hash. Exact hashes ở [HANDOVER.md](HANDOVER.md). Commit sau chỉ cập nhật tài liệu, không thay bản đã đo.

`./scripts/verify.ps1`: **99 PASS / 0 FAIL / 0 SKIP**, report **13 PASS trên Windows PowerShell 5.1**, WPF **70 PASS**, build không có warning/error. Evidence: `.evidence/verify-500d1ec1770949aeb6d375fa63241b5d`. Chính artifact chạy lại WPF 70 PASS với SDK lookup bị vô hiệu; 3 lượt native read/private persistence/user/performance đều PASS. Evidence: `.evidence/performance-series-ab559c34fb4546fc8abbfaf604840bad`.

| Lượt không profiler | Khung hình đầu | CPU idle | Working set |
|---|---:|---:|---:|
| 1 | 981 ms | 0% | 146,37 MiB |
| 2 | 695 ms | 0,013019% | 138,28 MiB |
| 3 | 774 ms | 0% | 147,48 MiB |

Cả 3 lượt: Cài đặt, foreground và visible, scaling 125%, không có tác vụ active ở đầu/cuối, 12 CPU logic, settle 5 giây rồi đo 10 giây. Ngưỡng giữ nguyên: 2.500 ms / 1% / 350 MiB. Giá trị CPU 0 phản ánh độ phân giải counter; không chứng minh tuyệt đối không có CPU work. Số lượt đã ghi trong plan trước khi chạy, giữ cả ba kết quả.

Lượt profile riêng duy nhất trên 1.0.3: `.evidence/cpu-artifact3-stream-c3dd2b4318014038bfead303507d3e3f`, collector exit 0, native PASS; 46,875 ms process CPU trong 9.999,6089 ms, chuẩn hóa 12 CPU = 0,039064%. Thread 20944 ghi 46,875 ms; chỉ có hai managed samples trên thread này, ở WPF automation/Dispatcher. Đây là quan sát nhỏ trong một lượt không tái hiện spike, chưa đủ phân bổ chi phí theo hàm hoặc quy nguyên nhân. 32.498 state samples chủ yếu External không phải số mẫu đang dùng CPU. Native/unresolved frames còn giới hạn; baseline 1.0.2 chỉ có window ước tính. Không xuất ProcessInfo payload, command line hoặc environment; thư viện có thể xử lý metadata nội bộ trong RAM, raw trace/ETLX mới không được lưu.

Output nghiệm thu cho scope đã chạy:

```text
Đợt sửa 5 finding: ĐẠT kiểm tra hồi quy
Đọc dữ liệu Windows: ĐẠT (3/3 lượt)
Lưu dữ liệu ứng dụng riêng: ĐẠT (3/3 lượt)
Hiệu năng bản 1.0.3 ở điều kiện đã ghi: ĐẠT (3/3 lượt)
Nguyên nhân CPU fail cũ: CHƯA XÁC ĐỊNH — evidence fail giữ nguyên
Mở lại/theme ngoài host và live state: CHƯA HOÀN TẤT
Thao tác Windows/UAC: CHƯA CHẠY
Kết luận toàn bộ: PARTIAL
```

Kho vẫn gồm 100 backup dùng chung các loại và 100 snapshot riêng. Chỉ environment undo/snapshot được discard; không hứa mọi kho đầy đều có mục được phép bỏ. Hướng dẫn source đã cập nhật sang 1.0.3; bản hướng dẫn riêng `artifacts/HUONG-DAN-1.0.3.md` đi cùng artifact, giữ ZIP/hash cũ nguyên vẹn. `HUONG-DAN.md` trong ZIP còn nhắc đường dẫn 1.0.2 theo bản tài liệu tại lúc đóng gói.

## Giới hạn còn lại

User Explorer1.0.2 đã đóng phiên đầu nhưng thiếu reopen/human summary. Explorer1.0.3 và production Windows/UAC chưa chạy; safe suites, native capture và app/file fixtures không thay thế phần này. Không thử trên startup/PATH/service/Temp có sẵn. PR#1 giữ draft, không merge. Không sửa Windows configuration hoặc global security/toolchain.
