using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CanvasForge.Core;

namespace CanvasForge.App;

internal sealed class FrontierSessionCaptureWindow:Window
{
    internal bool Accepted {get;private set;}
    internal readonly Button ConfirmButton=new(){Content="Підтвердити — Enter",Padding=new Thickness(14,8,14,8)};
    internal readonly Button CancelButton=new(){Content="Скасувати — Esc",Padding=new Thickness(14,8,14,8)};
    internal FrontierSessionCaptureWindow(PixelImage frame,ScreenRect screen,ScreenRect canvas,IReadOnlyList<CanvasPatch> patches,int dpi,ScreenPoint origin=default)
    {
        double scale=96d/dpi;Title="Pixora: підтвердження еталона поточної сесії";
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.Manual;
        Left=screen.Left*scale;Top=screen.Top*scale;Width=screen.Width*scale;Height=screen.Height*scale;Topmost=true;ShowInTaskbar=true;
        var grid=new Grid();grid.Children.Add(new Image{Source=Images.Bitmap(frame),Stretch=Stretch.Fill});var overlay=new Canvas();grid.Children.Add(overlay);Content=grid;
        void Box(ScreenRect r,Brush color,int thickness)
        {var box=new Rectangle{Width=r.Width*scale,Height=r.Height*scale,Stroke=color,StrokeThickness=thickness,IsHitTestVisible=false};Canvas.SetLeft(box,(r.Left-screen.Left)*scale);Canvas.SetTop(box,(r.Top-screen.Top)*scale);overlay.Children.Add(box);}
        Box(canvas,Brushes.Orange,3);foreach(var patch in patches)Box(new(patch.Rect.Left+origin.X,patch.Rect.Top+origin.Y,patch.Rect.Right+origin.X,patch.Rect.Bottom+origin.Y),Brushes.Lime,1);
        var panel=new StackPanel{Background=new SolidColorBrush(Color.FromRgb(24,24,28)),MaxWidth=Math.Max(300,Width-36)};
        panel.Children.Add(new TextBlock{Text="Новий еталон рамки цієї сесії Rust.\nПомаранчева рамка — точні координати попереднього виділення. Вони не змінюються.\nПідтверджуй лише якщо полотно очищене й область правильна.\nEnter — підтвердити, Esc — скасувати.",Foreground=Brushes.White,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(14),FontSize=17});
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(14,0,14,14)};buttons.Children.Add(ConfirmButton);buttons.Children.Add(CancelButton);panel.Children.Add(buttons);
        Canvas.SetLeft(panel,18);Canvas.SetTop(panel,18);overlay.Children.Add(panel);
        ConfirmButton.Click+=(_,_)=>{Accepted=true;Close();};CancelButton.Click+=(_,_)=>{Accepted=false;Close();};
        KeyDown+=(_,e)=>{if(e.Key==Key.Enter){Accepted=true;e.Handled=true;Close();}else if(e.Key==Key.Escape){Accepted=false;e.Handled=true;Close();}};
    }
    internal static bool Confirm(PixelImage frame,ScreenRect screen,ScreenRect canvas,IReadOnlyList<CanvasPatch> patches,int dpi,ScreenPoint origin)
    {
        var app=Application.Current;var mode=app.ShutdownMode;var main=app.MainWindow;app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        try{var window=new FrontierSessionCaptureWindow(frame,screen,canvas,patches,dpi,origin);window.ShowDialog();return window.Accepted;}
        finally{app.MainWindow=main;app.ShutdownMode=mode;}
    }
}
