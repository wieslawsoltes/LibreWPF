using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.ProGPU;
using System.Windows.Threading;
using ProGPU.Wpf.Interop;

namespace LinuxMessageBoxCaption;
public sealed class App : Application
{
    readonly Stopwatch clock = Stopwatch.StartNew();
    Window owner = null!;
    DispatcherTimer timer = null!;
    string output = null!, run = null!, renderer = null!;
    Window? dialog;
    int index = -1, phase;
    double firstSnapshot;
    Exception? failure;
    readonly List<object> results = new();
    static readonly MessageBoxButton[] buttons = { MessageBoxButton.OK, MessageBoxButton.OKCancel, MessageBoxButton.YesNo, MessageBoxButton.YesNoCancel };
    static readonly JsonSerializerOptions json = new() { WriteIndented = true, IncludeFields = true };
    [STAThread] public static int Main() => new App { ShutdownMode = ShutdownMode.OnExplicitShutdown }.Run();
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (!OperatingSystem.IsLinux() || e.Args.Length != 3 || Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted" || Environment.GetEnvironmentVariable("GITHUB_REPOSITORY") != "wieslawsoltes/LibreWPF") throw new InvalidOperationException("Hosted Linux only");
        output = Path.GetFullPath(e.Args[0]); run = e.Args[1]; renderer = e.Args[2];
        if (Directory.Exists(output) || !Guid.TryParseExact(run, "N", out _) || renderer is not ("NativeMilWgpu" or "ManagedPortable")) throw new InvalidOperationException("Fresh owned output and renderer required");
        Directory.CreateDirectory(output);
        owner = new Window { Title = "Caption owner " + run, Width = 760, Height = 520, Left = 80, Top = 80,
            Content = new Button { Content = "Owner input guard", Width = 160, Height = 36, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(16,48,0,0) } };
        MainWindow = owner;
        timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) => Observe();
        owner.ContentRendered += (_, _) => { if(index == -1) Dispatcher.BeginInvoke(new Action(OpenDialogs)); };
        timer.Start(); owner.Show();
    }
    string Caption => "MessageBox " + run + " " + buttons[index];
    void OpenDialogs()
    {
        if(index != -1) return;
        try {
            for(index=0;index<buttons.Length;index++) {
                dialog=null;phase=0;
                var response=MessageBox.Show(owner,"Actual source-created MessageBox. Bounded native caption and owner-input acceptance.",Caption,buttons[index],MessageBoxImage.None);
                if(failure != null) throw new InvalidOperationException("Observation failed",failure);
                if(dialog == null || dialog.IsVisible || phase != 2) throw new InvalidOperationException("Missing complete diagnostic snapshots");
                results.Add(new {index,response=response.ToString()});
            }
            timer.Stop();Write("complete.json",new {completed=true,qualified=false,inputEventsSent=0,pid=Environment.ProcessId,run,renderer,elapsedSeconds=clock.Elapsed.TotalSeconds,results});owner.Close();Shutdown(0);
        } catch(Exception error) { Fail(error); }
    }
    void Observe()
    {
        try {
            if(clock.Elapsed.TotalSeconds>55) throw new TimeoutException("Original55second source limit");
            if(index<0 || index>=buttons.Length) return;
            dialog ??= Windows.Cast<Window>().SingleOrDefault(w=>w.IsVisible && w.Title==Caption && w.Owner==owner);
            if(dialog==null) return;
            if(phase<2) {
                if(!ProGpuWpfDiagnostics.TryGetDesktopWindowSnapshots(dialog,out var values) || values.Length!=1 || values[0].PresentedFrameCount<2) return;
                if(phase==1 && clock.Elapsed.TotalSeconds-firstSnapshot<0.5) return;
                if(phase==1 && !Ack(0)) return;
                Snapshot(dialog,phase,values[0].PresentedFrameCount);
                if(phase==0) firstSnapshot=clock.Elapsed.TotalSeconds;
                phase++;return;
            }
            if(!Ack(1))return;
            if(buttons[index]==MessageBoxButton.YesNo) {
                var panel=(StackPanel)((Grid)dialog.Content).Children[1];
                var no=panel.Children.OfType<Button>().Single(b=>Equals(b.Content,"_No"));
                no.RaiseEvent(new RoutedEventArgs(Button.ClickEvent,no));
            } else dialog.Close();
        } catch(Exception error) { failure=error;Fail(error); }
    }
    bool Ack(int stage) {var path=Path.Combine(output,$"capture-{index}-{stage}.ack");if(!File.Exists(path))return false;if(File.ReadAllText(path)!=run)throw new InvalidOperationException("Foreign acknowledgement");return true;}
    void Snapshot(Window window,int stage,long frames)
    {
        if(!ProGpuWpfDiagnostics.TryGetWindowHost(window,out var host) || host?.CompositionTarget is not {} target) throw new InvalidOperationException("Owned composition target missing");
        if(!ProGpuWpfDiagnostics.TryGetDesktopWindowSnapshots(window,out var native) || native.Length!=1 || native[0].RendererMode.ToString()!=renderer || native[0].NativeWindow.Kind.ToString()!="X11")throw new InvalidOperationException("Renderer/native identity mismatch");
        if(!ProGpuWpfDiagnostics.TryGetRenderSurfaceGeometry(window,out var geometry))throw new InvalidOperationException("Missing source client geometry");
        var source=new List<object>();Source(window,-1,source,$"{index}-{stage}");
        var retained=new List<object>();Retained(target.SceneRootVisual,-1,retained);
        Write($"snapshot-{index}-{stage}.json",new {pid=Environment.ProcessId,run,renderer,index,stage,frames,elapsedSeconds=clock.Elapsed.TotalSeconds,nativeHandle=native[0].NativeWindow.Handle.ToInt64(),title=window.Title,geometry,source,retained,host.LastVisualReplayResult,host.RetainedWpfReplaySkipCount,host.RetainedWpfBranchReplayCount,target.DirtySourceCount,target.RetainedVisualBranchSourceCount,target.RetainedVisualBranchCount});
    }
    void Source(Visual visual,int parent,List<object> nodes,string prefix)
    {
        if(nodes.Count>=512)throw new InvalidOperationException("Source inventory bound");
        int id=nodes.Count;var offset=VisualTreeHelper.GetOffset(visual);int count=VisualTreeHelper.GetChildrenCount(visual);
        string? record=null,sha=null;int bytes=0;string?[]? dependencies=null;
        if(visual is IPortableDrawingContentSource content && content.TryGetPortableDrawingContent(out var value) && value is IPortableRenderDataSource data && data.TryGetPortableRenderDataSnapshot(out var snapshot)) {
            bytes=snapshot.RenderData.Length;if(bytes>1024*1024)throw new InvalidOperationException("Source byte bound");
            record=$"records-{prefix}-{id}.bin";File.WriteAllBytes(Path.Combine(output,record),snapshot.RenderData);sha=Convert.ToHexString(SHA256.HashData(snapshot.RenderData));dependencies=snapshot.DependentResources.Select(x=>x?.GetType().FullName).ToArray();
        }
        var stateSource=(IPortableVisualStateSource)visual;bool stateAvailable=stateSource.TryGetPortableVisualState(out var state);
        nodes.Add(new {id,parent,type=visual.GetType().FullName,stateAvailable,children=count,offset=new {offset.X,offset.Y},size=visual is UIElement ui ? new {ui.RenderSize.Width,ui.RenderSize.Height}:null,state.HasVisibility,visibility=state.Visibility.ToString(),state.HasOpacity,state.Opacity,state.HasClip,clip=state.Clip?.ToString(),state.HasTransform,transform=state.Transform?.ToString(),record,sha,bytes,dependencies});
        for(int i=0;i<count;i++)Source((Visual)VisualTreeHelper.GetChild(visual,i),id,nodes,prefix);
    }
    static void Retained(global::ProGPU.Scene.Visual visual,int parent,List<object> nodes)
    {
        if(nodes.Count>=512)throw new InvalidOperationException("Retained inventory bound");int id=nodes.Count;
        var children=visual is global::ProGPU.Scene.ContainerVisual container ? container.Children : Array.Empty<global::ProGPU.Scene.Visual>();
        global::ProGPU.Scene.DrawingContext? paint = (visual as global::ProGPU.Scene.DrawingVisual)?.Context;
        string? paintSource = paint != null ? "DrawingVisual.Context" : null;
        // This is a read-only diagnostic of the exact qualified adapter type, not
        // a product API or arbitrary source getter. Keep paint and hit metadata distinct.
        if (visual.GetType().FullName == "System.Windows.Media.ProGPU.Composition.ProGpuRetainedDrawingVisual" &&
            visual.GetType().Assembly == typeof(ProGpuWpfWindowHost).Assembly) {
            paint = (global::ProGPU.Scene.DrawingContext?)visual.GetType().GetProperty("Context")!.GetValue(visual);
            paintSource = "ProGpuRetainedDrawingVisual.Context";
        }
        var hit = (visual as global::ProGPU.Scene.ISourceGeometryHitTestCommands)?.SourceHitTestCommands;
        nodes.Add(new {id,parent,type=visual.GetType().FullName,visual.IsVisible,visual.Opacity,visual.Offset,visual.Size,visual.Transform,visual.ClipBounds,visual.OuterClipBounds,paintSource,
            paintCommands=paint?.Commands.Select(x=>x.Type.ToString()).ToArray(),
            sourceHitCommands=hit?.Commands.Select(x=>x.Type.ToString()).ToArray(),
            paintAndSourceHitContextIdentical=paint != null && ReferenceEquals(paint,hit),children=children.Count});
        foreach(var child in children)Retained(child,id,nodes);
    }
    void Write(string name,object value) {var path=Path.Combine(output,name);File.WriteAllText(path+".tmp",JsonSerializer.Serialize(value,json));File.Move(path+".tmp",path);}
    void Fail(Exception e) {timer?.Stop();if(output!=null && !File.Exists(Path.Combine(output,"failure.json")))Write("failure.json",new {error=e.ToString(),elapsedSeconds=clock.Elapsed.TotalSeconds});Shutdown(1);}
}
