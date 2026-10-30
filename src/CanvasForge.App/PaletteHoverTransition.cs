using CanvasForge.Core;

namespace CanvasForge.App;

// Only the program's short palette click/hover transition may defer passive
// screenshots. Focus, cancellation, window geometry and input guards still run.
// No paint or second UI transaction is allowed until a fresh editor proof.
internal sealed class PaletteHoverTransition(Func<double> seconds)
{
    internal sealed class Expired():PreparationInterruptedException("Palette selection timed out. Return to Rust and repeat the color check.");
    internal const double MaximumSeconds=2,SettleSeconds=.10;
    internal bool Active {get;private set;}
    private double started;
    internal void CheckDeadline()
    {
        if(Active&&seconds()-started>=MaximumSeconds)
            throw new Expired();
    }
    internal void Select(Action prove,Action click,Action park,Action<double> wait,Func<bool> cleanFrame,Action unavailable,Action release)
    {
        if(Active)throw new InvalidOperationException("Nested palette transition.");
        try
        {
            prove();release();started=seconds();Active=true;
            click();CheckDeadline();release();park();CheckDeadline();
            int clean=0;
            for(int sample=0;sample<6&&clean<2;sample++)
            {
                wait(SettleSeconds);CheckDeadline();
                clean=cleanFrame()?clean+1:0;CheckDeadline();
            }
            Active=false;
            if(clean<2){unavailable();throw new PreparationInterruptedException("Palette editor proof unavailable.");}
        }
        finally{Active=false;release();}
    }
}
