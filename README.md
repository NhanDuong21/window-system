# Nyan Control Center

Desktop WPF tiếng Việt quản lý Windows local. Mở **Mo-Nyan.cmd** hoặc `artifacts/NyanControlCenter-1.0.2-win-x64/NyanControlCenter.exe` sau khi package. Nghiệm thu bằng **Nghiem-Thu-Nyan.cmd từ File Explorer**; mặc định chỉ đọc Windows và dữ liệu app riêng. Bản self-contained gồm runtime trong cả thư mục; không chỉ sao chép riêng EXE. Giữ bản1.0.0/1.0.1 để đối chiếu.

Đọc [docs/HANDOVER.md](docs/HANDOVER.md) để nghiệm thu 18 phase và giới hạn. Không telemetry/cloud/backend HTTP. App chính chạy user thường.

Dev: Windows x64 build22631, PowerShell7, SDK .NET10.0.401 local. `./scripts/bootstrap.ps1` tải ZIP Microsoft và kiểm SHA-512, không cài global. Không dependency NuGet bên thứ ba; giữ lockfile.

```powershell
./scripts/dev.ps1
./scripts/verify.ps1
./scripts/package.ps1
./scripts/smoke-release.ps1
./scripts/open-release.ps1
```

Verify hiện tại gồm Release build, regression service mô phỏng, app-store/files riêng, Windows read-only và WPF controls. Không chạy Windows registry/process/listener/recycle fixtures cũ trong lượt nghiệm thu này. Các bài production mutation cần xác nhận GUID riêng trong launcher; Explorer/UAC chưa chạy không tính PASS. Evidence mới mỗi lần, binary ignored; không commit inventory máy.

Dữ liệu `%LOCALAPPDATA%\NyanControlCenter`, DPAPI cùng user. Update/backup trong [docs/OPERATIONS.md](docs/OPERATIONS.md). Snapshot không phải Restore Point Windows.
