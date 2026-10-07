using CanvasForge.Core;
using System.Text.Json.Nodes;

internal static class ReviewRegressionChecks
{
    private static void Require(bool value){if(!value)throw new Exception("Beta.31 review regression");}
    private sealed class CopyInput:IControlReadbackInput
    {
        public double Time,Latency;public int Copies,Commits,Reads;public bool FailWrite,Cancel;
        public string? Text="3";public Func<int,string?> Result=_=>"3.00";
        public string Marker=ControlNumber.Marker;public List<int> Selections=[];
        private double copiedAt;
        public void SelectField(){}
        public void SelectField(int attempt)=>Selections.Add(attempt);
        public void SelectAll(){}
        public void WriteMarker(string marker){if(FailWrite)throw new System.ComponentModel.Win32Exception();Text=marker;}
        public void Copy(){Require(Text==Marker);Copies++;copiedAt=Time;}
        public void Wait(double seconds){if(Cancel)throw new OperationCanceledException();Time+=seconds;}
        public string? Read(){Reads++;if(Time-copiedAt+1e-9>=Latency)Text=Result(Copies);return Text;}
        public void Commit()=>Commits++;
    }
    private static PixelImage Frame()
    {var image=new PixelImage(64,64);for(int i=0;i<4096;i++)image.Set(i,new(140,140,140));return image;}
    public static void Run(Action<string,Action> test)
    {
        test("Late numeric copy is polled without rewriting its marker",()=>{
            var input=new CopyInput{Latency=.18};var result=ControlReadback.Read("size",3,input,.05);
            Require(result.Verified&&input.Copies==1&&input.Reads>1&&input.Commits==1);
        });
        test("Numeric copy timing escalates while keeping bounded attempts",()=>{
            var input=new CopyInput{Latency=.26};var result=ControlReadback.Read("size",3,input,.05);
            Require(result.Verified&&input.Copies==3&&result.Attempts==3&&input.Reads<=21);
        });
        test("Missed numeric copies never accept the old pasted payload",()=>{
            var input=new CopyInput{Latency=100};var result=ControlReadback.Read("size",3,input,.05);
            Require(!result.Verified&&result.Number is null&&input.Copies==3&&input.Reads==21);
        });
        test("Fresh wrong and malformed numbers cannot verify controls",()=>{
            foreach(string? value in new string?[]{"2.00","not a number",null})
            {var input=new CopyInput{Result=_=>value};Require(!ControlReadback.Read("size",3,input,.05).Verified&&input.Copies==3);}
            var changed=new CopyInput{Result=i=>i==1?"2":"3"};Require(ControlReadback.Read("size",3,changed,.05).Verified&&changed.Copies==2);
        });
        test("Numeric marker failure prevents copy and cancellation prevents Enter",()=>{
            var input=new CopyInput{FailWrite=true};try{ControlReadback.Read("size",3,input,.05);throw new Exception("Write failure accepted");}catch(System.ComponentModel.Win32Exception){}
            Require(input.Copies==0&&input.Commits==0);
            input=new(){Cancel=true};try{ControlReadback.Read("size",3,input,.05);throw new Exception("Cancellation ignored");}catch(OperationCanceledException){}
            Require(input.Commits==0);
        });
        test("Invalid numeric readback requests emit no input",()=>{
            var input=new CopyInput();try{ControlReadback.Read("size",0,input,.05);throw new Exception("Invalid request accepted");}catch(ArgumentException){}
            Require(input.Copies==0);
        });
        test("Numeric recovery requests a new field selection after a missed copy",()=>{
            var input=new CopyInput{Result=i=>i==1?ControlNumber.Marker:"0.01"};
            var result=ControlReadback.Read("interval",.01,input,.06);
            Require(result.Verified&&input.Selections.SequenceEqual(new[]{0,1})&&input.Commits==1);
        });
        test("A delayed HEX copy is polled without replacing its marker",()=>{
            var input=new CopyInput{Marker=HexReadback.Marker,Latency=.18,Result=_=>"#ff3333"};
            var result=new HexReadback(new(255,51,51)).Read(input,.06);
            Require(result.Verified&&result.Value=="FF3333"&&input.Copies==1&&result.Reads>1&&input.Commits==0);
        });
        test("Missing HEX copies cannot certify the pasted target and remain bounded",()=>{
            var input=new CopyInput{Marker=HexReadback.Marker,Latency=100,Text="FF3333",Result=_=>"FF3333"};
            var result=new HexReadback(new(255,51,51)).Read(input,.06);
            Require(!result.Verified&&result.Value is null&&input.Copies==3&&result.Reads==21&&input.Commits==0);
            Require(input.Selections.SequenceEqual(new[]{0,1,2}));
        });
        test("Fresh wrong HEX color requires a new copy and exact color match",()=>{
            var input=new CopyInput{Marker=HexReadback.Marker,Result=i=>i==1?"#89FF33":"#FF3333"};
            var result=new HexReadback(new(255,51,51)).Read(input,.06);
            Require(result.Verified&&input.Copies==2&&result.Value=="FF3333");
            input=new(){Marker=HexReadback.Marker,Result=_=>"#89FF33"};
            result=new HexReadback(new(255,51,51)).Read(input,.06);
            Require(!result.Verified&&result.Value=="89FF33"&&input.Copies==3);
        });
        test("HEX marker failure and cancellation stop further input",()=>{
            var input=new CopyInput{Marker=HexReadback.Marker,FailWrite=true};
            try{new HexReadback(new(255,51,51)).Read(input,.06);throw new Exception("HEX write failure ignored");}catch(System.ComponentModel.Win32Exception){}
            Require(input.Copies==0);
            input=new(){Marker=HexReadback.Marker,Cancel=true};
            try{new HexReadback(new(255,51,51)).Read(input,.06);throw new Exception("HEX cancellation ignored");}catch(OperationCanceledException){}
            Require(input.Copies==1&&input.Commits==0);
        });
        foreach(bool vertical in new[]{false,true})test($"Offset core cannot be cropped to PASS ({vertical})",()=>{
            var before=Frame();var after=before.Clone();var line=vertical?new ScreenLine(20,14,20,39):new ScreenLine(14,20,39,20);
            for(int k=14;k<=39;k++)for(int p=20;p<=22;p++)after.Set(vertical?k*64+p:p*64+k,new(0,0,0));
            var result=ProbeAnalysis.Control(before,after,line,1,1);Require(!result.Passed&&result.CoreCoverage.Expected==18*3);
            Require(result.PerpendicularOffset==0&&result.CoreCoverage.Missing>0);
        });
        test("A complete in-bounds core still passes and freezes its full trial mask",()=>{
            var before=Frame();var after=before.Clone();var line=new ScreenLine(14,20,39,20);
            for(int k=14;k<=39;k++)for(int p=19;p<=21;p++)after.Set(p*64+k,new(0,0,0));
            var result=ProbeAnalysis.Control(before,after,line,1,1);Require(result.Passed&&result.CoreCoverage.Expected==54);
            after.Set(19*64+20,before.Color(19*64+20));Require(!ProbeAnalysis.Trial(before,after,line,result).Passed);
        });
        test("A core outside the screenshot is explicitly clipped rather than shrunk",()=>{
            var before=Frame();var after=before.Clone();for(int x=14;x<=39;x++)after.Set(x,new(0,0,0));
            var result=ProbeAnalysis.Control(before,after,new(14,0,39,0),1,1);Require(!result.Passed&&result.Failure==ProbeFailure.ClippedCore);
        });
        test("Corrupt numeric settings are rejected before fallback hides them",()=>{
            foreach(JsonNode? value in new JsonNode?[]{JsonValue.Create("abc"),JsonValue.Create("NaN"),new JsonObject(),null})
            {var s=Settings.Defaults();s.Data["stroke_speed"]=value;try{s.Validate();throw new Exception("Corruption accepted");}catch(InvalidDataException){}}
            var missing=Settings.Defaults();missing.Data.Remove("stroke_speed");missing.Validate();
            var legacy=Settings.Defaults();legacy.Set("stroke_speed","0.028");legacy.Validate();Require(legacy.Number("stroke_speed")==.028);
        });
        test("Fractional integer settings cannot pass truncated validation",()=>{
            var s=Settings.Defaults();s.Set("cell_px",3.5);try{s.Validate();throw new Exception("Fraction accepted");}catch(InvalidDataException){}
        });
        test("Plan identity ignores object order and equivalent numeric representation",()=>{
            var s=Settings.Defaults();var json=new JsonObject();foreach(var p in s.Data.Reverse())json[p.Key]=p.Value?.DeepClone();
            json["cell_px"]=JsonNode.Parse("3.0");var reordered=new Settings(json);var image=Frame();
            Require(PlanIdentity.Compute(image,s,[])==PlanIdentity.Compute(image,reordered,[]));
            reordered.Set("cell_px",4);Require(PlanIdentity.Compute(image,s,[])!=PlanIdentity.Compute(image,reordered,[]));
        });
        test("Plan identity preserves semantic array order",()=>{
            var s=Settings.Defaults();s.Set("test_order",new[]{1,2});var other=s.Clone();other.Set("test_order",new[]{2,1});
            Require(PlanIdentity.Compute(Frame(),s,[])!=PlanIdentity.Compute(Frame(),other,[]));
        });
        test("Legacy floating palette coordinates are rounded with a useful validation error",()=>{
            var s=Settings.Defaults();s.SetPalette([new(new(1,2,3),new(1,2),"palette")]);s.Data["palette_click_points"]=new JsonArray(new JsonArray(45d,12.6));
            Require(s.Palette().Single().ClickPoint==new ScreenPoint(45,13));s.Data["palette_click_points"]=new JsonArray(new JsonArray("bad",12));
            try{s.Palette();throw new Exception("Invalid coordinate accepted");}catch(InvalidDataException){}
        });
        test("Gap mask arithmetic cannot wrap a large rectangle to an empty mask",()=>{
            try{CoverageAudit.GapBounds([],new(0,0,65536,65536));throw new Exception("Overflow mask accepted");}catch(ArgumentException){}
        });
    }
}
