using CanvasForge.Core;

internal static class BrushColorChecks
{
    private static void Require(bool value){if(!value)throw new Exception("Brush color guard regression");}
    private static PixelImage Frame(Rgb color)
    {var image=new PixelImage(96,96);for(int i=0;i<96*96;i++)image.Set(i,color);return image;}
    private static PixelImage Dot(PixelImage before,Rgb color)
    {var after=before.Clone();for(int y=47;y<50;y++)for(int x=47;x<50;x++)after.Set(y*96+x,color);return after;}
    public static void Run(Action<string,Action> test)
    {
        test("High contrast white artifact cannot certify requested black",()=>{
            var before=Frame(new(137,127,109));var after=Dot(before,new(226,226,226));
            Require(BrushFootprints.Contrast(before,after).PeakDelta==117);
            var check=BrushColorGuard.Inspect(before,after,new(0,0,0));Require(!check.Passed&&check.Opposed==9);
        });
        test("Color direction supports black white and non-neutral palette colors",()=>{
            foreach(var pair in new[]{(new Rgb(140,130,110),new Rgb(20,20,20),new Rgb(0,0,0)),
                (new Rgb(20,30,40),new Rgb(180,180,180),new Rgb(255,255,255)),
                (new Rgb(120,120,120),new Rgb(200,60,90),new Rgb(255,20,70))})
            {var before=Frame(pair.Item1);Require(BrushColorGuard.Inspect(before,Dot(before,pair.Item2),pair.Item3).Passed);}
        });
        test("Mixed artifacts and ambiguous changes never pass a color check",()=>{
            var before=Frame(new(140,140,140));var after=Dot(before,new(20,20,20));after.Set(48*96+48,new(230,230,230));
            var check=BrushColorGuard.Inspect(before,after,new(0,0,0));Require(!check.Passed&&check.Opposed==1&&check.Toward==8);
            var near=Frame(new(5,5,5));check=BrushColorGuard.Inspect(near,Dot(near,new(100,100,100)),new(0,0,0));
            Require(!check.Passed&&check.Ambiguous==9);
        });
        test("Three repeatable wrong-color dots create neither weak nor verified profiles",()=>{
            var s=Settings.Defaults();var batch=new BrushCalibrationBatch(s,3,[1]);var before=Frame(new(100,100,100));
            for(int i=1;i<=3;i++)batch.Record(1,i,before,before,Dot(before,new(220,220,220)),new(48,48),new(0,0,0));
            Require(batch.Profiles.Count==0&&batch.Diagnostics.Single().State==BrushSignalState.Rejected);
            Require(batch.Reference(1)==null&&batch.Rejected.Single().Reason=="Dot does not match requested color direction.");
            BrushSignalDiagnostics.Save(s,batch.Diagnostics);Require(BrushSignalDiagnostics.Read(s).Single().Samples.All(p=>p.Color is {Passed:false}));
        });
        test("Requested black retains strict contrast rejection and weak diagnostics",()=>{
            var batch=new BrushCalibrationBatch(Settings.Defaults(),3,[1]);var before=Frame(new(140,140,140));
            for(int i=1;i<=3;i++)batch.Record(1,i,before,before,Dot(before,new(70,70,70)),new(48,48),new(0,0,0));
            Require(batch.Profiles.Count==0&&batch.Diagnostics.Single().State==BrushSignalState.WeakRepeatable);
            Require(batch.Diagnostics[0].Samples.All(p=>p.Color is {Passed:true}&&p.Contrast.RequiredDelta==80));
        });
        test("Suspicious frame recaptures once without repeatedly accepting an artifact",()=>{
            var before=Frame(new(140,140,140));var wrong=Dot(before,new(240,240,240));var right=Dot(before,new(20,20,20));int calls=0;
            var capture=BrushColorGuard.Confirm(before,wrong,new(0,0,0),()=>{calls++;return right;});
            Require(calls==1&&capture.Retried&&!capture.Initial.Passed&&capture.Final.Passed&&ReferenceEquals(capture.Image,right));
            capture=BrushColorGuard.Confirm(before,wrong,new(0,0,0),()=>{calls++;return wrong;});Require(calls==2&&!capture.Final.Passed);
            capture=BrushColorGuard.Confirm(before,right,new(0,0,0),()=>throw new Exception("Unexpected recapture"));Require(!capture.Retried);
            capture=BrushColorGuard.Confirm(before,before,new(0,0,0),()=>before);Require(capture.Retried&&!capture.Final.Passed);
        });
        test("Brush recapture propagates cancellation instead of publishing partial success",()=>{
            var before=Frame(new(140,140,140));
            try{BrushColorGuard.Confirm(before,before,new(0,0,0),()=>throw new OperationCanceledException());throw new Exception("Cancellation lost");}
            catch(OperationCanceledException){}
        });
    }
}
