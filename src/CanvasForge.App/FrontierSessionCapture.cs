using System.Text.Json;
using System.Text.Json.Nodes;
using CanvasForge.Core;

namespace CanvasForge.App;

// Explicit preparation capture, before Painter is constructed. No runtime
// mismatch handler calls this class. Coordinates/settings files stay frozen.
internal static class FrontierSessionCapture
{
    internal const string Revision="frontier-explicit-editor-capture-v1";
    internal const string FileName="session-editor.json",FrameName="session-editor.png",Key="frontier_editor_session_id";
    internal sealed record Record(string Revision,string PairId,string SettingsSha256,string AppSha256,string CoreSha256,
        uint RustPid,ScreenRect Screen,ScreenPoint Origin,int Dpi,ScreenSize Client,string FrameSha256,
        EditorReference Reference,bool UserConfirmed,bool Synthetic);
    // The capture proof depends on an immutable experiment/context binding,
    // not on the frontier grid. Keep the historical Pair API as wrappers.
    internal sealed record Binding(string PairId,string SettingsSha256,string AppSha256,string CoreSha256,
        StandardGeometryContext Context,bool SyntheticSource)
    {
        internal static Binding From(StandardDiscreteBandCli.Pair p)=>new(p.PairId,p.SettingsSha256,p.AppSha256,p.CoreSha256,p.Context,p.SyntheticSource);
    }

    // Use the same predicate before paint and during the final evidence check.
    // A clean reservation cannot excuse an altered pixel elsewhere in Canvas.
    internal static void RequireBaseline(PixelImage confirmed,PixelImage baseline,ScreenRect canvas)
    {
        if(!EvidenceRegionCheck.Same(confirmed,baseline,canvas,canvas))
            throw new InvalidDataException("Початкове полотно не збігається з підтвердженим чистим кадром цієї сесії. Вимірювання не дозволено.");
    }
    internal static void ParkCursor(ScreenPoint park,Action release,Action<ScreenPoint> move,
        Func<ScreenPoint> cursor,Func<bool> contextSafe)
    {
        if(!contextSafe())throw new PreparationInterruptedException("Редактор або стан вводу змінився перед знімком.");
        release();
        try
        {
            move(park);
            if(!contextSafe()||cursor()!=park)
                throw new PreparationInterruptedException("Не вдалося підтвердити відведення курсора перед знімком.");
        }
        catch {try{release();}catch{}throw;}
    }

    internal static EditorReference Prepare(Settings source,PixelImage frame,ScreenRect screen,ScreenPoint origin,int dpi,ScreenSize client,ScreenRect frozenCanvas)
    {
        var original=EditorEvidence.Reference(source)??throw new InvalidDataException("Немає початкових координат редактора.");
        var cal=source.PaintCalibration();
        if(cal.Rect("canvas")!=frozenCanvas||cal.SessionClient!=origin||cal.SessionDpi!=dpi||cal.SessionSize!=client
            ||original.Canvas!=new ScreenRect(frozenCanvas.Left-origin.X,frozenCanvas.Top-origin.Y,frozenCanvas.Right-origin.X,frozenCanvas.Bottom-origin.Y))
            throw new InvalidDataException("Геометрія або DPI змінилися. Новий еталон не пересуває координати вимірювання.");
        // This is a NEW user-confirmed capture. Only frame texture is renewed;
        // controls/mode/tool must still match their existing verified locators.
        var structure=CanvasStructure.CaptureForEditor(frozenCanvas,frame,screen,origin,dpi,client,cal);
        var candidate=original with{Structure=structure};
        var proof=EditorEvidence.Read(source,candidate,frame,screen,origin,dpi,client);
        var tool=BrushToolState.Read(original.Tool,frame,screen,cal.Rect("brush_shapes"),origin);
        if(proof.State!=EditorEvidenceState.Present||!tool.Safe)
            throw new InvalidDataException("Не вдалося підтвердити елементи редактора та активний пензель ("+FailureDetail(proof,tool)+"). Закрий накладки й відкрий той самий редактор.");
        var quiet=PaletteCursorParking.Find(source,screen,origin,dpi,new(screen.Left+16,screen.Top+16),candidate)
            ??throw new InvalidDataException("Немає безпечної точки паркування для нового еталона.");
        return candidate with{QuietParkingPoint=new(quiet.X-origin.X,quiet.Y-origin.Y)};
    }
    // Names which check failed (editor frame or brush row) so an overlay or a changed tool can be told apart.
    internal static string FailureDetail(EditorEvidenceResult proof,BrushToolResult tool)
    {
        string frame=proof.State==EditorEvidenceState.Present?"рамка: OK":"рамка: "+proof.State+"/"+proof.Reason+(proof.FailedGroups.Count>0?" ["+string.Join(",",proof.FailedGroups)+"]":"");
        return frame+"; пензель: "+(tool.Safe?"OK":tool.Reason);
    }
    internal static Settings Apply(Settings source,EditorReference reference,string id,bool confirmed)
    {
        if(!confirmed)throw new OperationCanceledException("Захоплення не підтверджено. Малювання не починалося.");
        var result=source.Clone();CanvasStructure.Store(result,reference.Structure??throw new InvalidDataException("Missing frame proof"));
        EditorEvidence.Store(result,reference);result.Set(Key,id);
        if(!EditorEvidence.Current(result))throw new InvalidDataException("Новий еталон не відповідає замороженим координатам.");
        return result;
    }
    // Validate the immutable reference on a second, independent frame. Never
    // learn from a mismatch or silently choose different patches after capture.
    internal static void ConfirmStable(Settings source,EditorReference reference,PixelImage captured,PixelImage observed,
        ScreenRect screen,ScreenPoint origin,int dpi,ScreenSize client)
    {
        var settings=Apply(source,reference,"pre-publication",true);
        var proof=EditorEvidence.Read(settings,reference,observed,screen,origin,dpi,client);
        if(proof.State!=EditorEvidenceState.Present)
            throw new PreparationInterruptedException("Рамку перекрито або змінено після захоплення: "+proof.Reason+". Ввід не розпочато.");
        if(!BrushToolState.Read(reference.Tool,observed,screen,settings.PaintCalibration().Rect("brush_shapes"),origin).Safe)
            throw new PreparationInterruptedException("Пензель змінився під час підготовки. Ввід не розпочато.");
        var r=settings.PaintCalibration().Rect("canvas");
        var local=new ScreenRect(r.Left-screen.Left,r.Top-screen.Top,r.Right-screen.Left,r.Bottom-screen.Top);
        if(!EvidenceRegionCheck.Same(captured,observed,new(0,0,screen.Width,screen.Height),local))
            throw new PreparationInterruptedException("Полотно змінилося під час підготовки. Ввід не розпочато.");
    }
    internal static string Save(string ascending,StandardDiscreteBandCli.Pair pair,PixelImage frame,ScreenRect screen,ScreenPoint origin,int dpi,ScreenSize client,uint pid,EditorReference reference,bool confirmed,bool synthetic=false)
        =>Save(ascending,Binding.From(pair),frame,screen,origin,dpi,client,pid,reference,confirmed,synthetic);
    internal static string Save(string ascending,Binding pair,PixelImage frame,ScreenRect screen,ScreenPoint origin,int dpi,ScreenSize client,uint pid,EditorReference reference,bool confirmed,bool synthetic=false)
    {
        if(!confirmed)throw new OperationCanceledException("Захоплення скасовано до публікації еталона.");
        if(pid==0)throw new InvalidDataException("Не визначено процес Rust для еталона.");
        string path=Path.Combine(ascending,FileName),image=Path.Combine(ascending,FrameName);
        if(File.Exists(path)||File.Exists(image)||File.Exists(path+".sha256"))throw new IOException("Еталон цієї спроби вже існує; перезапис заборонено.");
        string rgba=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(frame.Rgba));
        if(reference.Structure?.FrameSha256!=rgba||!CanvasBounds.Current(reference.Structure.Bounds,reference.Canvas,rgba))throw new InvalidDataException("Еталон і підтверджений прямокутник походять із різних кадрів.");
        var record=new Record(Revision,pair.PairId,pair.SettingsSha256,pair.AppSha256,pair.CoreSha256,pid,screen,origin,dpi,client,rgba,reference,true,synthetic);
        Images.Save(frame,image);
        using(var file=new FileStream(path,FileMode.CreateNew))JsonSerializer.Serialize(file,record);
        string hash=StandardBoundaryReview.Hash(path);File.WriteAllText(path+".sha256",hash);return hash;
    }
    internal static (Record Record,string Id) Read(string ascending,StandardDiscreteBandCli.Pair pair)
        =>Read(ascending,Binding.From(pair));
    internal static (Record Record,string Id) Read(string ascending,Binding pair)
    {
        string path=Path.Combine(ascending,FileName),id=StandardBoundaryReview.Hash(path);
        if(id!=File.ReadAllText(path+".sha256").Trim())throw new InvalidDataException("Еталон сесії пошкоджений.");
        var r=JsonSerializer.Deserialize<Record>(File.ReadAllText(path))??throw new InvalidDataException("Немає еталона сесії.");
        if(r.Revision!=Revision||!r.UserConfirmed||r.RustPid==0||r.PairId!=pair.PairId||r.SettingsSha256!=pair.SettingsSha256||r.AppSha256!=pair.AppSha256||r.CoreSha256!=pair.CoreSha256
            ||r.Synthetic!=pair.SyntheticSource||r.Origin!=pair.Context.ClientOrigin||r.Dpi!=pair.Context.Dpi||r.Client!=pair.Context.ClientSize
            ||r.Reference.Canvas!=new ScreenRect(pair.Context.Canvas.Left-r.Origin.X,pair.Context.Canvas.Top-r.Origin.Y,pair.Context.Canvas.Right-r.Origin.X,pair.Context.Canvas.Bottom-r.Origin.Y))
            throw new InvalidDataException("Еталон належить іншій парі, збірці або геометрії.");
        var frame=Images.LoadNative(Path.Combine(ascending,FrameName));
        if(frame.Width!=r.Screen.Width||frame.Height!=r.Screen.Height||Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(frame.Rgba))!=r.FrameSha256
            ||r.Reference.Structure?.FrameSha256!=r.FrameSha256)throw new InvalidDataException("Кадр еталона сесії пошкоджений.");
        var source=Settings.Load(Path.Combine(ascending,"settings.json"));var original=EditorEvidence.Reference(source)??throw new InvalidDataException("Missing frozen controls");
        if(JsonSerializer.Serialize(original with{Structure=r.Reference.Structure,QuietParkingPoint=r.Reference.QuietParkingPoint})!=JsonSerializer.Serialize(r.Reference))
            throw new InvalidDataException("Захоплення змінило зафіксовані елементи керування.");
        if(pair.Context.Canvas.Left<r.Screen.Left||pair.Context.Canvas.Top<r.Screen.Top||pair.Context.Canvas.Right>r.Screen.Right||pair.Context.Canvas.Bottom>r.Screen.Bottom)
            throw new InvalidDataException("Еталон не охоплює зафіксоване полотно.");
        var candidate=Apply(source,r.Reference,id,true);
        if(EditorEvidence.Read(candidate,r.Reference,frame,r.Screen,r.Origin,r.Dpi,r.Client).State!=EditorEvidenceState.Present
            ||!BrushToolState.Read(r.Reference.Tool,frame,r.Screen,candidate.PaintCalibration().Rect("brush_shapes"),r.Origin).Safe)
            throw new InvalidDataException("Збережений кадр не підтверджує еталон сесії.");
        return(r,id);
    }
    internal static void VerifyUse(string directory,StandardDiscreteBandCli.Pair pair)
    {
        string ascending=Path.Combine(Path.GetDirectoryName(directory)!,"ascending");
        VerifyUse(directory,ascending,Binding.From(pair));
    }
    internal static void VerifyUse(string directory,string ascending,Binding pair)
    {
        using var live=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"live-context.json")));
        var env=live.RootElement;bool hasSettings=env.TryGetProperty("settings",out var data);JsonElement key=default;
        bool claimed=hasSettings&&data.TryGetProperty(Key,out key)&&key.ValueKind==JsonValueKind.String&&!string.IsNullOrEmpty(key.GetString());
        if(!claimed&&!File.Exists(Path.Combine(ascending,FileName)))return; // Historical v1 evidence remains readable.
        var (record,id)=Read(ascending,pair);
        if(!claimed||key.GetString()!=id||env.GetProperty("windowProcessId").GetUInt32()!=record.RustPid)throw new InvalidDataException("Живий запуск не використовував підтверджений еталон цієї сесії.");
        var expected=Apply(Settings.Load(Path.Combine(directory,"settings.json")),record.Reference,id,true);
        foreach(string k in new[]{EditorEvidence.Key,"editor_references","canvas_structure"})
            if(!data.TryGetProperty(k,out var actual)||!JsonNode.DeepEquals(expected.Data[k],JsonNode.Parse(actual.GetRawText())))throw new InvalidDataException("Еталон запуску відрізняється від підтвердженого.");
        var baseline=StandardStraightPilotCli.Frame(Path.Combine(directory,"baseline.png"),pair.Context.Canvas,pair.SyntheticSource);
        RequireBaseline(Baseline(ascending,pair),baseline,pair.Context.Canvas);
    }
    internal static PixelImage Baseline(string ascending,StandardDiscreteBandCli.Pair pair)
        =>Baseline(ascending,Binding.From(pair));
    internal static PixelImage Baseline(string ascending,Binding pair)
    {
        var (record,_)=Read(ascending,pair);var frame=Images.LoadNative(Path.Combine(ascending,FrameName));var r=pair.Context.Canvas;
        return ImageProcessing.Crop(frame,r.Left-record.Screen.Left,r.Top-record.Screen.Top,r.Right-record.Screen.Left,r.Bottom-record.Screen.Top);
    }
    internal static Settings CaptureOrLoad(Settings source,IntPtr window,StandardDiscreteBandCli.Pair pair,StandardDiscreteBandCli.Condition condition,string directory)
    {
        if(Native.OfflineTestHost)throw new InvalidOperationException("Native capture is forbidden in offline tests.");
        uint pid=Native.ProcessIdOf(window);var origin=Native.ClientOrigin(window);int dpi=Native.DpiOf(window);var client=Native.ClientSize(window);
        if(origin!=pair.Context.ClientOrigin||dpi!=pair.Context.Dpi||client!=pair.Context.ClientSize)throw new InvalidDataException("Змінилися розмір, положення або DPI редактора.");
        if(condition.Direction=="descending")
        {
            var (saved,id)=Read(Path.Combine(Path.GetDirectoryName(directory)!,"ascending"),pair);
            if(saved.RustPid!=pid)throw new InvalidDataException("Rust перезапущено між напрямками.");
            return Apply(source,saved.Reference,id,true);
        }
        return CaptureFresh(source,window,Binding.From(pair),directory);
    }
    internal static Settings CaptureFresh(Settings source,IntPtr window,Binding pair,string directory)
    {
        if(Native.OfflineTestHost)throw new InvalidOperationException("Native capture is forbidden in offline tests.");
        uint pid=Native.ProcessIdOf(window);var origin=Native.ClientOrigin(window);int dpi=Native.DpiOf(window);var client=Native.ClientSize(window);
        if(origin!=pair.Context.ClientOrigin||dpi!=pair.Context.Dpi||client!=pair.Context.ClientSize)
            throw new InvalidDataException("Змінилися розмір, положення або DPI редактора.");
        if(!Native.GetWindowRect(window,out var bounds)||Native.GetForegroundWindow()!=window)throw new PreparationInterruptedException("Повернись у Rust перед захопленням еталона.");
        var screen=bounds.ToScreen();
        var park=PaletteCursorParking.Find(source,screen,origin,dpi,Native.Cursor())
            ??throw new PreparationInterruptedException("Немає безпечної точки для знімка без курсора.");
        bool Safe()=>Native.GetForegroundWindow()==window&&Native.WindowAt(park)==window&&Native.ProcessIdOf(window)==pid
            &&Native.ClientOrigin(window)==origin&&Native.ClientSize(window)==client&&Native.DpiOf(window)==dpi
            &&!new[]{0x1B,0x0D,0x01,0x10,0x11,0x12}.Any(Native.Down);
        // Match the normal screenshot path: released SendInput movement and a
        // real GetCursorPos confirmation, not an unchecked SetCursorPos warp.
        ParkCursor(park,Native.ReleaseChecked,p=>{Native.MovePath([p]);Thread.Sleep(200);},Native.Cursor,Safe);
        var frame=Native.Screenshot(screen);
        if(Native.GetForegroundWindow()!=window)throw new PreparationInterruptedException("Фокус змінився під час захоплення. Спроба зупинена.");
        var reference=Prepare(source,frame,screen,origin,dpi,client,pair.Context.Canvas);
        // run-session is an explicit preparation command, launched only after
        // the console's clean-Canvas confirmation and countdown. Do not open a
        // topmost preview between the frozen capture and its first use: that
        // changes foreground/rendering and can place an overlay over the ROI.
        Thread.Sleep(200);
        if(Native.GetForegroundWindow()!=window||!Native.GetWindowRect(window,out var current)||current.ToScreen()!=screen
            ||Native.ProcessIdOf(window)!=pid||Native.ClientOrigin(window)!=origin||Native.DpiOf(window)!=dpi||Native.ClientSize(window)!=client)
            throw new PreparationInterruptedException("Редактор змінився під час захоплення. Ввід не розпочато.");
        if(Native.Down(0x1B)||Native.Down(0x0D)||Native.Down(0x01)||Native.Down(0x10)||Native.Down(0x11)||Native.Down(0x12))
            throw new PreparationInterruptedException("Відпусти клавіші й кнопки миші перед вимірюванням.");
        var confirmation=Native.Screenshot(screen);
        if(Native.GetForegroundWindow()!=window)throw new PreparationInterruptedException("Фокус змінився під час перевірки кадру.");
        ConfirmStable(source,reference,frame,confirmation,screen,origin,dpi,client);
        string identity=Save(directory,pair,frame,screen,origin,dpi,client,pid,reference,true);
        return Apply(source,reference,identity,true);
    }
}
