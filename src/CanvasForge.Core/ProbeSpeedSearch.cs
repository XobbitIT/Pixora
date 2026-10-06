namespace CanvasForge.Core;

public sealed record VerifiedProbeTiming(double TestedMs,double SafeMs);

public static class ProbeSpeedSearch
{
    public static VerifiedProbeTiming? Run(Func<double,string,bool> trial)
    {
        double best=0;
        foreach(int ms in SpeedCalibration.CandidatesMs)
        {
            if(AllRepeats(ms,"candidate")){best=ms;continue;}
            // After a faster failure, validate the last successful candidate now.
            // Faster untested intervals are not claimed as the machine's limit.
            if(best>0)break;
        }
        if(best==0)return null;
        double safe=SpeedCalibration.Margin(best);
        return AllRepeats(safe,"margin")?new(best,safe):null;

        bool AllRepeats(double ms,string phase)
        {
            for(int repeat=0;repeat<SpeedCalibration.Repeats;repeat++)
                if(!trial(ms,phase))return false;
            return true;
        }
    }
}
