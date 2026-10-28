using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using ProGPU.Backend;
using ProGPU.Scene;
using ProGPU.Vector;
using SceneVisual = ProGPU.Scene.Visual;
using SceneContext = ProGPU.Scene.DrawingContext;

namespace LinuxMessageBoxCaption;

public sealed partial class App
{
    // Diagnostic-only, read-only reflection of the exact qualified Scene assembly.
    // Never invoke an unknown source getter, materialize a cache, or read GPU memory.
    static object? Field(object owner, Type declaring, string name) =>
        (declaring.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
         ?? throw new InvalidOperationException("Missing exact producer field: " + name)).GetValue(owner);

    static object? Texture(GpuTexture? value) => value == null ? null : new {
        identity=RuntimeHelpers.GetHashCode(value),value.Width,value.Height,
        format=value.Format.ToString(),value.IsDisposed
    };

    static object? BrushState(Brush? value) => value == null ? null : new {
        type=value.GetType().FullName,identity=RuntimeHelpers.GetHashCode(value),value.Opacity,
        solidColor=value is SolidColorBrush solid ? (System.Numerics.Vector4?)solid.Color : null
    };

    static object CommandState(RenderCommand command) => new {
        type=command.Type.ToString(),command.Rect,command.Position,command.Position2,
        command.Transform,command.FloatParam,command.IntParam,command.TextureOpacity,
        command.HasTextureOpacity,command.IsSourceOpacityScope,brush=BrushState(command.Brush),
        command.PointBufferOffset,command.PointBufferCount,command.DoubleBufferOffset,
        command.DoubleBufferCount,command.FontSize,command.HitTestId
    };

    static object ContextState(SceneContext context)
    {
        if(context.Commands.Count>65536)throw new InvalidOperationException("Command observation bound");
        return new {identity=RuntimeHelpers.GetHashCode(context),commands=context.Commands.Select(CommandState).ToArray(),
            points=context.PointBuffer.Count,doubles=context.DoubleBuffer.Count,
            floats=context.FloatBuffer.Count,lines3D=context.Line3DBuffer.Count};
    }

    static object DrawState(Compositor.CompositorDrawCall draw) => new {
        type=draw.Type.ToString(),draw.IsSolidRect,draw.IsSolidRounded,draw.IndexStart,draw.IndexCount,
        draw.Transform,draw.Color,draw.Scale,draw.Translate,draw.ClipRect,
        texture=Texture(draw.Texture),mask=Texture(draw.MaskTexture),
        maskBindGroupOverride=draw.MaskBindGroupOverride.ToString("x"),
        blendMode=draw.BlendMode.ToString(),sampling=draw.TextureSamplingMode.ToString(),
        textureAlpha=draw.TextureAlphaMode.ToString(),rasterOperation=draw.RasterOperation.ToString(),
        draw.HasImageEffect,draw.ExtensionId,draw.IntParam,draw.FloatParam,
        staticBufferType=draw.StaticBuffer?.GetType().FullName,dataType=draw.DataParam?.GetType().FullName
    };

    static object MaskResource(object resource)
    {
        var type=typeof(Compositor).GetNestedType("MaskBindGroupResource",BindingFlags.NonPublic)!;
        if(resource.GetType()!=type)throw new InvalidOperationException("Unexpected mask resource type");
        return new {identity=RuntimeHelpers.GetHashCode(resource),
            bindGroup=((nint)Field(resource,type,"<BindGroupPtr>k__BackingField")!).ToString("x"),
            samplingUniforms=Field(resource,type,"<SamplingUniforms>k__BackingField")};
    }

    object ObserveCompiler(Compositor compositor, SceneVisual root)
    {
        if(compositor.GetType()!=typeof(Compositor))throw new InvalidOperationException("Exact owned compositor required");
        var type=typeof(Compositor);
        var draws=(List<Compositor.CompositorDrawCall>)Field(compositor,type,"_drawCalls")!;
        var brushes=(List<GpuBrush>)Field(compositor,type,"_activeBrushes")!;
        if(draws.Count>4096 || brushes.Count>4096 || compositor.VectorVertices.Count>65536 || compositor.VectorIndices.Count>262144)
            throw new InvalidOperationException("Compiler inventory bound");
        var masks=new Dictionary<string,object>();
        foreach(string name in new[]{"_analyticMaskResourcePool","_analyticMasksToReturnToPool","_compiledSceneAnalyticMaskResources"}) {
            var values=((IEnumerable)Field(compositor,type,name)!).Cast<object>().ToArray();
            if(values.Length>4096)throw new InvalidOperationException("Mask observation bound");
            masks.Add(name,values.Select(MaskResource).ToArray());
        }
        var textureMasks=(IDictionary)Field(compositor,type,"_maskBindGroups")!;
        if(textureMasks.Count>4096)throw new InvalidOperationException("Texture mask observation bound");
        var textureMaskRows=new List<object>();
        foreach(DictionaryEntry entry in textureMasks)textureMaskRows.Add(new {texture=Texture((GpuTexture)entry.Key),resource=MaskResource(entry.Value!)});
        masks.Add("_maskBindGroups",textureMaskRows);
        var cache=Field(compositor,type,"_retainedVisualCommands")!;
        var tryGet=cache.GetType().GetMethod("TryGetValue") ?? throw new InvalidOperationException("Exact cache lookup missing");
        var visuals=new List<object>();
        void Read(SceneVisual visual,int parent)
        {
            if(visuals.Count>=512)throw new InvalidOperationException("Compiler visual inventory bound");
            var visualType=visual.GetType();
            bool exactRetained=visualType.FullName=="System.Windows.Media.ProGPU.Composition.ProGpuRetainedDrawingVisual" && visualType.Assembly==typeof(System.Windows.Media.ProGPU.ProGpuWpfWindowHost).Assembly;
            if(visualType!=typeof(ContainerVisual) && visualType!=typeof(DrawingVisual) && !exactRetained)
                throw new InvalidOperationException("Unexpected diagnostic visual type");
            int id=visuals.Count;
            object?[] args={visual,null};bool found=(bool)tryGet.Invoke(cache,args)!;
            object? cached=null;
            if(found) {
                object owner=args[1]!;var expected=type.GetNestedType("RetainedVisualCommands",BindingFlags.NonPublic);
                if(owner.GetType()!=expected)throw new InvalidOperationException("Unexpected cache type");
                // Only auto-property backing fields from the exact private cache type.
                cached=new {
                    renderContentVersion=Field(owner,expected!,"<RenderContentVersion>k__BackingField"),
                    targetWidth=Field(owner,expected!,"<TargetWidth>k__BackingField"),
                    targetHeight=Field(owner,expected!,"<TargetHeight>k__BackingField"),
                    dpiScale=Field(owner,expected!,"<DpiScale>k__BackingField"),
                    context=ContextState((SceneContext)Field(owner,expected!,"<Context>k__BackingField")!)
                };
            }
            SceneContext? live = visual is DrawingVisual drawing ? drawing.Context : null;
            if(exactRetained)
                live=(SceneContext)visual.GetType().GetProperty("Context")!.GetValue(visual)!;
            visuals.Add(new {id,parent,type=visual.GetType().FullName,visual.HasTemplate,visual.LocalRenderBounds,
                liveContext=live==null?null:ContextState(live),
                renderContentVersion=Field(visual,typeof(SceneVisual),"_renderContentVersion"),
                found,cached});
            if(visual is ContainerVisual container)foreach(var child in container.Children)Read(child,id);
        }
        Read(root,-1);
        return new {pid=Environment.ProcessId,run,renderer,index,stage=phase,elapsedSeconds=clock.Elapsed.TotalSeconds,
            assembly=type.Assembly.Location,compositor.Metrics,
            target=new {width=Field(compositor,type,"_currentWidth"),height=Field(compositor,type,"_currentHeight"),
                compositor.CurrentDpiScale,compositor.CurrentProjection,
                explicitWidth=Field(compositor,type,"_explicitRenderTargetWidth"),
                explicitHeight=Field(compositor,type,"_explicitRenderTargetHeight"),
                viewport=Field(compositor,type,"_explicitRenderTargetViewport"),
                activeClip=Field(compositor,type,"_activeClipRect"),compositor.ActiveOpacity},
            draws=draws.Select(DrawState).ToArray(),vertices=compositor.VectorVertices.ToArray(),
            indices=compositor.VectorIndices.ToArray(),brushes=brushes.ToArray(),masks,visuals};
    }
}
