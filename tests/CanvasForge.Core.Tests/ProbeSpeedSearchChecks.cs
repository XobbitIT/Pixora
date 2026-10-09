using CanvasForge.Core;

internal static class ProbeSpeedSearchChecks
{
    private static void Require(bool value){if(!value)throw new Exception("Speed search regression");}
    public static void Run(Action<string,Action> test)
    {
        test("Faster failure validates the prior candidate margin before more optimization",()=>{
            var calls=new List<(double Ms,string Phase)>();
            var result=ProbeSpeedSearch.Run((ms,phase)=>{calls.Add((ms,phase));return phase=="margin"||ms>=20;});
            Require(result==new VerifiedProbeTiming(20,27));
            Require(calls.SequenceEqual(new[]{(32d,"candidate"),(32d,"candidate"),(32d,"candidate"),
                (20d,"candidate"),(20d,"candidate"),(20d,"candidate"),(12d,"candidate"),
                (27d,"margin"),(27d,"margin"),(27d,"margin")}));
        });
        test("One failed repeat prevents certification of that candidate",()=>{
            int twenties=0;var result=ProbeSpeedSearch.Run((ms,phase)=>phase=="margin"||ms==32||ms==20&&++twenties==1);
            Require(twenties==2&&result==new VerifiedProbeTiming(32,42));
        });
        test("Failed margin never produces a verified timing",()=>{
            int margins=0;var result=ProbeSpeedSearch.Run((ms,phase)=>phase=="candidate"?ms>=20:++margins<3);
            Require(result is null&&margins==3);
        });
        test("Fastest all-pass candidate still needs three margin repeats",()=>{
            var calls=new List<(double,string)>();var result=ProbeSpeedSearch.Run((ms,phase)=>{calls.Add((ms,phase));return true;});
            Require(result==new VerifiedProbeTiming(8,12)&&calls.Count==15&&calls.Count(x=>x==(12d,"margin"))==3);
        });
        test("All failed candidates produce no margin or route",()=>{
            int calls=0;Require(ProbeSpeedSearch.Run((ms,phase)=>{Require(phase=="candidate");calls++;return false;}) is null&&calls==4);
        });
        test("No successful candidate permits continuing to a later interval",()=>{
            var calls=new List<double>();var result=ProbeSpeedSearch.Run((ms,phase)=>{calls.Add(ms);return phase=="margin"||ms==20;});
            Require(result==new VerifiedProbeTiming(20,27)&&calls.SequenceEqual(new[]{32d,20,20,20,12,27,27,27}));
        });
        test("Reference failure propagates without certifying a pending margin",()=>{
            int calls=0;try{ProbeSpeedSearch.Run((ms,phase)=>{calls++;if(phase=="margin")throw new InvalidOperationException("slow reference");return ms>=20;});throw new Exception("Failure hidden");}
            catch(InvalidOperationException e){Require(e.Message=="slow reference"&&calls==8);}
        });
        test("Cancellation cannot complete remaining repeats",()=>{
            int calls=0;try{ProbeSpeedSearch.Run((ms,phase)=>{if(++calls==2)throw new OperationCanceledException();return true;});throw new Exception("Cancellation ignored");}
            catch(OperationCanceledException){Require(calls==2);}
        });
    }
}
