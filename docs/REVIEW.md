# Review

1.0.3: UI/storage/Windows workers sửa đúng ownership, Lead tích hợp contract/mutations/scripts. Storage worker review độc lập appdata mutation route và report: TTL/token/cancel/restore/fingerprint đúng, helper whitelist và Windowsguard giữ nguyên; test yêu cầu HKCU>0 đã bỏ để empty inventory hợp lệ. New regressions99safe +13PS5report +70WPF. Source/package/CPU evidence cuối ở FIX-1.0.3/HANDOVER/STATE. Baseline FAIL, runner FAIL và spikeCPU cũ giữ nguyên; không nhận chưa tái hiện là đã sửa CPU.

Evidence user Explorer 1.0.2 `.evidence/acceptance-858cceaea5ce4551936a796aaa272bc7`: identity/BOM/native/private persistence PASS, elevated/package/redirection đều false. UI mới có first session-open; chưa có hai close records/human summary. Idle CPU 2.2257% vượt budget 1% lần nữa dù first frame/RAM đạt; không gọi bản này hoàn tất performance, chưa có source fix cho CPU. Acceptance launcher hiện không gate performance trong summary; native PASS/human OK không thay cho performance PASS. Không chạy thêm hoặc thay ngưỡng để tìm PASS; HANDOVER/STATE ghi PARTIAL và bước người dùng còn thiếu. Đây là kiểm evidence/rà source bởi Lead, không gọi review độc lập.

Patch1.0.2: actual user Explorer evidence tái hiện JSON UTF8 BOM của PS5.1 bị parser1.0.1 từ chối. Bounded/native JSON reader chỉ bỏ một leading UTF8 marker; raw DPAPI/guard giữ nguyên. 9 regressions bằng writer PS5.1 thật, safe suite62PASS/WPF46PASS; artifact BOM/native/private persistence PASS. Imported FAIL giờ làm harness exit1 (expected-failure probe). First performance sample FAIL CPU2.12%; một recheck PASS0.013%, giữ cả hai và chưa khẳng định ổn định. Đây là Lead diagnosis/regression, không gọi review độc lập hoặc Explorer1.0.2 PASS. HANDOVER/STATE ghi source/hash/evidence và user retry.

Lượt nghiệm thu1.0.1: Lead tự tái hiện service outcome bằng adapter mô phỏng, không gọi đây là review độc lập. Stop accepted timeout, restart stop thành công/start lỗi, cancel sau send và helper unconfirmed trước đây mất chi tiết/unknown. Sửa partial với native reread/completed steps; stale không gửi command, không repair. 13 regression PASS trong safe suite53; UI46 giữ selected stable row khi refresh. Actual artifact native read/private persistence PASS, production SCM/UAC/resource workflows **NOT_RUN**. Chi tiết mới và capability matrix ở HANDOVER; các kết quả independent review dưới đây thuộc baseline trước.

Worker storage review độc lập phần Lead native/policy và chạy fixture riêng để tái hiện findings. Reader/UI tự kiểm phần họ, không gọi self-review độc lập.

Đã phát hiện/sửa: Dispose hai lần; partial snapshot false diff; Startup registration khác approval; contents bound4MiB; env undo validate; port PID identity/recheck; child hooks/Docker remote config; cancel before semaphore; history save che native outcome; helper expired confirm/cancel lease; imported backup type/casing.

Regression/evidence/final outcome cập nhật ở HANDOVER/STATE. Không scanner được dùng để gọi app an toàn tuyệt đối; giới hạn race ngoài app/UAC thật được ghi riêng.

| Mức | Finding và sửa | Bằng chứng |
|---|---|---|
| P1 | Helper result leaf có thể bị ghi đè; CreateNew, parent lease, bounded native handle read | 7 IPC regression + review source độc lập |
| P1 | Hết hạn sau confirm hoặc cancel-before-lock có thể thực thi lại; kiểm tra expiry tại native boundary, nonce one-use | Full verify, expiry/cancel regression |
| P1 | History lỗi làm mất kết quả native đã xác minh; giữ outcome, mask/truncate target/message | Long registry-name và cancel history tests |
| P1 | Recycle chưa chứng minh không xóa vĩnh viễn; modern IFileOperation pre/post callback, no fallback | `.evidence/recycle-owned/results.txt`:5PASS gồm restore file exact |
| P2 | Rename lỗi87 và parent metadata handle không pin; FILE_RENAME_INFO terminated absolute UTF16, LIST_DIRECTORY/no SHARE_DELETE | `.evidence/rename-owned/results.txt`:6PASS |
| P2 | Numeric enum/backup type/casing, stale undo; strict literal kind/view, canonical bool, native recheck | Fixture registry + imported backup regressions |
| P2 | Restore còn preview/cache; serialize restore + invalidate | Controller restore-preview regression |
| P2 | Service Disabled restart có thể stop trước khi thất bại; chặn trước native stop | Source review; SCM mutation/UAC thật chưa test |
| P2 | Host chuyển hướng AppData khi package identity=false; canonical app-owned root và hai native mutation guards | Owned native path probe + direct/token guard regressions |

Worker storage đã tái hiện findings bằng runner riêng và full suite123PASS/0FAIL/0SKIP trước IPC tightening; sau đó review diff cập nhật độc lập. Lead chạy lại toàn bộ suite mới nhất, ghi số/evidence cuối trong HANDOVER. Review này không chứng minh an toàn trước malware đã kiểm soát tài khoản hoặc admin.
