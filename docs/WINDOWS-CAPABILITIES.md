# Nguồn và giới hạn Windows

| Module | API/nguồn | Quyền/phạm vi | Giới hạn |
|---|---|---|---|
| Dashboard | GetSystemTimes/GlobalMemoryStatusEx/DriveInfo/CIM | User read | CPU400ms, hardware cache20min; không nhiệt độ |
| Apps | Registry user/machine32/64 + Appx | User read | Không tuyệt đối đầy đủ, không Win32_Product |
| Startup | Run/RunOnce/folders/tasks/auto services | Mutation User Run/folder | Registration khác StartupApproved; System/task/service readonly |
| Processes | Process/token/critical native API | Same user/session, ngoài Windows | Creation UTC + handle, không kill tree |
| Storage | Directory metadata, FileID/allocated size | Local only | Skip links/cloud/denied, hardlink dedup, rows2000 |
| Services | EnumServicesStatusEx/QueryServiceConfig + SCM | Read user, UAC action hẹp | OwnProcess bên thứ ba, no dependency stop chain |
| Dev tools | Known resolve paths/AuthentiCode/version | User | Untrusted metadata only; Docker local pipe |
| Ports | GetExtendedTcpTable/GetExtendedUdpTable OWNER_PID | User read | v4/v6 TCP/UDP + stable PID/time |
| Env | Raw registry User/System, Process env | User, System UAC | Preserve type/order; Process readonly; values masked |
| Network | NetworkInterface/IP properties | User read | Local only, không public IP request |
| Snapshot/history | DPAPI AppStore | Same user | Không Restore Point; coverage partial |
| Cleanup | User Temp metadata/handle rename/Recycle Bin | Selected files >7days | No default selection, stale/links/locked skip |

Official: [WPF](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/), [CPU](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getsystemtimes), [process times](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getprocesstimes), [file mutation](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-setfileinformationbyhandle), [DPAPI](https://learn.microsoft.com/en-us/windows/win32/api/dpapi/nf-dpapi-cryptprotectdata), [SCM](https://learn.microsoft.com/en-us/windows/win32/services/service-control-manager), [Recycle flags](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifileoperation-setoperationflags).

Đã đọc [Impeccable README](https://github.com/pbakaus/impeccable) và [SKILL](https://github.com/pbakaus/impeccable/blob/main/.agents/skills/impeccable/SKILL.md) v4.5.0/reference native trực tiếp; áp dụng tài liệu, không chạy launcher/hook. Antigravity CLI không có PATH.

Native inventory: [TCP owner table](https://learn.microsoft.com/en-us/windows/win32/api/iphlpapi/nf-iphlpapi-getextendedtcptable), [UDP owner table](https://learn.microsoft.com/en-us/windows/win32/api/iphlpapi/nf-iphlpapi-getextendedudptable), [SCM enumeration](https://learn.microsoft.com/en-us/windows/win32/api/winsvc/nf-winsvc-enumservicesstatusexw). CIM/NetTCPIP đã bị từ chối trong native user thường trên máy đích; Ports/Services chuyển sang API trực tiếp, không yêu cầu elevated inventory. Hardware CIM không đọc được vẫn hiện Partial.

AppData host: [known folder flags](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/ne-shlobj_core-known_folder_flag), [Desktop Bridge redirection](https://learn.microsoft.com/en-us/windows/msix/desktop/flexible-virtualization). Ngoài package identity, kiểm tra acquired native app-directory path; không sửa host policy. Launcher xác minh token với Win32 APIs, không dựa chỉ vào manifest asInvoker.
