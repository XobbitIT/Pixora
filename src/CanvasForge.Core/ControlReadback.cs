namespace CanvasForge.Core;

// The adapter owns field selection and keyboard/clipboard transport. This seam
// makes late copies, stale payloads and interruption testable without a game.
public interface IControlReadbackInput
{
    void SelectField();
    void SelectField(int attempt)=>SelectField();
    void SelectAll();
    void WriteMarker(string marker);
    void Copy();
    void Wait(double seconds);
    string? Read();
    void Commit();
}
public sealed record ControlReadbackObservation(int Attempt,int Poll,string? Raw,double? Number,string Status);
public sealed record ControlReadbackResult(double? Number,string? Raw,int Attempts,int Reads,bool Verified);

public static class ControlReadback
{
    public static ControlReadbackResult Read(string kind,double expected,IControlReadbackInput input,double copyDelay,
        Action<ControlReadbackObservation>? observe=null)
    {
        if(!ControlNumber.InRange(kind,expected)||!double.IsFinite(copyDelay)||copyDelay is <0 or >1)
            throw new ArgumentException("Invalid control readback request.");
        double? last=null;string? raw=null;int reads=0;
        for(int attempt=0;attempt<3;attempt++)
        {
            input.SelectField(attempt);input.SelectAll();input.WriteMarker(ControlNumber.Marker);input.Copy();
            input.Wait(copyDelay+attempt*.05);
            for(int poll=0;poll<=6;poll++)
            {
                if(poll>0)input.Wait(.025);
                raw=input.Read();reads++;var number=ControlNumber.Parse(kind,raw);
                string status=raw==ControlNumber.Marker?"copy_pending":raw is null?"no_text":number is null?"invalid_text":
                    ControlNumber.Matches(number,expected)?"match":"mismatch";
                observe?.Invoke(new(attempt,poll,raw,number,status));
                if(number is null)continue;
                last=number;
                if(ControlNumber.Matches(number,expected))
                {input.Commit();return new(number,raw,attempt+1,reads,true);}
                // A copied old field value needs another Ctrl+C, not repeated
                // acceptance of an unchanged clipboard payload.
                break;
            }
        }
        input.Commit();return new(last,raw,3,reads,false);
    }
}
