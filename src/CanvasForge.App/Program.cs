using System.Windows;
using System.Windows.Media;
using CanvasForge.Core;

namespace CanvasForge.App;
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if(args.FirstOrDefault()=="--texel-session-package")
        {
            try
            {
                if(args.Length!=6)throw new ArgumentException("Use --texel-session-package MAIN_CONFIG BINDING_JSON SESSION_EDITOR_JSON PIC8_ASSETS NEW_OUTPUT.");
                TexelSessionProductPackage.Create(args[1],args[2],args[3],args[4],args[5]);
                Console.WriteLine("Product_1 session package prepared; nativeInput=false; physicalQualityCertified=false");
            }
            catch(Exception e){Console.Error.WriteLine(e.Message);Environment.ExitCode=1;}return;
        }
        // pic4 launcher reads redirected output as UTF-8; without a console the default is the ANSI code page,
        // which turned Ukrainian failure reasons into replacement characters in earlier launcher logs.
        if(args.FirstOrDefault()=="--standard-contour-pilot"&&(args.ElementAtOrDefault(1)?.StartsWith("pic4-",StringComparison.Ordinal)==true||args.ElementAtOrDefault(1)?.StartsWith("pic5-",StringComparison.Ordinal)==true||args.ElementAtOrDefault(1)?.StartsWith("pic6-",StringComparison.Ordinal)==true||args.ElementAtOrDefault(1)?.StartsWith("pic7-",StringComparison.Ordinal)==true||args.ElementAtOrDefault(1)?.StartsWith("pic8-",StringComparison.Ordinal)==true||args.ElementAtOrDefault(1)?.StartsWith("pic9-",StringComparison.Ordinal)==true||args.ElementAtOrDefault(1)?.StartsWith("session1-",StringComparison.Ordinal)==true))
        {
            var utf8=new System.Text.UTF8Encoding(false);
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(),utf8){AutoFlush=true});Console.SetError(new StreamWriter(Console.OpenStandardError(),utf8){AutoFlush=true});
        }
        // Read-only review of a product texel START from its last-run folder ("latest" = newest texel run).
        if(args.FirstOrDefault()=="--texel-product-review")
        {
            var utf8=new System.Text.UTF8Encoding(false);
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(),utf8){AutoFlush=true});Console.SetError(new StreamWriter(Console.OpenStandardError(),utf8){AutoFlush=true});
            try
            {
                if(args.Length!=3&&!(args.Length==5&&args[3]=="--resumed-from"))
                    throw new ArgumentException("Use --texel-product-review RUN_DIRECTORY|latest NEW_OUTPUT [--resumed-from FIRST_INTERRUPTED_RUN].");
                string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Pixora","last-run");
                string run=args[1]!="latest"?args[1]:TexelProductReview.Latest(root)??throw new DirectoryNotFoundException("Не знайдено прогону з тексельним маршрутом у "+root);
                var r=TexelProductReview.Review(run,args[2],args.Length==5?args[4]:null);
                Console.WriteLine(r.Status+(r.Picture is {} p?$"; gaps={p.InteriorGaps}/{p.InteriorRequired}; holes={p.HoleChanges}/{p.HoleRequired}; edgesWithEvents={p.EdgesWithEvents}":"")+$"; outside={r.OutsideChangedPixels}"+(r.Ends is {} e?$"; ends={e.Status} short={e.Short} extra={e.Extra} measured={e.Measured}/{e.Required}":"")+(r.Reason.Length>0?"; "+r.Reason:""));
                Console.WriteLine("run="+r.RunDirectory);
                if(r.Status=="INCOMPLETE")Environment.ExitCode=1;
            }
            catch(Exception error){Console.Error.WriteLine(error.Message);Environment.ExitCode=1;}return;
        }
        if(args.FirstOrDefault()=="--standard-contour-pilot"&&args.ElementAtOrDefault(1)?.StartsWith("offgrid-",StringComparison.Ordinal)==true&&args[1]!="offgrid-run")
        {
            try
            {
                if(args.Length==5&&args[1]=="offgrid-init")StandardRowOffgridPilot.Init(args[2],args[3],args[4]);
                else if(args.Length==3&&args[1]=="offgrid-preflight"){StandardRowOffgridPilot.Read(args[2]);Console.WriteLine("OFFGRID_PROTOCOL_FILES_VALIDATED; nativeInput=false");}
                else if(args.Length==3&&args[1]=="offgrid-verify")StandardRowOffgridPilot.Verify(args[2]);
                else if(args.Length==4&&args[1]=="offgrid-review"){var r=StandardRowOffgridAnalysis.Review(args[2],args[3]);Console.WriteLine(r.Status+"; mechanism="+r.GridVerdict);if(r.Status=="INCOMPLETE")Environment.ExitCode=1;}
                else throw new ArgumentException("Use offgrid-init SOURCE_ROW FIXTURES NEW_DIRECTORY; offgrid-preflight/offgrid-verify DIRECTORY; offgrid-review DIRECTORY NEW_OUTPUT.");
            }
            catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}return;
        }
        if(args.FirstOrDefault()=="--standard-contour-pilot"&&args.ElementAtOrDefault(1) is {} pic&&(pic.StartsWith("pic2-",StringComparison.Ordinal)||pic.StartsWith("pic3-",StringComparison.Ordinal)||pic.StartsWith("pic4-",StringComparison.Ordinal)||pic.StartsWith("pic5-",StringComparison.Ordinal)||pic.StartsWith("pic6-",StringComparison.Ordinal)||pic.StartsWith("pic7-",StringComparison.Ordinal)||pic.StartsWith("pic8-",StringComparison.Ordinal)||pic.StartsWith("pic9-",StringComparison.Ordinal))&&args[1] is not ("pic2-run" or "pic3-run" or "pic4-run" or "pic5-run" or "pic6-run" or "pic7-run" or "pic8-run" or "pic9-run"))
        {
            try
            {
                if(args.Length==5&&args[1]=="pic2-init")StandardRowPicturePilot.Init(args[2],args[3],args[4]);
                else if(args.Length==5&&args[1]=="pic3-init")StandardRowPicturePilot.Init(args[2],args[3],args[4],StandardRowPicturePilot.Pic3);
                else if(args.Length==5&&args[1]=="pic4-init")StandardRowPicturePilot.Init(args[2],args[3],args[4],StandardRowPicturePilot.Pic4);
                else if(args.Length==5&&args[1]=="pic5-init")StandardRowPicturePilot.Init(args[2],args[3],args[4],StandardRowPicturePilot.Pic5);
                else if(args.Length==5&&args[1]=="pic6-init")StandardRowPicturePilot.Init(args[2],args[3],args[4],StandardRowPicturePilot.Pic6);
                else if(args.Length==5&&args[1]=="pic7-init")StandardRowPicturePilot.Init(args[2],args[3],args[4],StandardRowPicturePilot.Pic7);
                else if(args.Length==5&&args[1]=="pic8-init")StandardRowPicturePilot.Init(args[2],args[3],args[4],StandardRowPicturePilot.Pic8);
                else if(args.Length==5&&args[1]=="pic9-init")StandardRowPicturePilot.Init(args[2],args[3],args[4],StandardRowPicturePilot.Pic9);
                else if(args.Length==3&&args[1] is "pic2-preflight" or "pic3-preflight" or "pic4-preflight" or "pic5-preflight" or "pic6-preflight" or "pic7-preflight" or "pic8-preflight" or "pic9-preflight"){StandardRowPicturePilot.Read(args[2]);Console.WriteLine("PICTURE_PROTOCOL_FILES_VALIDATED; nativeInput=false");}
                else if(args.Length==3&&args[1] is "pic2-verify" or "pic3-verify" or "pic4-verify" or "pic5-verify" or "pic6-verify" or "pic7-verify" or "pic8-verify" or "pic9-verify")StandardRowPicturePilot.Verify(args[2]);
                else if(args.Length==4&&args[1] is "pic2-review" or "pic3-review" or "pic4-review" or "pic5-review" or "pic6-review" or "pic7-review" or "pic8-review" or "pic9-review")
                {
                    var r=StandardRowPictureAnalysis.Review(args[2],args[3]);
                    Console.WriteLine(r.Status+"; pictures="+string.Join(",",r.Pictures.Select(p=>p.Name+":"+p.Status))+(r.Sweeps is null?"":"; sweeps="+string.Join(",",r.Sweeps.Select(w=>w.Name+":"+w.Status+(w.SelectedOffset is int o?"("+o+")":"")))));
                    if(r.Status=="INCOMPLETE")Environment.ExitCode=1;
                }
                else throw new ArgumentException("Use pic2-init SOURCE_ROW FIXTURES NEW_DIRECTORY; pic2-preflight/pic2-verify DIRECTORY; pic2-review DIRECTORY NEW_OUTPUT.");
            }
            catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}return;
        }
        // Session calibration 1 (stages 1+2): offline commands; session1-run is the only one that may send input.
        if(args.FirstOrDefault()=="--standard-contour-pilot"&&args.ElementAtOrDefault(1)?.StartsWith("session1-",StringComparison.Ordinal)==true&&args[1]!="session1-run")
        {
            try
            {
                if(args.Length==5&&args[1]=="session1-init")StandardSessionCalibPilot.Init(args[2],args[3],args[4]);
                else if(args.Length==3&&args[1]=="session1-preflight"){var m=StandardSessionCalibPilot.Read(args[2]);Console.WriteLine("SESSION_PROTOCOL_FILES_VALIDATED; stage0Audit=PASSED; bindingId="+m.BindingId+"; nativeInput=false");}
                else if(args.Length==3&&args[1]=="session1-verify")StandardSessionCalibPilot.Verify(args[2]);
                else if(args.Length==4&&args[1]=="session1-review")
                {
                    var r=StandardSessionCalibAnalysis.Review(args[2],args[3]);
                    Console.WriteLine(r.Status+"; controls="+string.Join(",",r.Controls.Select(c=>c.Label+":"+(c.MeasuredRow?.ToString()??"-")+"/"+(c.TopMedianAbsolute?.ToString("0.000",System.Globalization.CultureInfo.InvariantCulture)??"-")+"/"+(c.BottomMedianAbsolute?.ToString("0.000",System.Globalization.CultureInfo.InvariantCulture)??"-")))+"; outside="+(r.OutsideChangedPixels?.ToString()??"-")+(r.Reasons.Length>0?"; "+string.Join(" | ",r.Reasons):""));
                    if(r.Status==StandardSessionCalibAnalysis.Incomplete)Environment.ExitCode=1;
                }
                else throw new ArgumentException("Use session1-init MAIN_CONFIG FIXTURES NEW_DIRECTORY; session1-preflight/session1-verify DIRECTORY; session1-review DIRECTORY NEW_OUTPUT.");
            }
            catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}return;
        }
        if(args.FirstOrDefault()=="--standard-contour-pilot"&&args.ElementAtOrDefault(1)?.StartsWith("calib-",StringComparison.Ordinal)==true&&args[1]!="calib-run")
        {
            try
            {
                if(args.Length==5&&args[1]=="calib-init")StandardRowCalibPilot.Init(args[2],args[3],args[4]);
                else if(args.Length==3&&args[1]=="calib-preflight"){StandardRowCalibPilot.Read(args[2]);Console.WriteLine("CALIB_PROTOCOL_FILES_VALIDATED; nativeInput=false");}
                else if(args.Length==3&&args[1]=="calib-verify")StandardRowCalibPilot.Verify(args[2]);
                else if(args.Length==4&&args[1]=="calib-review"){var r=StandardRowCalibAnalysis.Review(args[2],args[3]);Console.WriteLine(r.Status);if(r.Status=="INCOMPLETE")Environment.ExitCode=1;}
                else throw new ArgumentException("Use calib-init SOURCE_ROW FIXTURES NEW_DIRECTORY; calib-preflight/calib-verify DIRECTORY; calib-review DIRECTORY NEW_OUTPUT.");
            }
            catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}return;
        }
        if(args.FirstOrDefault()=="--standard-contour-pilot"&&args.ElementAtOrDefault(1)?.StartsWith("plane-",StringComparison.Ordinal)==true&&args[1]!="plane-run")
        {
            try
            {
                if(args.Length==5&&args[1]=="plane-init")StandardPlanePilot.Init(args[2],args[3],args[4]);
                else if(args.Length==3&&args[1]=="plane-preflight"){StandardPlanePilot.Read(args[2]);Console.WriteLine("PLANE_PROTOCOL_FILES_VALIDATED; nativeInput=false");}
                else if(args.Length==3&&args[1]=="plane-verify")StandardPlanePilot.Verify(args[2]);
                else if(args.Length==4&&args[1]=="plane-review"){var r=StandardPlaneAnalysis.Review(args[2],args[3]);Console.WriteLine(r.Status+"; mechanism="+r.Mechanism);if(r.Status=="INCOMPLETE")Environment.ExitCode=1;}
                else throw new ArgumentException("Use plane-init SOURCE_ROW FIXTURES NEW_DIRECTORY; plane-preflight/plane-verify DIRECTORY; plane-review DIRECTORY NEW_OUTPUT.");
            }
            catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}return;
        }
        if(args.FirstOrDefault()=="--standard-contour-pilot"&&args.ElementAtOrDefault(1)?.StartsWith("row-",StringComparison.Ordinal)==true&&args[1]!="row-run")
        {
            try
            {
                if(args.Length==4&&args[1]=="row-init")StandardRowPilot.Init(args[2],args[3]);
                else if(args.Length==3&&args[1]=="row-preflight"){StandardRowPilot.Read(args[2]);Console.WriteLine("ROW_PROTOCOL_FILES_VALIDATED; nativeInput=false");}
                else if(args.Length==3&&args[1]=="row-verify")StandardRowPilot.Verify(args[2]);
                else if(args.Length==4&&args[1]=="row-review"){var r=StandardRowPilot.Review(args[2],args[3]);Console.WriteLine(r.Status);if(r.Status=="INCOMPLETE")Environment.ExitCode=1;}
                else throw new ArgumentException("Use row-init SOURCE_UV NEW_DIRECTORY; row-preflight/row-verify DIRECTORY; row-review DIRECTORY NEW_OUTPUT.");
            }
            catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}return;
        }
        if(args.FirstOrDefault()=="--standard-contour-pilot"&&args.ElementAtOrDefault(1)?.StartsWith("texel-",StringComparison.Ordinal)==true&&args[1]!="texel-run")
        {
            try
            {
                if(args.Length==4&&args[1]=="texel-init")StandardTexelPilot.Init(args[2],args[3]);
                else if(args.Length==3&&args[1]=="texel-preflight"){StandardTexelPilot.Read(args[2]);Console.WriteLine("TEXEL_PROTOCOL_FILES_VALIDATED; nativeInput=false");}
                else if(args.Length==3&&args[1]=="texel-verify")StandardTexelPilot.Verify(args[2]);
                else if(args.Length==4&&args[1]=="texel-review"){var r=StandardTexelAnalysis.Review(args[2],args[3]);Console.WriteLine(r.Verdict);if(r.Verdict=="INCOMPLETE")Environment.ExitCode=1;}
                else throw new ArgumentException("Use texel-init SOURCE_UV NEW_DIRECTORY; texel-preflight/texel-verify DIRECTORY; texel-review DIRECTORY NEW_OUTPUT.");
            }
            catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}return;
        }
        // UV diagnostics reuse the contour entry. Offline commands must return
        // before the input lease, WPF application and any Native.Release call.
        if(args.FirstOrDefault()=="--standard-contour-pilot"&&args.ElementAtOrDefault(1)?.StartsWith("uv-",StringComparison.Ordinal)==true&&args[1]!="uv-run")
        {
            try
            {
                if(args.Length==4&&args[1]=="uv-init")StandardUvPilot.Init(args[2],args[3]);
                else if(args.Length==3&&args[1]=="uv-preflight")
                {var m=StandardUvPilot.Read(args[2]);StandardUvAnalysis.CheckFixtures(Path.Combine(args[2],"fixtures"),m.Lines);Console.WriteLine("UV_PROTOCOL_FILES_VALIDATED; nativeInput=false; physicalQualityCertified=false");}
                else if(args.Length==3&&args[1]=="uv-verify")StandardUvPilot.Verify(args[2]);
                else if(args.Length==4&&args[1]=="uv-review")
                {var result=StandardUvAnalysis.Review(args[2],args[3]);Console.WriteLine(result.Verdict);if(result.Verdict=="INCOMPLETE")Environment.ExitCode=1;}
                else throw new ArgumentException("Use --standard-contour-pilot uv-init SOURCE_PAIR NEW_DIRECTORY; uv-preflight/uv-verify DIRECTORY; uv-review DIRECTORY NEW_OUTPUT.");
            }
            catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}return;
        }
        if(args.FirstOrDefault()=="--standard-discrete-band"&&args.ElementAtOrDefault(1) is not ("run" or "run-session"))
        {
            try
            {
                if(args.Length==4&&args[1]=="init")StandardDiscreteBandCli.Init(args[2],args[3]);
                else if(args.Length==4&&args[1]=="review")StandardDiscreteBandCli.Review(args[2],args[3]);
                else if(args.Length==3&&args[1]=="preflight")
                {StandardDiscreteBandCli.Read(args[2],preflight:true);Console.WriteLine("FRONTIER_FILES_VALIDATED; nativeInput=false; readiness of a new attempt checked separately");}
                else if(args.Length==3&&args[1]=="verify")
                {StandardDiscreteBandCli.Verify(args[2]);}
                else throw new ArgumentException("Use --standard-discrete-band init SOURCE NEW_PARENT; preflight/verify CONDITION; review PAIR NEW_OUTPUT.");
            }
            catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}return;
        }
        if(args.FirstOrDefault()=="--standard-band-inverse-evaluate")
        {
            try{if(args.Length!=3)throw new ArgumentException("Use --standard-band-inverse-evaluate COMPLETED_PILOT NEW_OUTPUT_DIRECTORY.");StandardBandInverseEvaluation.Run(args[1],args[2]);}
            catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}return;
        }
        if(args.Length>=2&&args[0]=="--standard-band-inverse"&&args[1] is "init" or "verify" or "preflight")
        {
            try
            {
                if(args[1]=="init")
                {if(args.Length!=4)throw new ArgumentException("Use --standard-band-inverse init COMPLETED_TRACK_PAIR NEW_DIRECTORY.");StandardBandInversePilotCli.Init(args[2],args[3]);}
                else if(args[1]=="preflight")
                {if(args.Length!=3)throw new ArgumentException("Use --standard-band-inverse preflight PILOT_DIRECTORY.");StandardBandInversePilotCli.Read(args[2]);Console.WriteLine("READY_FOR_MANUAL_RUN; nativeInput=false; portableReference=true");}
                else
                {if(args.Length!=3)throw new ArgumentException("Use --standard-band-inverse verify COMPLETED_PILOT.");var m=StandardBandInversePilotCli.Read(args[2],offline:true);StandardTrackPairCli.ValidateCompleted(args[2],StandardBandInversePilotCli.ValidationView(m));}
            }
            catch(Exception error){Console.Error.WriteLine(error);if(args[1]!="init"&&args.Length==3)StandardBandInversePilotCli.LogLaunchFailure(args[2],error);Environment.ExitCode=1;}return;
        }
        if(args.FirstOrDefault() is "--standard-band-response-review" or "--standard-band-inverse-review")
        {
            try
            {
                if(args.Length!=3)throw new ArgumentException("Use the band review command with COMPLETED_TRACK_PAIR NEW_OUTPUT_DIRECTORY.");
                StandardBandResponseReview.Run(args[1],args[2],inverse:args[0]=="--standard-band-inverse-review");
                Console.WriteLine("MEASURED_RESPONSE_MODEL_NOT_QUALIFIED; physicalQualityCertified=false; nativeInputByReviewer=false");
            }
            catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}return;
        }
        if(args.Length>=2&&args[0]=="--standard-track-pair"&&args[1] is "init" or "verify")
        {
            try
            {
                if(args[1]=="init")
                {if(args.Length!=4)throw new ArgumentException("Use --standard-track-pair init COMPLETED_CONTROL NEW_PARENT.");StandardTrackPairCli.Init(args[2],args[3]);}
                else
                {
                    if(args.Length!=3)throw new ArgumentException("Use --standard-track-pair verify COMPLETED_CONDITION.");
                    var manifest=StandardTrackPairCli.Read(args[2],offline:true);StandardTrackPairCli.ValidateCompleted(args[2],manifest);
                    if(manifest.Condition=="shifted")StandardTrackPairCli.RequireCompleteControl(args[2],manifest);
                    Console.WriteLine("DATA_VALIDATED_NOT_QUALIFIED; physicalQualityCertified=false; nativeInputByVerifier=false");
                }
            }
            catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}return;
        }
        if(args.Length>=2&&args[0]=="--standard-straight-band"&&args[1]=="init")
        {
            try{if(args.Length!=4)throw new ArgumentException("Use --standard-straight-band init COMPLETED_CONTROL NEW_DIRECTORY.");StandardStraightPilotCli.Init(args[2],args[3]);}
            catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}return;
        }
        if(args.Length>=2&&args[0]=="--standard-contour-pilot"&&args[1]=="init")
        {
            try{if(args.Length!=4)throw new ArgumentException("Use --standard-contour-pilot init COMPLETED_SOURCE NEW_PARENT.");StandardContourPilotCli.Init(args[2],args[3]);}
            catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}return;
        }
        // A read-only offline command must not acquire a paint lease, construct
        // WPF windows or release another application's held native input.
        if(args.FirstOrDefault() is "--standard-boundary-review" or "--standard-size3-repeat-review" or "--standard-edge-correction-review")
        {
            try
            {
                if(args.Length!=3)throw new ArgumentException("Use the review command with EVIDENCE_DIRECTORY NEW_OUTPUT_DIRECTORY.");
                if(args[0]=="--standard-edge-correction-review")StandardEdgeCorrectionReview.Run(args[1],args[2]);
                else if(args[0]=="--standard-size3-repeat-review")StandardRepeatReview.Run(args[1],args[2]);else StandardBoundaryReview.Run(args[1],args[2]);
            }
            catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}
            return;
        }
        using var instance=SingleInstanceLease.Acquire();
        if(instance is null)
        {
            string language=LanguageCatalog.FromCulture(System.Globalization.CultureInfo.CurrentUICulture);
            try{language=Settings.Load(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Pixora","config-csharp.json")).Text("language");}
            catch(Exception e) when(e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException){}
            MessageBox.Show(Translations.ForLanguage("Pixora вже запущена. Закрий іншу копію перед новим запуском.",language,
                "Pixora is already running. Close the other copy before starting a new one."),"Pixora",MessageBoxButton.OK,MessageBoxImage.Information);
            return;
        }
        var application = new Application
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose
        };
        if(args.FirstOrDefault()=="--standard-discrete-band")
        {
            try{if(args.Length!=3||args[1] is not ("run" or "run-session"))throw new ArgumentException("Use --standard-discrete-band run-session FROZEN_CONDITION_DIRECTORY.");StandardDiscreteBandCli.Run(args[2],explicitSessionCapture:args[1]=="run-session");}
            catch(Exception error){Environment.ExitCode=1;if(args.Length==3)StandardBandInversePilotCli.LogLaunchFailure(args[2],error);Native.Release();MessageBox.Show(error.Message,"Pixora: вимірювання двох країв",MessageBoxButton.OK,MessageBoxImage.Warning);}
            finally{application.Shutdown();}return;
        }
        if(args.FirstOrDefault()=="--standard-band-inverse")
        {
            try{if(args.Length!=3||args[1]!="run")throw new ArgumentException("Use --standard-band-inverse run FROZEN_PILOT_DIRECTORY.");StandardBandInversePilotCli.Run(args[2]);}
            catch(Exception error){Environment.ExitCode=1;if(args.Length==3)StandardBandInversePilotCli.LogLaunchFailure(args[2],error);Native.Release();MessageBox.Show(error.Message,"Pixora: пробна корекція меж",MessageBoxButton.OK,MessageBoxImage.Warning);}
            finally{application.Shutdown();}return;
        }
        if(args.FirstOrDefault()=="--standard-track-pair")
        {
            try{if(args.Length!=3||args[1]!="run")throw new ArgumentException("Use --standard-track-pair run FROZEN_CONDITION_DIRECTORY.");StandardTrackPairCli.Run(args[2]);}
            catch(Exception error){Environment.ExitCode=1;Native.Release();MessageBox.Show(error.Message,"Pixora: поетапні сліди",MessageBoxButton.OK,MessageBoxImage.Warning);}
            finally{application.Shutdown();}return;
        }
        if(args.FirstOrDefault()=="--standard-straight-band")
        {
            try{if(args.Length!=3||args[1]!="run")throw new ArgumentException("Use --standard-straight-band run FROZEN_DIRECTORY.");StandardStraightPilotCli.Run(args[2]);}
            catch(Exception error){Environment.ExitCode=1;Native.Release();MessageBox.Show(error.Message,"Pixora straight bands",MessageBoxButton.OK,MessageBoxImage.Warning);}
            finally{application.Shutdown();}return;
        }
        if(args.FirstOrDefault()=="--standard-contour-pilot")
        {
            try
            {
                if(args.Length==3&&args[1] is "pic2-run" or "pic3-run" or "pic4-run" or "pic5-run" or "pic6-run" or "pic7-run" or "pic8-run" or "pic9-run")StandardRowPicturePilot.Run(args[2]);
                else if(args.Length==3&&args[1]=="session1-run")StandardSessionCalibPilot.Run(args[2]);
                else if(args.Length==3&&args[1]=="calib-run")StandardRowCalibPilot.Run(args[2]);
                else if(args.Length==3&&args[1]=="offgrid-run")StandardRowOffgridPilot.Run(args[2]);
                else if(args.Length==3&&args[1]=="plane-run")StandardPlanePilot.Run(args[2]);
                else if(args.Length==3&&args[1]=="row-run")StandardRowPilot.Run(args[2]);
                else if(args.Length==3&&args[1]=="texel-run")StandardTexelPilot.Run(args[2]);
                else if(args.Length==3&&args[1]=="uv-run")StandardUvPilot.Run(args[2]);
                else if(args.Length==3&&args[1]=="run")StandardContourPilotCli.Run(args[2]);
                else throw new ArgumentException("Use --standard-contour-pilot texel-run or uv-run FROZEN_DIRECTORY.");
            }
            catch(Exception error)
            {
                Environment.ExitCode=1;if(args.Length==3)StandardBandInversePilotCli.LogLaunchFailure(args[2],error);Native.Release();
                if(args.ElementAtOrDefault(1) is "session1-run" or "row-run" or "plane-run" or "offgrid-run" or "calib-run" or "pic2-run" or "pic3-run" or "pic4-run" or "pic5-run" or "pic6-run" or "pic7-run" or "pic8-run" or "pic9-run")Console.Error.WriteLine(error);
                else MessageBox.Show(error.Message,"Pixora contour experiment",MessageBoxButton.OK,MessageBoxImage.Warning);
            }
            finally{application.Shutdown();}return;
        }
        if(args.FirstOrDefault() is "--standard-evidence-series" or "--standard-size3-repeat")
        {
            try{if(args[0]=="--standard-size3-repeat")StandardRepeatSeriesCli.Run(args);else StandardEvidenceSeriesCli.Run(args);}
            catch(Exception error){if(args.ElementAtOrDefault(1) is "run" or "collect")Native.Release();MessageBox.Show(error.Message,"Pixora evidence",MessageBoxButton.OK,MessageBoxImage.Warning);}
            finally{application.Shutdown();}
            return;
        }
        if(args.FirstOrDefault() is "--standard-quality-evidence" or "--standard-direction-evidence")
        {
            try{if(args[0]=="--standard-direction-evidence")StandardDirectionEvidenceCli.Run(args);else StandardQualityEvidenceCli.Run(args);}
            catch(Exception error){if(args.ElementAtOrDefault(1)=="sample")Native.Release();MessageBox.Show(error.Message,"Pixora evidence",MessageBoxButton.OK,MessageBoxImage.Warning);}
            finally{application.Shutdown();}
            return;
        }
        bool fatalShutdown=false;
        application.DispatcherUnhandledException += (_, e) =>
        {
            if(fatalShutdown){e.Handled=true;return;}
            fatalShutdown=FatalErrorPolicy.MustStop(e.Exception);
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pixora", "crash.log");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, DateTimeOffset.Now + " [" + BuildInfo.Full + "]\n" + e.Exception + "\n");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (OutOfMemoryException) when(fatalShutdown) { }
            Native.Release();
            // A renderer allocation failure cannot be recovered by rendering
            // another error dialog. Release input and stop after one log entry.
            if(fatalShutdown){e.Handled=true;application.Shutdown(1);return;}
            if (application.MainWindow is MainWindow window) window.ShowMessage(e.Exception.Message);
            else MessageBox.Show(Translations.ForLanguage(e.Exception.Message, false), "Pixora", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
        application.Resources["Bg"] = new SolidColorBrush(Color.FromRgb(24, 24, 28));
        // --data-folder DIR runs the normal app on a separate settings/resume/last-run folder (isolated live tests);
        // no migration from the default folder.
        application.Run(new MainWindow(args is ["--data-folder",var data]?data:null));
    }
}
