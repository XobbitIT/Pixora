using System.Security.Cryptography;
using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;

// Explicit opt-in, pinned to the bytes reviewed by the user. No fitting or settings migration.
internal static class TexelProductSession
{
    internal const string Revision="texel-product-session-start-v1";
    internal const string PathKey="texel_session_binding_path",ShaKey="texel_session_binding_sha256";
    internal sealed record Admission(TexelSessionBinding Binding,string RecordSha256,byte[] Record,TexelGrid Grid)
    {
        internal string Identity=>Revision+":"+TexelProduct.ProfileSha256+":"+Binding.Id+":"+RecordSha256;
    }
    internal static bool Requested(Settings s)=>!string.IsNullOrEmpty(s.Text(PathKey))||!string.IsNullOrEmpty(s.Text(ShaKey));
    internal static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static void Require(bool ok,string field)
    {if(!ok)throw new InvalidDataException("Session binding refused: "+field+".");}
    internal static Admission? Resolve(Settings settings,byte[]? archived=null,string? appSha=null,string? coreSha=null)
    {
        if(!Requested(settings))return null;
        byte[] bytes;
        try{bytes=archived??File.ReadAllBytes(settings.Text(PathKey));}
        catch(IOException e){throw new InvalidDataException("Session binding unavailable: "+e.Message,e);}
        string sha=Hash(bytes);
        Require(sha==settings.Text(ShaKey),"binding file SHA changed since review");
        try
        {
            using var doc=JsonDocument.Parse(bytes);var r=doc.RootElement;
            Require(r.GetProperty("revision").GetString()=="texel-session-binding-record-v1","record revision");
            Require(r.GetProperty("bindingActive").GetBoolean(),"bindingActive=false");
            Require(r.GetProperty("status").GetString()=="SESSION_TRANSFER_CONFIRMED","status is not SESSION_TRANSFER_CONFIRMED");
            var live=r.GetProperty("liveBinding");
            // Constructor defaults must never turn an absent physical field into an admission.
            foreach(var property in typeof(TexelSessionBinding).GetProperties().Where(p=>p.Name!="Id"))
                Require(live.TryGetProperty(property.Name,out var value)&&value.ValueKind!=JsonValueKind.Null,"unknown "+property.Name);
            foreach(var property in live.EnumerateObject())
                Require(typeof(TexelSessionBinding).GetProperty(property.Name) is not null,"unknown liveBinding field "+property.Name);
            var b=live.Deserialize<TexelSessionBinding>()??throw new InvalidDataException("Missing liveBinding");
            Require(b.Id==r.GetProperty("liveBindingId").GetString(),"liveBindingId");
            Require(!live.TryGetProperty("Id",out var serializedId)||serializedId.GetString()==b.Id,"liveBinding.Id");
            var frozen=appSha is null?TexelProduct.Grid():TexelProduct.Grid(appSha,coreSha!);
            var g=frozen.Context.Geometry;var cal=settings.PaintCalibration();
            foreach(string key in new[]{"brush_shape_slot","brush_size_value","precision_brush_size","input_frame_delay_ms","cycle_delay_ms"})
                Require(!settings.Data.ContainsKey(key)||double.TryParse(settings.Text(key),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var n)&&double.IsFinite(n),"unknown numeric setting "+key);
            Require(b.UserRect==new ScreenRect(465,151,1509,1192)&&cal.Rect("canvas")==b.UserRect,"exact rect");
            Require(b.AppSha256==frozen.Context.AppSha256,"App SHA");
            Require(b.CoreSha256==frozen.Context.CoreSha256,"Core SHA");
            Require(b.SourceProfileSha256==TexelProduct.ProfileSha256&&r.GetProperty("ProfileSha256").GetString()==TexelProduct.ProfileSha256,"profile SHA");
            Require(b.GridId==frozen.Id&&r.GetProperty("FrozenGridId").GetString()==frozen.Id,"frozen Grid Id");
            var grid=TexelSessionAudit.Rebind(frozen,b.UserRect);
            Require(r.GetProperty("SessionGridId").GetString()==grid.Id,"rebound Grid Id");
            Require(b.ClientOrigin==g.ClientOrigin&&cal.SessionClient==b.ClientOrigin,"client origin");
            Require(b.ClientSize==g.ClientSize&&cal.SessionSize==b.ClientSize,"client size");
            Require(int.TryParse(cal.Data["session_dpi"]?.ToString(),out int rawDpi)&&rawDpi==b.Dpi&&b.Dpi==g.Dpi&&cal.SessionDpi==b.Dpi,"DPI");
            Require(b.ShapeSlot==3&&settings.Number("brush_shape_slot",3)==3,"shape slot 3");
            Require(b.Size==3&&PaintTimingPlan.DefaultSize(settings)==3&&settings.Number("brush_size_value",3)==3&&settings.Number("precision_brush_size",3)==3,"Size 3");
            Require(b.FrameSeconds==.02&&StrokeTiming.Frame(settings)==b.FrameSeconds,"frame 0.02");
            Require(b.HoldSeconds==g.EndHoldSeconds&&StrokeTiming.EndHold(settings,StandardRepeatSeries.Speed)==b.HoldSeconds,"hold timing");
            Require(b.ReleaseSeconds==g.ReleaseSeconds&&StrokeTiming.Release(settings)==b.ReleaseSeconds,"release timing");
            Require(b.RequestedSpeed==settings.Text("speed_profile")&&b.RequestedSpeed=="Max Speed","requested speed");
            Require(b.EffectiveEmitterSpeed==StandardRepeatSeries.Speed&&b.EffectiveEmitterSpeed==g.Speed,"effective emitter speed");
            Require(b.ViewSha256==TexelProduct.ViewPngSha256,"view SHA");
            Require(b.StructureSha256 is {Length:64}&&b.StructureSha256.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f'),"structure SHA unknown");
            var structure=CanvasStructure.Reference(settings);
            var local=new ScreenRect(b.UserRect.Left-b.ClientOrigin.X,b.UserRect.Top-b.ClientOrigin.Y,b.UserRect.Right-b.ClientOrigin.X,b.UserRect.Bottom-b.ClientOrigin.Y);
            Require(CanvasStructure.Current(structure,local,b.Dpi,b.ClientSize)&&string.Equals(structure!.FrameSha256,b.StructureSha256,StringComparison.OrdinalIgnoreCase),"confirmed structure / 16 ROI");
            Require(ReleaseProfile.Standard(settings)&&settings.Mode==ColorMode.RustPalette&&!StandardLineMotion.Enabled(settings)
                &&!settings.Bool("adaptive_brush")&&!settings.Bool("coverage_audit"),"Standard Rust palette / unsupported mode");
            return new(b,sha,bytes,grid);
        }
        catch(Exception e) when(e is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        {throw new InvalidDataException("Session binding refused: missing or invalid field: "+e.Message,e);}
    }
    internal static Settings Pin(Settings source,string bindingFile)
    {
        var s=source.Clone();s.Set(PathKey,Path.GetFullPath(bindingFile));s.Set(ShaKey,Hash(File.ReadAllBytes(bindingFile)));
        Resolve(s);return s;
    }
    internal static Settings Attach(Settings source,string bindingFile,string editorFile)
    {
        var bytes=File.ReadAllBytes(editorFile);
        Require(Hash(bytes)==File.ReadAllText(editorFile+".sha256").Trim().ToLowerInvariant(),"session editor SHA");
        var editor=JsonSerializer.Deserialize<FrontierSessionCapture.Record>(bytes)??throw new InvalidDataException("Missing session editor");
        Require(editor.Revision==FrontierSessionCapture.Revision&&editor.UserConfirmed&&!editor.Synthetic&&editor.RustPid!=0,"confirmed native session editor");
        var frame=Images.LoadNative(Path.ChangeExtension(editorFile,"png"));
        Require(frame.Width==2560&&frame.Height==1440&&editor.Screen==new ScreenRect(0,0,2560,1440)
            &&string.Equals(Hash(frame.Rgba),editor.FrameSha256,StringComparison.OrdinalIgnoreCase),"session editor frame");
        TexelProduct.RequireView(frame);
        var s=FrontierSessionCapture.Apply(source,editor.Reference,Hash(bytes),true);
        s=Pin(s,bindingFile);var b=Resolve(s)!.Binding;
        Require(editor.Origin==b.ClientOrigin&&editor.Client==b.ClientSize&&editor.Dpi==b.Dpi&&editor.AppSha256==b.AppSha256&&editor.CoreSha256==b.CoreSha256,"session editor identity");
        var proof=EditorEvidence.Read(s,editor.Reference,frame,editor.Screen,editor.Origin,editor.Dpi,editor.Client);
        Require(proof.State==EditorEvidenceState.Present,"session editor proof / 16 ROI");
        return s;
    }
}
