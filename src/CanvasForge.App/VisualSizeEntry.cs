using CanvasForge.Core;

namespace CanvasForge.App;

// Visual policy never needs a clipboard lease, copy, paste or backup. This
// path is also used when a virtual-file clipboard cannot safely be preserved.
// No failure changes policy to Visual: the caller must already have selected it.
internal static class VisualSizeEntry
{
    internal sealed record Result(VisualControlEvidence Evidence,int Writes);
    internal static Result Ensure(Settings settings,double value,Func<SliderObservation?> read,
        Action<SliderObservation,string,int> enter,Action<double> wait,Action<VisualControlEvidence,int>? report=null)
    {
        if(!CalibrationReliability.Visual(settings))throw new InvalidOperationException("Visual Size entry requires the existing visual confirmation policy.");
        var payload=ControlNumber.Format("size",value);
        VisualControlEvidence Check(int writes)
        {
            var e=VisualControlVerification.Read("size",value,read,wait,CalibrationReliability.StableInterval(settings));report?.Invoke(e,writes);return e;
        }
        var observed=Check(0);if(observed.Verified)return new(observed,0);
        int retries=Math.Clamp(settings.Int("control_verify_retries",2),2,5);
        for(int attempt=0;attempt<=retries;attempt++)
        {
            if(observed.First is not{} a||observed.Second is not{} b||a.Track!=b.Track||a.ValueField!=b.ValueField||!b.ValueField.Valid)
                throw new InvalidOperationException("Не підтверджено стабільне числове поле Size. Перевір захоплення елементів редактора.");
            enter(b,payload,attempt);observed=Check(attempt+1);
            if(observed.Verified)return new(observed,attempt+1);
        }
        throw new InvalidOperationException("Не підтверджено Size після вводу клавішами. Малювання не розпочато.");
    }
    internal static void Type(string payload,Action check,Action<char,bool> key,Action<double> wait,double hold,double release)
    {
        if(payload.Length>12||ControlNumber.Parse("size",payload) is null||payload.Any(c=>!char.IsAsciiDigit(c)&&c!='.')
            ||!double.IsFinite(hold)||!double.IsFinite(release)||hold<0||release<0)
            throw new InvalidDataException("Invalid numeric Size keystrokes.");
        foreach(char c in payload)
        {
            check();
            try{key(c,false);wait(hold);}
            finally{key(c,true);}
            wait(release);
        }
    }
}
