using System.IO;
using System.Windows;
using System.Windows.Controls;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static void CheckTargetRecipeUi(string output)
    {
        foreach(string profile in new[]{"Standard","Experimental"})foreach(bool hex in new[]{false,true})
        {
            string name="target-recipe-"+profile+(hex?"-hex":"-palette");
            string folder=Path.Combine(output,name);Directory.CreateDirectory(folder);
            var s=ReadySettings("English");s.Set("release_profile",profile);s.Set("speed_profile","Safe");s.Set("cell_px",2);
            s.Set("color_mode",hex?"HEX Direct":"Rust Palette");s.Set("max_colors","8");s.Set("hex_max_colors","8");
            s.Save(Path.Combine(folder,SettingsStore.ConfigName));var w=FixtureWindow(folder);
            var image=new PixelImage(16,16);for(int i=0;i<256;i++)image.Set(i,i%2==0?Rgb.White:new(0,0,0));
            string OriginalRecipe()=>ImageRecipe.Capture(Field<Settings>(w,"settings")).ToJson().ToJsonString();
            string original=OriginalRecipe();var page=Field<Dictionary<string,FrameworkElement>>(w,"pages")["paint"];
            var speed=LogicalNodes(page).OfType<ComboBox>().Single(c=>Equals(c.Tag,"speed_profile"));
            speed.SelectedIndex=Array.FindIndex(SpeedProfile.All,p=>p.Name=="Max Speed");Invoke(w,"ReadSettings");
            Assert(Field<Settings>(w,"settings").Text("speed_profile")=="Max Speed"&&OriginalRecipe()==original,"Speed selector changed image recipe: "+original+" -> "+OriginalRecipe());
            Field<Dictionary<int,Button>>(w,"detailPresets")[8].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            var changed=Field<Settings>(w,"settings");Assert(changed.Int("cell_px")==8&&changed.Text("speed_profile")=="Max Speed","Detail preset changed speed");
            SetField(w,"plan",Planner.Build(image,changed));Invoke(w,"RefreshTargetPlanningUi");
            var label=Field<TextBlock>(w,"targetRecipeSummary");
            Assert(label.Visibility==Visibility.Visible&&label.Text.Contains("cell 8 px")&&label.Text.Contains("color limit 8")&&label.Text.Contains("Cell and colors change the image"),"Missing visible image recipe explanation");
            changed.Set(hex?"hex_max_colors":"max_colors","2");SetField(w,"plan",Planner.Build(image,changed));Invoke(w,"RefreshTargetPlanningUi");
            Assert(label.Text.Contains("color limit 2"),"Color limit explanation stayed stale");
            Render(w,Path.Combine(output,name+".png"),1280,780,false);
        }
        Console.WriteLine("PASS target-recipe-UI-speed-and-detail-independent-with-visible-color-cell-explanation");
    }
}
