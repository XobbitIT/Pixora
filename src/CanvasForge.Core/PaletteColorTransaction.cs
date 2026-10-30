namespace CanvasForge.Core;

public enum PaletteColorProof { ActiveVerified, CellOnly, Unavailable }
public static class PaletteColorTransaction
{
    // Shared production path: the cell is proved before any click. Recovery can
    // select a contrast color, so restore and re-prove the target on advisory exit.
    public static PaletteColorProof Select(Action verifyCell,Action<int> selectTarget,
        Func<bool?> activeProof,Func<bool> recover,bool requireAppliedProof)
    {
        verifyCell();
        for(int attempt=0;attempt<3;attempt++)
        {
            selectTarget(attempt);var proof=activeProof();
            if(proof==true)return PaletteColorProof.ActiveVerified;
            if(proof is null)break;
        }
        bool recovered;
        try{recovered=recover();}
        catch(Exception e) when(!requireAppliedProof&&e is (PaletteTargetException or PaletteLayoutException)){recovered=false;}
        if(recovered)return PaletteColorProof.ActiveVerified;
        if(requireAppliedProof)return PaletteColorProof.Unavailable;
        verifyCell();selectTarget(0);return PaletteColorProof.CellOnly;
    }
}
