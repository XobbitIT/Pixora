using CanvasForge.Core;

internal static class StandardPaintMaskChecks
{
    private static void Need(bool b){if(!b)throw new Exception("Strict raw mask regression");}
    private static PixelImage Blank(int w=12,int h=12,Rgb? color=null)
    {var p=new PixelImage(w,h);for(int i=0;i<w*h;i++)p.Set(i,color??Rgb.White);return p;}
    private static void Refuse(Action a)
    {try{a();}catch(InvalidDataException){return;}throw new Exception("Invalid mask accepted");}
    private static StandardPaintMaskResult Measure(PixelImage before,PixelImage after,ScreenLine? axis=null,Rgb? color=null)
        =>StandardPaintMask.Measure(before,after,new(-20,30,-8,42),new(-19,31,-9,41),axis??new(-15,32,-15,39),color??new(0,0,0));
    public static void Run(Action<string,Action> test)
    {
        test("Raw mask preserves requested black in full H/V rows and both directions with physical pixel edges",()=>{
            foreach(bool vertical in new[]{true,false})foreach(bool reverse in new[]{true,false})
            {
                var before=Blank();var after=before.Clone();
                for(int y=2;y<=9;y++)for(int x=4;x<=6;x++)after.Set((vertical?y:x)*12+(vertical?x:y),new(0,0,0));
                var axis=vertical?new ScreenLine(-15,32,-15,39):new ScreenLine(-18,35,-11,35);
                if(reverse)axis=new(axis.X2,axis.Y2,axis.X1,axis.Y1);
                var m=Measure(before,after,axis);Need(m.Complete&&m.CompleteRows==8&&m.MatchingPixels==24);
                Need(m.Rows.All(r=>r.FirstPixel==(vertical?-16:34)&&r.EndPixelExclusive==(vertical?-13:37)));
                Need(m.Rows.All(r=>r.Edges!.Low==(vertical?-16.5:33.5)&&r.Edges.High==(vertical?-13.5:36.5)));
                Need(!m.PhysicalQualityCertified&&!m.ProductionEligible);
            }
        });
        test("Raw mask accepts RGB distance12 and rejects13 without learning a different paint color",()=>{
            var before=Blank();var after=before.Clone();
            for(int y=2;y<=9;y++)after.Set(y*12+5,new(52,40,40));
            Need(Measure(before,after,color:new(40,40,40)).Complete);
            after.Set(5*12+5,new(53,40,40));var m=Measure(before,after,color:new(40,40,40));
            Need(!m.Complete&&m.MissingRows==1&&m.StrongNonMatchingPixels==1&&m.Requested==new Rgb(40,40,40));
        });
        test("Raw contrast is strictly above80 and unchanged matching background is never paint evidence",()=>{
            var before=Blank(color:new(120,0,0));var after=before.Clone();
            for(int y=2;y<=9;y++)after.Set(y*12+5,new(40,0,0));
            Need(Measure(before,after,color:new(40,0,0)).MissingRows==8);
            before=Blank(color:new(121,0,0));Need(Measure(before,after,color:new(40,0,0)).Complete);
            var black=Blank(color:new(0,0,0));Need(Measure(black,black).MatchingPixels==0);
        });
        test("Disconnected requested-color runs retain holes instead of bridging them into an edge",()=>{
            var before=Blank();var after=before.Clone();
            for(int y=2;y<=9;y++){after.Set(y*12+4,new(0,0,0));after.Set(y*12+6,new(0,0,0));}
            var m=Measure(before,after);Need(m.DisconnectedRows==8&&m.CompleteRows==0&&m.Spans.Count==16);
            Need(m.Rows.All(r=>r.Reason=="disconnected_requested_color_mask"&&r.Edges is null&&r.FirstPixel is null));
        });
        test("Missing last mandatory row remains visible and no complete mask is inferred",()=>{
            var before=Blank();var after=before.Clone();for(int y=2;y<9;y++)after.Set(y*12+5,new(0,0,0));
            var m=Measure(before,after);Need(m.Rows.Count==8&&m.MissingRows==1&&m.Rows[^1].Along==39&&m.Rows[^1].Edges is null);
        });
        test("A requested-color run touching either search border is clipped rather than a measured full edge",()=>{
            foreach(int x in new[]{1,10})
            {
                var before=Blank();var after=before.Clone();for(int y=2;y<=9;y++)after.Set(y*12+x,new(0,0,0));
                var m=Measure(before,after);Need(m.ClippedRows==8&&m.Rows.All(r=>r.Edges is null));
            }
        });
        test("Raw mask never replaces matching RGB with contrast-only or a median of the observed stroke",()=>{
            var before=Blank();var after=before.Clone();for(int y=2;y<=9;y++)after.Set(y*12+5,new(30,30,30));
            var m=Measure(before,after);Need(m.MatchingPixels==0&&m.StrongNonMatchingPixels==8&&m.MissingRows==8);
        });
        test("Opaque native mask rejects alpha evidence instead of treating transparent black as paint",()=>{
            var before=Blank();var after=before.Clone();after.Set(5*12+5,new(0,0,0),0);Refuse(()=>Measure(before,after));
            after=before.Clone();before.Set(5*12+5,Rgb.White,128);Refuse(()=>Measure(before,after));
        });
        test("Mask geometry rejects wrong sizes diagonal zero-length outside axes and invalid rectangles",()=>{
            var a=Blank();var b=Blank();Refuse(()=>Measure(a,Blank(11,12)));
            foreach(var axis in new[]{new ScreenLine(-18,32,-10,39),new(-15,32,-15,32),new(-30,32,-30,39),new(-15,32,-15,41)})
                Refuse(()=>Measure(a,b,axis));
            Refuse(()=>StandardPaintMask.Measure(a,b,new(-20,30,-8,42),new(-21,31,-9,41),new(-15,32,-15,39),new(0,0,0)));
            Refuse(()=>StandardPaintMask.Measure(a,b,new(int.MinValue,0,int.MaxValue,12),new(0,0,10,10),new(1,1,1,8),new(0,0,0)));
        });
        test("Mask observations are immutable snapshots and sparse spans preserve every selected pixel",()=>{
            var a=Blank();var b=a.Clone();for(int y=2;y<=9;y++)b.Set(y*12+5,new(0,0,0));
            var m=Measure(a,b);b.Set(5*12+5,Rgb.White);Need(m.Complete&&m.MatchingPixels==8);
            Need(m.Spans.Sum(s=>s.Right-s.Left)==m.MatchingPixels);
            Need(typeof(StandardPaintMaskResult).GetConstructors().Length==0&&typeof(StandardPaintMaskResult).GetProperties().All(p=>p.SetMethod is null));
        });
    }
}
