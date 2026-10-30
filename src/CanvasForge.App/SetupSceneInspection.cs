using CanvasForge.Core;

namespace CanvasForge.App;

internal sealed partial class MainWindow
{
    internal Func<Task<EditorEvidenceResult>>? SetupSceneInspector {get;set;}
    internal Func<SetupCaptureScope,Task>? SetupCaptureAction {get;set;}
    private async Task EnsureSetupScene()
    {
        var result=await SetupSceneFlow.Ensure(
            ()=>SetupSequence.CaptureReady(settings)&&EditorEvidence.Current(settings),
            ()=>SetupSceneInspector?.Invoke()??InspectSetupScene(),
            scope=>SetupCaptureAction?.Invoke(scope)??(scope==SetupCaptureScope.Canvas
                ?Capture("canvas",T("ПОЛОТНО","CANVAS")):CaptureWizard()),SetupToken);
        SessionLog.Write(LogPath,"setup_scene_verified",new{scope=result.Scope.ToString(),result.PreviousReason,
            result.Evidence.Reason,invasiveInput=false,referenceChanged=result.Scope!=SetupCaptureScope.None});
    }
    private async Task<EditorEvidenceResult> InspectSetupScene()
    {
        try
        {
            Hide();await Task.Delay(150,SetupToken);
            nint target;
            try{target=AlignRustForTest();}
            catch(InvalidOperationException e) when(e.Message is CalibrationSession.DpiChangedMessage or CalibrationSession.SizeChangedMessage)
            {return new(EditorEvidenceState.Missing,"editor_geometry_changed",["geometry"]);}
            Native.SetForegroundWindow(target);await Task.Delay(200,SetupToken);
            var origin=Native.ClientOrigin(target);var client=Native.ClientSize(target);int dpi=Native.DpiOf(target);uint pid=Native.ProcessIdOf(target);
            if(!Native.GetWindowRect(target,out var initial))throw new PreparationInterruptedException();
            void Check()
            {
                SetupToken.ThrowIfCancellationRequested();
                if(!Native.IsRust(target)||Native.GetForegroundWindow()!=target||Native.ProcessIdOf(target)!=pid)
                    throw new PreparationInterruptedException();
                if(Native.ClientOrigin(target)!=origin||Native.ClientSize(target)!=client||Native.DpiOf(target)!=dpi
                    ||!Native.GetWindowRect(target,out var current)||current.ToScreen()!=initial.ToScreen())
                    throw new PreparationInterruptedException(CalibrationSession.SizeChangedMessage);
            }
            EditorEvidenceResult Read()
            {
                Check();var frame=Native.Screenshot(initial.ToScreen());Check();
                return EditorEvidence.Read(settings,EditorEvidence.Reference(settings),frame,initial.ToScreen(),origin,dpi,client);
            }
            var first=Read();if(first.State!=EditorEvidenceState.Present)return first;
            await Task.Delay(120,SetupToken);return Read();
        }
        finally{if(!closing){Show();Activate();}}
    }
}
