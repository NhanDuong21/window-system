# Nyan Control Center

Desktop WPF tiếng Việt quản lý Windows local. Mở **Mo-Nyan.cmd** hoặc `artifacts/NyanControlCenter-1.0.0-win-x64/NyanControlCenter.exe` sau khi package. Bản self-contained gồm runtime trong cả thư mục; không chỉ sao chép riêng EXE.

Đọc [docs/HANDOVER.md](docs/HANDOVER.md) để nghiệm thu 18 phase và giới hạn. Không telemetry/cloud/backend HTTP. App chính chạy user thường.

Dev: Windows x64 build22631, PowerShell7, SDK .NET10.0.401 local. `./scripts/bootstrap.ps1` tải ZIP Microsoft và kiểm SHA-512, không cài global. Không dependency NuGet bên thứ ba; giữ lockfile.

```powershell
./scripts/dev.ps1
./scripts/verify.ps1
./scripts/package.ps1
./scripts/smoke-release.ps1
./scripts/open-release.ps1
```

Verify gồm build Release, unit/contract/Windows read-only, mutation fixture và WPF UI. Báo rõ UAC/service thật chưa chạy. Evidence/binary ignored; không commit inventory máy.

Dữ liệu `%LOCALAPPDATA%\NyanControlCenter`, DPAPI cùng user. Update/backup trong [docs/OPERATIONS.md](docs/OPERATIONS.md). Snapshot không phải Restore Point Windows.
