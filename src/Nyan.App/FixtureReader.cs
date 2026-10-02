using Nyan.Core;

namespace Nyan.App;

// Explicit --ui-checks only. Never used as fallback for production Windows failures.
internal sealed class FixtureReader : IWindowsReader
{
    public Task<ModuleResult> ReadAsync(Module module,bool reveal,CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();var columns=new List<Column>{new("name","Tên"),new("value","Giá trị"),new("source","Nguồn")};var rows=new List<Row>();
        for(int i=0;i<(module==Module.Processes?5000:8);i++)rows.Add(new("fixture-"+i,new(){{"name",i==0?"Dữ liệu kiểm thử — Tiếng Việt có dấu":$"Mục kiểm thử {i}"},{"value",i==0?"Không có dữ liệu Windows thật trong chế độ này":i.ToString()},{"source","Fixture có nhãn"}},new(){{"scope","Process"},{"name","NYAN_FIXTURE"},{"kind","task"}}));
        if(module==Module.Network)return Task.FromResult(ModuleResult.Failure(module,"Không có quyền — fixture kiểm tra trạng thái lỗi.",ResultState.Denied));
        return Task.FromResult(new ModuleResult(module,columns,rows,ResultState.Ready,"CHẾ ĐỘ KIỂM THỬ — fixture, không phải inventory máy.",DateTimeOffset.Now));
    }
}
