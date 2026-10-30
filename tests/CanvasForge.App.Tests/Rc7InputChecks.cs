using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static void CheckRc7InputFailures()
    {
        int released=0,downs=0,checkedTool=0;
        var point=CursorPosition.Require(true,new(0,0),0,()=>released++);
        Assert(point==new ScreenPoint(0,0)&&released==0,"A successful (0,0) read must remain valid");
        try{CursorPosition.Require(false,new(30,30),5,()=>released++);throw new Exception("Failed API read accepted");}
        catch(CursorReadException error){Assert(error.Win32Error==5&&released==1,"API error lost or held input retained");}
        var guard=new InputCommandGuard(new(20,20,80,80),new(0,0,100,100),[new(81,10,98,40)]);
        var failed=new GuardedNativeInput.Environment(()=>throw new CursorReadException(5),_=>1,()=>1,()=>released++,up=>{if(!up)downs++;});
        try{GuardedNativeInput.Button(guard,false,InputIntent.Paint,()=>{},()=>{},failed,()=>checkedTool++);throw new Exception("Mouse down after failed cursor read");}
        catch(CursorReadException){}
        Assert(downs==0&&checkedTool==0&&released==2,"Cursor failure reached tool or paint");
        var runtime=new GuardedNativeInput.Environment(()=>new(30,30),_=>1,()=>1,()=>released++,up=>{if(!up)downs++;});
        try{GuardedNativeInput.Button(guard,false,InputIntent.Paint,()=>{},()=>{},runtime,()=>throw new OperationCanceledException("unsafe tool"));throw new Exception("Unsafe tool painted");}
        catch(OperationCanceledException){}
        Assert(downs==0&&released==3,"Tool failure left input held or painted");
        GuardedNativeInput.Button(guard,false,InputIntent.Paint,()=>{},()=>{},runtime,()=>checkedTool++);
        Assert(downs==1&&checkedTool==1,"Safe tool did not authorize valid paint");
        GuardedNativeInput.Button(guard,true,InputIntent.Paint,()=>throw new Exception(),()=>{},runtime,()=>throw new Exception());
        int reads=0,changed=0;
        var moved=new GuardedNativeInput.Environment(()=>++reads==1?new(30,30):new(31,30),_=>1,()=>1,()=>released++,up=>{if(!up)downs++;});
        GuardedNativeInput.Button(guard,false,InputIntent.Paint,()=>{},()=>throw new Exception("Wrong denial reason"),moved,()=>{},
            (before,after)=>{Assert(before==new ScreenPoint(30,30)&&after==new ScreenPoint(31,30),"Cursor trace missing");changed++;});
        Assert(changed==1&&downs==1,"Cursor moved during tool capture but still painted");
        int missingProof=0;
        GuardedNativeInput.Button(guard,false,InputIntent.Paint,()=>{},()=>missingProof++,runtime);
        Assert(missingProof==1&&downs==1,"Paint without a tool proof callback was allowed");
        Console.WriteLine("PASS rc7-cursor-api-failure-and-tool-denial-no-paint-down-unconditional-release");
    }
}
