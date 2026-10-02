# Bảo mật và dữ liệu

UI/import untrusted; đối tượng có thể biến mất/PID reuse/stale preview; quyền user khác nhau; backups có secrets. Không chống process đã kiểm soát toàn tài khoản hoặc Administrator độc hại.

Backend enum/policy/validation/one-use TTL120s/serialized/recheck. Process critical/Windows/khác user/session bị bảo vệ, không kill tree. Services không tự coi third-party an toàn để tắt, chặn dependency chain. Registry recheck best effort: Windows không cung cấp compare-and-swap writer ngoài app.

Files reject UNC/network/reparse/cloud-only; hardlinks mutation chặn; scope Temp/Startup User rõ. File handle bound identity trước rename quarantine. Scan metadata không đọc nội dung/hydrate. Startup backup bytes chỉ file nhỏ user chủ động chọn.

Mask personal paths, ALL env values, IP/DNS default. Reveal chủ động in-memory. Snapshot strip Data/env values/hashIDs/coverage; partial không suy luận Removed/Added. Error redacted. DPAPI/ACL user, log bound/retention, không inventory thật vào docs/Git.

Subprocess fixed executable/args/scripts; timeout20s/5s, stdout8MiB/stderr16KiB, max2. Signed trusted tool probes loại child injection env; Docker chỉ local npipe/config cô lập, không đọc credentials. UI không arbitrary command.

UAC file request DPAPI/ACL/TTL/one-use/kind/size; decoded action confirm, active lease/expiry recheck. Không listener/LAN/debug bridge. External tools chỉ fixed ms-settings/System executable.

Repo PUBLIC: .evidence/.runtime/.tools/artifacts ignored; kiểm staged trước commit. UAC thật/service thật chưa chạy được ghi rõ; không tuyên bố an toàn tuyệt đối.
