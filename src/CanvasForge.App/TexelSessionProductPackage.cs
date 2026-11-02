using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;

internal static class TexelSessionProductPackage
{
    // Preparation only. The normal UI START remains the sole execution path.
    internal static void Create(string config,string binding,string editor,string pic8Assets,string output)
    {
        output=Path.GetFullPath(output);
        if(Directory.Exists(output)||File.Exists(output))throw new IOException("Package output already exists.");
        var original=File.ReadAllBytes(config);var s=Settings.Load(config);
        s.Set(TexelExperimentSettings.EnabledKey,true);s.Set(TexelExperimentSettings.ColumnKey,300);s.Set(TexelExperimentSettings.RowKey,203);
        s=TexelProductSession.Attach(s,binding,editor);
        var frozen=TexelProduct.Grid().Context.Geometry;
        var picture=StandardRowPicturePilot.BuildPictures(frozen,pic8Assets,StandardRowPicturePilot.Pic8).Single(p=>p.Name=="pictureA");
        var target=new PixelImage(picture.Mask[0].Length,picture.Mask.Length);
        for(int y=0;y<target.Height;y++)for(int x=0;x<target.Width;x++)target.Set(y*target.Width+x,new(0,0,0),(byte)(picture.Mask[y][x]=='#'?255:0));
        var plan=TexelProduct.Build(target,s);var route=plan.ExperimentalTexel!;
        if(route.Runs.Count!=107||!route.Runs.Zip(picture.Runs).All(p=>p.First.Path.Points.SequenceEqual(p.Second.Path)))
            throw new InvalidDataException("Product_1 differs from the frozen 107-stroke protocol.");
        if(!File.ReadAllBytes(config).SequenceEqual(original))throw new IOException("Main config changed during preparation.");
        Directory.CreateDirectory(output);var data=Path.Combine(output,"Data");Directory.CreateDirectory(data);
        var admission=TexelProductSession.Resolve(s)!;
        string local=Path.Combine(output,"session-binding.json");File.WriteAllBytes(local,admission.Record);s.Set(TexelProductSession.PathKey,local);
        File.WriteAllBytes(Path.Combine(output,"source-config.json"),original);
        File.WriteAllText(Path.Combine(data,SettingsStore.ConfigName),s.Data.ToJsonString());Images.Save(target,Path.Combine(output,"Product_1.png"));
        File.WriteAllText(Path.Combine(output,"package.json"),JsonSerializer.Serialize(new{revision=TexelProductSession.Revision,bindingId=admission.Binding.Id,
            bindingRecordSha256=admission.RecordSha256,sourceConfigSha256=TexelProductSession.Hash(original),requestedSpeed=admission.Binding.RequestedSpeed,
            effectiveSpeed=admission.Binding.EffectiveEmitterSpeed,rect=admission.Binding.UserRect,strokes=107,nativeInput=false,physicalQualityCertified=false},new JsonSerializerOptions{WriteIndented=true}));
    }
}
