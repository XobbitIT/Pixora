using CanvasForge.Core;

internal static class ProbeControlRetryChecks
{
    private static void Require(bool value){if(!value)throw new Exception("Reference retry regression");}
    private static ProbeAnalysisResult Reference(bool uncertain=false,bool missing=false)
    {
        var before=new PixelImage(96,96);for(int i=0;i<96*96;i++)before.Set(i,new(200,200,200));var after=before.Clone();
        var line=new ScreenLine(30,48,65,48);for(int x=30;x<=65;x++)after.Set(48*96+x,new(39,39,39));
        if(uncertain)after.Set(48*96+46,new(90,90,90));if(missing)after.Set(48*96+46,new(200,200,200));
        return ProbeAnalysis.Control(before,after,line,5,0);
    }
    public static void Run(Action<string,Action> test)
    {
        test("Uncertain slow reference is replaced only by an independent fresh measurement",()=>{
            int area=0;var measured=new List<int>();var failed=Reference(true);var complete=Reference();int retries=0;
            var result=new ProbeControlRetry(8).Measure(()=>area++,i=>{measured.Add(i);return i==0?failed:complete;},()=>true,_=>retries++);
            Require(result.Item==1&&result.Result==complete&&result.Result.Passed&&measured.SequenceEqual(new[]{0,1})&&retries==1);
            Require(!failed.Passed&&failed.CoreCoverage is {Covered:27,Unknown:1,Missing:0});
        });
        test("Repeated uncertainty stops after one fresh control without granting a reference",()=>{
            int area=0;var failed=Reference(true);var result=new ProbeControlRetry(8).Measure(()=>area++,_=>failed,()=>true);
            Require(area==2&&!result.Result.Passed&&result.Item==1&&result.Result==failed);
        });
        test("Scene changes missing paint and outside-only failures never trigger a retry",()=>{
            var missing=Reference(missing:true);var uncertain=Reference(true);
            foreach(var result in new[]{missing,uncertain with{Failure=ProbeFailure.SceneChanged},uncertain with{Failure=ProbeFailure.ColorMismatch},uncertain with{Failure=ProbeFailure.LowContrast}})
            {
                int areas=0;var final=new ProbeControlRetry(8).Measure(()=>areas++,_=>result,()=>true);
                Require(areas==1&&final.Result==result&&!final.Result.Passed);
            }
        });
        test("Retry budget preserves primary areas and never reuses a dirty tile",()=>{
            var retry=new ProbeControlRetry(100);int area=0;var failed=Reference(true);var used=new List<int>();
            for(int pair=0;pair<10;pair++)retry.Measure(()=>area++,i=>{used.Add(i);return failed;},()=>true);
            Require(area==18&&used.Distinct().Count()==18&&used.SequenceEqual(Enumerable.Range(0,18)));
            int calls=0;new ProbeControlRetry(0).Measure(()=>calls++,_=>failed,()=>true);Require(calls==1);
            calls=0;new ProbeControlRetry(8).Measure(()=>calls++,_=>failed,()=>false);Require(calls==1);
        });
        test("Extra clean retry tiles do not shorten the probe span or overlap envelopes",()=>{
            var tiles=SpeedCalibration.Tiles(new(467,148,1511,1192),5);
            Require(tiles.Count==SpeedCalibration.RequiredTiles+ProbeControlRetry.MaximumRetries&&TransferSchedule.Length(tiles[0].Horizontal)==31);
            foreach(var tile in tiles){Require(tile.Area.Width==100&&tile.Area.Height==100&&tile.Horizontal.X1-tile.ControlHorizontal.X2>10);}
            for(int a=0;a<tiles.Count;a++)for(int b=a+1;b<tiles.Count;b++)
                Require(tiles[a].Area.Right<=tiles[b].Area.Left||tiles[b].Area.Right<=tiles[a].Area.Left||tiles[a].Area.Bottom<=tiles[b].Area.Top||tiles[b].Area.Bottom<=tiles[a].Area.Top);
        });
    }
}
