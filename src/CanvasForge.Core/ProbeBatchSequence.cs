namespace CanvasForge.Core;

public sealed record ProbeSizeChoices(double[] Requested,double[] Ready,double[] Unavailable);
public enum ProbeBatchState { Spatial,Speed,Passed,Failed,Cancelled }
public sealed record ProbeBatchResult(int Passed,int Failed,bool Cancelled);

public static class ProbeBatchSequence
{
    public const string Measured="Measured";
    public static string[] Options=>new[]{Measured,SetupBrushSelection.Combined,"3/10/20","3/10","20/30/40"}.Concat(SetupBrushSelection.SizeOptions).Distinct().ToArray();
    public static ProbeSizeChoices Select(Settings settings,string choice)
    {
        var requested=choice==Measured?BrushFootprints.Read(settings).Where(p=>p.SolidCore.Valid).Select(p=>p.Size).Distinct().Order().ToArray()
            :SetupBrushSelection.Sizes(choice,false).Order().ToArray();
        var ready=requested.Where(size=>SpeedCalibration.BrushReady(settings,size)).ToArray();
        return new(requested,ready,requested.Except(ready).ToArray());
    }
    // A new clean surface is required before each independent protocol. No
    // footprint, spatial, candidate, margin or coverage criterion is shortened.
    public static async Task<ProbeBatchResult> Run(IReadOnlyList<double> sizes,
        Func<double,bool,CancellationToken,Task<bool>> clean,
        Func<double,bool> spatialCurrent,
        Func<double,bool,CancellationToken,Task<bool>> execute,
        Action<double,ProbeBatchState,string> report,CancellationToken token)
    {
        if(sizes.Count==0||sizes.Any(size=>!BrushFootprints.Sizes.Contains(size))||sizes.Distinct().Count()!=sizes.Count)
            throw new ArgumentException("Invalid probe Sizes.");
        int passed=0,failed=0;
        foreach(double size in sizes)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                bool spatial=!spatialCurrent(size);
                if(!await clean(size,spatial,token)){report(size,ProbeBatchState.Cancelled,"");return new(passed,failed,true);}
                token.ThrowIfCancellationRequested();
                if(spatial)
                {
                    report(size,ProbeBatchState.Spatial,"");
                    if(!await execute(size,true,token))throw new InvalidOperationException("Spatial offsets were not verified.");
                    token.ThrowIfCancellationRequested();
                    if(!await clean(size,false,token)){report(size,ProbeBatchState.Cancelled,"");return new(passed,failed,true);}
                    token.ThrowIfCancellationRequested();
                }
                report(size,ProbeBatchState.Speed,"");
                if(!await execute(size,false,token))throw new InvalidOperationException("No fast route passed verification.");
                token.ThrowIfCancellationRequested();passed++;report(size,ProbeBatchState.Passed,"");
            }
            catch(OperationCanceledException){report(size,ProbeBatchState.Cancelled,"");throw;}
            catch(Exception e){failed++;report(size,ProbeBatchState.Failed,e.Message);}
        }
        return new(passed,failed,false);
    }
}
