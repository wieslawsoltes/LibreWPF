/* Authored dependency control, not original application/package qualification. */
#include "wgpu.h"
#include <X11/Xlib.h>
#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
extern WGPUInstance progpuWgpuCreateInstanceXlibV1(const WGPUInstanceDescriptor*, void*, int);
static void adapter_ready(WGPURequestAdapterStatus status,WGPUAdapter adapter,const char* message,void* data) {
    if(status!=WGPURequestAdapterStatus_Success){fprintf(stderr,"Adapter: %s\n",message?message:"");abort();}
    *(WGPUAdapter*)data=adapter;
}
static void device_ready(WGPURequestDeviceStatus status,WGPUDevice device,const char* message,void* data) {
    if(status!=WGPURequestDeviceStatus_Success){fprintf(stderr,"Device: %s\n",message?message:"");abort();}
    *(WGPUDevice*)data=device;
}
typedef struct {WGPUInstance instance;WGPUSurface surface;WGPUAdapter adapter;WGPUDevice device;WGPUQueue queue;Window window;} Owner;
static Owner create_owner(Display* display,int index) {
    Owner o={0};
    o.window=XCreateSimpleWindow(display,DefaultRootWindow(display),20+index*180,20,128,128,0,0,0);
    assert(o.window);XMapWindow(display,o.window);XSync(display,False);
    WGPUInstanceExtras extras={.chain={.sType=(WGPUSType)WGPUSType_InstanceExtras},.backends=WGPUInstanceBackend_GL};
    WGPUInstanceDescriptor desc={.nextInChain=&extras.chain};
    o.instance=progpuWgpuCreateInstanceXlibV1(&desc,display,0);assert(o.instance);
    WGPUSurfaceDescriptorFromXlibWindow xlib={.chain={.sType=WGPUSType_SurfaceDescriptorFromXlibWindow},.display=display,.window=o.window};
    WGPUSurfaceDescriptor surface={.nextInChain=&xlib.chain};
    o.surface=wgpuInstanceCreateSurface(o.instance,&surface);assert(o.surface);
    WGPURequestAdapterOptions request={.compatibleSurface=o.surface,.backendType=WGPUBackendType_OpenGL};
    wgpuInstanceRequestAdapter(o.instance,&request,adapter_ready,&o.adapter);assert(o.adapter);
    WGPUAdapterProperties properties={0};wgpuAdapterGetProperties(o.adapter,&properties);
    assert(properties.backendType==WGPUBackendType_OpenGL || properties.backendType==WGPUBackendType_OpenGLES);
    fprintf(stdout,"owner%d backend=%u adapter=%s\n",index,properties.backendType,properties.name);fflush(stdout);
    // This ABI borrows property strings from the live adapter.
    WGPUDeviceDescriptor device={0};wgpuAdapterRequestDevice(o.adapter,&device,device_ready,&o.device);assert(o.device);
    o.queue=wgpuDeviceGetQueue(o.device);assert(o.queue);
    WGPUSurfaceCapabilities capabilities={0};wgpuSurfaceGetCapabilities(o.surface,o.adapter,&capabilities);
    assert(capabilities.formatCount && capabilities.alphaModeCount && capabilities.presentModeCount);
    WGPUSurfaceConfiguration config={.device=o.device,.format=capabilities.formats[0],.usage=WGPUTextureUsage_RenderAttachment,
        .alphaMode=capabilities.alphaModes[0],.width=128,.height=128,.presentMode=WGPUPresentMode_Fifo};
    wgpuSurfaceConfigure(o.surface,&config);wgpuSurfaceCapabilitiesFreeMembers(capabilities);
    return o;
}
static void present(Owner* o,double red,double blue) {
    WGPUSurfaceTexture acquired={0};wgpuSurfaceGetCurrentTexture(o->surface,&acquired);
    assert(acquired.status==WGPUSurfaceGetCurrentTextureStatus_Success && acquired.texture);
    assert(wgpuTextureGetWidth(acquired.texture)==128 && wgpuTextureGetHeight(acquired.texture)==128);
    WGPUTextureView view=wgpuTextureCreateView(acquired.texture,NULL);assert(view);
    WGPUCommandEncoder encoder=wgpuDeviceCreateCommandEncoder(o->device,NULL);assert(encoder);
    WGPURenderPassColorAttachment attachment={.view=view,.depthSlice=WGPU_DEPTH_SLICE_UNDEFINED,.loadOp=WGPULoadOp_Clear,.storeOp=WGPUStoreOp_Store,.clearValue={red,0,blue,1}};
    WGPURenderPassDescriptor desc={.colorAttachmentCount=1,.colorAttachments=&attachment};
    WGPURenderPassEncoder pass=wgpuCommandEncoderBeginRenderPass(encoder,&desc);assert(pass);
    wgpuRenderPassEncoderEnd(pass);wgpuRenderPassEncoderRelease(pass);
    WGPUCommandBuffer commands=wgpuCommandEncoderFinish(encoder,NULL);assert(commands);
    wgpuQueueSubmit(o->queue,1,&commands);wgpuSurfacePresent(o->surface);wgpuDevicePoll(o->device,1,NULL);
    wgpuCommandBufferRelease(commands);wgpuCommandEncoderRelease(encoder);wgpuTextureViewRelease(view);wgpuTextureRelease(acquired.texture);
}
static void retire(Owner* o,Display* display) {
    wgpuSurfaceUnconfigure(o->surface);wgpuQueueRelease(o->queue);wgpuDeviceRelease(o->device);
    wgpuAdapterRelease(o->adapter);wgpuSurfaceRelease(o->surface);
    if(o->instance)wgpuInstanceRelease(o->instance);
    XDestroyWindow(display,o->window);XSync(display,False);memset(o,0,sizeof(*o));
}
int main(void) {
    assert(getenv("RUNNER_ENVIRONMENT") && !strcmp(getenv("RUNNER_ENVIRONMENT"),"github-hosted"));
    assert(getenv("GITHUB_REPOSITORY") && !strcmp(getenv("GITHUB_REPOSITORY"),"wieslawsoltes/LibreWPF"));
    Display* display=XOpenDisplay(NULL);assert(display && DefaultScreen(display)==0);
    assert(!progpuWgpuCreateInstanceXlibV1(NULL,NULL,0));
    assert(!progpuWgpuCreateInstanceXlibV1(NULL,display,-1));
    assert(!progpuWgpuCreateInstanceXlibV1(NULL,display,1));
    Owner first=create_owner(display,0),second=create_owner(display,1);
    present(&first,1,0);present(&second,0,1);
    wgpuInstanceRelease(first.instance);first.instance=NULL;
    present(&first,0,1);retire(&first,display);
    present(&second,1,0);retire(&second,display);
    Owner replacement=create_owner(display,2);present(&replacement,1,1);retire(&replacement,display);
    assert(XCloseDisplay(display)==0);
    puts("PASS: three owned GL contexts, five presentations, instance retirement and surviving peer; no input; pixels unqualified");
    return 0;
}
