/* Authored dependency regression control; not source application qualification.
 * The original owned-Xlib lifetime probe remains byte-for-byte unchanged. */
#define _GNU_SOURCE
#define main original_surface_control_main
#include "native-surface-probe.c"
#undef main
#include <dlfcn.h>
#include <limits.h>
#include <stdint.h>

#define WIDTH 16u
#define HEIGHT 12u
#define ROW_BYTES 256u
static void mapped(WGPUBufferMapAsyncStatus status, void* data) {
    assert(status == WGPUBufferMapAsyncStatus_Success);
    *(int*)data = 1;
}
static WGPUTexture texture(Owner* o, uint32_t samples, WGPUTextureUsageFlags usage) {
    WGPUTextureDescriptor descriptor = {.usage=usage,.dimension=WGPUTextureDimension_2D,
        .size={WIDTH,HEIGHT,1},.format=WGPUTextureFormat_RGBA8Unorm,
        .mipLevelCount=1,.sampleCount=samples};
    WGPUTexture result=wgpuDeviceCreateTexture(o->device,&descriptor);assert(result);return result;
}
static void clear_destination(Owner* o,WGPUTextureView destination) {
    WGPUCommandEncoder encoder=wgpuDeviceCreateCommandEncoder(o->device,NULL);assert(encoder);
    WGPURenderPassColorAttachment attachment={.view=destination,.depthSlice=WGPU_DEPTH_SLICE_UNDEFINED,
        .loadOp=WGPULoadOp_Clear,.storeOp=WGPUStoreOp_Store,.clearValue={0,0,1,1}};
    WGPURenderPassDescriptor descriptor={.colorAttachmentCount=1,.colorAttachments=&attachment};
    WGPURenderPassEncoder pass=wgpuCommandEncoderBeginRenderPass(encoder,&descriptor);assert(pass);
    wgpuRenderPassEncoderEnd(pass);wgpuRenderPassEncoderRelease(pass);
    WGPUCommandBuffer command=wgpuCommandEncoderFinish(encoder,NULL);assert(command);
    wgpuQueueSubmit(o->queue,1,&command);wgpuDevicePoll(o->device,1,NULL);
    wgpuCommandBufferRelease(command);wgpuCommandEncoderRelease(encoder);
}
static int check_case(Owner* o,WGPURenderPipeline pipeline,WGPUTexture msaa,WGPUTextureView msaa_view,
    WGPUTexture destination,WGPUTextureView destination_view,unsigned index) {
    (void)msaa;
    const uint32_t left=index==1?2:0,top=index==1?4:0,width=index==1?8:WIDTH,height=4;
    const int draw=index<2;
    WGPUBufferDescriptor buffer_descriptor={.usage=WGPUBufferUsage_CopyDst|WGPUBufferUsage_MapRead,
        .size=ROW_BYTES*HEIGHT,.mappedAtCreation=0};
    WGPUBuffer buffer=wgpuDeviceCreateBuffer(o->device,&buffer_descriptor);assert(buffer);
    WGPUCommandEncoder encoder=wgpuDeviceCreateCommandEncoder(o->device,NULL);assert(encoder);
    WGPURenderPassColorAttachment attachment={.view=msaa_view,.resolveTarget=destination_view,
        .depthSlice=WGPU_DEPTH_SLICE_UNDEFINED,.loadOp=WGPULoadOp_Clear,.storeOp=WGPUStoreOp_Discard,
        .clearValue=index==1?(WGPUColor){0,0,1,1}:(WGPUColor){1,0,0,1}};
    WGPURenderPassDescriptor descriptor={.colorAttachmentCount=1,.colorAttachments=&attachment};
    WGPURenderPassEncoder pass=wgpuCommandEncoderBeginRenderPass(encoder,&descriptor);assert(pass);
    if(draw) {
        wgpuRenderPassEncoderSetPipeline(pass,pipeline);
        wgpuRenderPassEncoderSetScissorRect(pass,left,top,width,height);
        wgpuRenderPassEncoderDraw(pass,3,1,0,0);
    }
    wgpuRenderPassEncoderEnd(pass);wgpuRenderPassEncoderRelease(pass);
    WGPUImageCopyTexture source={.texture=destination,.aspect=WGPUTextureAspect_All};
    WGPUImageCopyBuffer target={.buffer=buffer,.layout={.bytesPerRow=ROW_BYTES,.rowsPerImage=HEIGHT}};
    WGPUExtent3D extent={WIDTH,HEIGHT,1};wgpuCommandEncoderCopyTextureToBuffer(encoder,&source,&target,&extent);
    WGPUCommandBuffer command=wgpuCommandEncoderFinish(encoder,NULL);assert(command);
    wgpuQueueSubmit(o->queue,1,&command);
    int complete=0;wgpuBufferMapAsync(buffer,WGPUMapMode_Read,0,ROW_BYTES*HEIGHT,mapped,&complete);
    wgpuDevicePoll(o->device,1,NULL);assert(complete);
    const uint8_t* pixels=wgpuBufferGetConstMappedRange(buffer,0,ROW_BYTES*HEIGHT);assert(pixels);
    unsigned mismatches=0;
    for(uint32_t y=0;y<HEIGHT;y++)for(uint32_t x=0;x<WIDTH;x++) {
        int green=draw && x>=left && x<left+width && y>=top && y<top+height;
        const uint8_t expected[4]={!green && index!=1?255:0,green?255:0,!green && index==1?255:0,255};
        const uint8_t* actual=pixels+y*ROW_BYTES+x*4;
        if(memcmp(actual,expected,4)) {
            if(!mismatches)fprintf(stdout,"case%u first_mismatch=%u,%u actual=%u,%u,%u,%u expected=%u,%u,%u,%u\n",
                index,x,y,actual[0],actual[1],actual[2],actual[3],expected[0],expected[1],expected[2],expected[3]);
            mismatches++;
        }
    }
    fprintf(stdout,"case%u samples=4 pixels=%u mismatches=%u draw=%d scissor=%u,%u,%u,%u\n",
        index,WIDTH*HEIGHT,mismatches,draw,left,top,width,height);fflush(stdout);
    wgpuBufferUnmap(buffer);wgpuBufferRelease(buffer);wgpuCommandBufferRelease(command);wgpuCommandEncoderRelease(encoder);
    return mismatches!=0;
}
int main(int argc,char** argv) {
    assert(argc==2);
    assert(getenv("RUNNER_ENVIRONMENT") && !strcmp(getenv("RUNNER_ENVIRONMENT"),"github-hosted"));
    assert(getenv("GITHUB_REPOSITORY") && !strcmp(getenv("GITHUB_REPOSITORY"),"wieslawsoltes/LibreWPF"));
    Dl_info loaded={0};assert(dladdr((void*)(uintptr_t)&wgpuCreateInstance,&loaded) && loaded.dli_fname);
    char actual[PATH_MAX],expected[PATH_MAX];assert(realpath(loaded.dli_fname,actual) && realpath(argv[1],expected));
    assert(!strcmp(actual,expected));printf("loaded_runtime=%s\n",actual);fflush(stdout);
    Display* display=XOpenDisplay(NULL);assert(display && DefaultScreen(display)==0);
    Owner owner=create_owner(display,0);
    const char* code="@vertex fn vs_main(@builtin(vertex_index) i:u32)->@builtin(position) vec4<f32>{var p=array<vec2<f32>,3>(vec2<f32>(-1,-1),vec2<f32>(3,-1),vec2<f32>(-1,3));return vec4<f32>(p[i],0,1);}@fragment fn fs_main()->@location(0) vec4<f32>{return vec4<f32>(0,1,0,1);}";
    WGPUShaderModuleWGSLDescriptor wgsl={.chain={.sType=WGPUSType_ShaderModuleWGSLDescriptor},.code=code};
    WGPUShaderModuleDescriptor shader_descriptor={.nextInChain=&wgsl.chain};
    WGPUShaderModule shader=wgpuDeviceCreateShaderModule(owner.device,&shader_descriptor);assert(shader);
    WGPUColorTargetState color={.format=WGPUTextureFormat_RGBA8Unorm,.writeMask=WGPUColorWriteMask_All};
    WGPUFragmentState fragment={.module=shader,.entryPoint="fs_main",.targetCount=1,.targets=&color};
    WGPURenderPipelineDescriptor pipeline_descriptor={.vertex={.module=shader,.entryPoint="vs_main"},
        .primitive={.topology=WGPUPrimitiveTopology_TriangleList,.frontFace=WGPUFrontFace_CCW,.cullMode=WGPUCullMode_None},
        .multisample={.count=4,.mask=~0u},.fragment=&fragment};
    WGPURenderPipeline pipeline=wgpuDeviceCreateRenderPipeline(owner.device,&pipeline_descriptor);assert(pipeline);
    WGPUTexture msaa=texture(&owner,4,WGPUTextureUsage_RenderAttachment);
    WGPUTexture destination=texture(&owner,1,WGPUTextureUsage_RenderAttachment|WGPUTextureUsage_CopySrc);
    assert(wgpuTextureGetSampleCount(msaa)==4 && wgpuTextureGetSampleCount(destination)==1);
    WGPUTextureView msaa_view=wgpuTextureCreateView(msaa,NULL),destination_view=wgpuTextureCreateView(destination,NULL);
    assert(msaa_view && destination_view);clear_destination(&owner,destination_view);
    int failures=0;for(unsigned i=0;i<3;i++)failures+=check_case(&owner,pipeline,msaa,msaa_view,destination,destination_view,i);
    wgpuTextureViewRelease(msaa_view);wgpuTextureViewRelease(destination_view);wgpuTextureRelease(msaa);wgpuTextureRelease(destination);
    wgpuRenderPipelineRelease(pipeline);wgpuShaderModuleRelease(shader);retire(&owner,display);assert(XCloseDisplay(display)==0);
    printf("%s: three authored MSAA4 passes, complete576pixel comparison, scoped draws and subsequent full clear; no input\n",failures?"FAIL":"PASS");
    return failures?1:0;
}
