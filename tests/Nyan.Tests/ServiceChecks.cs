using Nyan.Core;

namespace Nyan.Tests;

static class ServiceChecks
{
    public static async Task<List<string>> RunAsync(string root)
    {
        var results=new List<string>();
        void Check(string title,bool ok)=>results.Add((ok?"PASS ":"FAIL ")+title);
        var identity=new ServiceIdentity("NYAN_ACCEPTANCE_simulated",4,0x10,3,Environment.ProcessPath!,1,[]);
        async Task<ActionOutcome> Run(Session s,CancellationToken token=default,ActionKind action=ActionKind.RestartService,string? fingerprint=null)
            =>await ServicesNative.PerformAsync(identity.Name,action,fingerprint??s.Initial.Fingerprint,token,()=>s,TimeSpan.FromMilliseconds(10),ct=>Task.Delay(1,ct));
        var timeout=new Session(identity){StopPending=true};var result=await Run(timeout,action:ActionKind.StopService);
        Check("service accepted Stop timeout is partial with pending native state",result.Status=="partial"&&result.Message.Contains("Windows nhận: Stop")&&result.Message.Contains("Stop pending")&&timeout.Stops==1&&timeout.Starts==0);
        var failedStart=new Session(identity){FailStart=true};result=await Run(failedStart);
        Check("service restart retains completed Stop when Start fails",result.Status=="partial"&&result.Message.Contains("Stop → Stopped")&&result.Message.Contains("gửi Start")&&result.Message.Contains("Stopped (1)")&&failedStart.Stops==1&&failedStart.Starts==1);
        using var cts=new CancellationTokenSource();var cancelled=new Session(identity){StopPending=true,AfterStop=cts.Cancel};result=await Run(cancelled,cts.Token);
        Check("service cancel after native Stop is partial and never implies unchanged Windows",result.Status=="partial"&&result.Message.Contains("Đã hủy chờ")&&result.Message.Contains("không được hoàn tác")&&cancelled.Starts==0);
        using var between=new CancellationTokenSource();var stopped=new Session(identity){AfterStop=between.Cancel};result=await Run(stopped,between.Token);
        Check("service cancellation never issues next restart command",result.Status=="partial"&&stopped.Stops==1&&stopped.Starts==0);
        var stale=new Session(identity with{State=1});result=await Run(stale,fingerprint:identity.Fingerprint);
        Check("service changed preview sends no commands",result.Status=="failed"&&stale.Stops==0&&stale.Starts==0&&result.Message.Contains("thay đổi"));
        var preCancel=new Session(identity);result=await Run(preCancel,new CancellationToken(true));
        Check("service cancelled before send remains cancelled",result.Status=="cancelled"&&preCancel.Stops==0&&preCancel.Starts==0&&result.Message.Contains("trước khi gửi"));
        var unknown=new Session(identity){StopPending=true,UnknownAfterStop=true};result=await Run(unknown,action:ActionKind.StopService);
        Check("service failed native reread explicitly reports unknown state",result.Status=="partial"&&result.Message.Contains("chưa xác định; cần tải lại"));
        var healthy=new Session(identity);result=await Run(healthy);
        Check("service restart verifies both steps and final Running",result.Status=="success"&&result.Message.Contains("Stop → Stopped, Start → Running")&&result.Message.Contains("Running (4)")&&healthy.Disposed);
        var changed=new Session(identity){ChangeAfterWait=true};result=await Run(changed,action:ActionKind.StopService);
        Check("service change after wait is partial with latest state",result.Status=="partial"&&result.Message.Contains("Running (4)"));
        var disabled=new Session(identity with{StartType=4});result=await Run(disabled);
        Check("service Disabled restart blocked before Stop",result.Status=="failed"&&disabled.Stops==0&&disabled.Starts==0);
        var store=new AppStore(Path.Combine(root,"service-history"));var mutation=new Mutations(store);
        var plan=new MutationPlan(new("simulated",ActionKind.RestartService,"Service regression",identity.Name,"","","",false,false,DateTimeOffset.UtcNow.AddMinutes(1)),new());
        result=await Run(new Session(identity){FailStart=true});mutation.RecordOutcome(plan,result);
        Check("service partial details persist unchanged in encrypted history",new AppStore(store.Root).GetHistory().Single() is {Status:"partial"} item&&item.Message==result.Message);
        plan=plan with{Fields=new(){{"name",identity.Name}}};
        result=Elevation.Unconfirmed(plan,_=>"Stopped (1)");
        Check("service unconfirmed helper remains partial and rereads native state",result.Status=="partial"&&result.Message.Contains("Stopped (1)")&&result.Message.Contains("Chưa biết lệnh"));
        result=Elevation.Unconfirmed(plan,_=>throw new IOException("simulated"));
        Check("service unconfirmed helper read failure reports unknown without retry",result.Status=="partial"&&result.Message.Contains("chưa xác định; cần tải lại"));
        return results;
    }
    sealed class Session(ServiceIdentity identity) : ServicesNative.ISession
    {
        public ServiceIdentity Initial=identity;uint state=identity.State;int readsAfterStop;
        public bool StopPending,FailStart,UnknownAfterStop,ChangeAfterWait,Disposed;public int Stops,Starts;public Action? AfterStop;
        public ServiceIdentity Identity()=>Initial;
        public uint State(){if(UnknownAfterStop&&Stops>0)throw new IOException("simulated read failure");if(ChangeAfterWait&&Stops>0&&++readsAfterStop>1)return 4;return state;}
        public void Stop(){Stops++;state=StopPending?3u:1u;AfterStop?.Invoke();}
        public void Start(){Starts++;if(FailStart)throw new System.ComponentModel.Win32Exception(5);state=4;}
        public void Dispose()=>Disposed=true;
    }
}
