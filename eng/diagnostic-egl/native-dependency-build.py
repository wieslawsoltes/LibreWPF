import hashlib,json,os,pathlib,subprocess,time
root=pathlib.Path.cwd();artifacts=root/'artifacts';artifacts.mkdir(exist_ok=True)
assert os.environ.get('RUNNER_ENVIRONMENT')=='github-hosted'
assert os.environ.get('GITHUB_REPOSITORY')=='wieslawsoltes/LibreWPF'
started=time.monotonic()
def run(args,cwd=None):return subprocess.check_output(args,cwd=cwd,text=True).strip()
pins={'native':'33133da4ec5a0174cb21539ef2d3346f75200411','wgpu':'87576b72b37c6b78b41104eb25fc31893af94092'}
for directory,revision in pins.items():
 assert run(['git','rev-parse','HEAD'],root/directory)==revision
 assert not run(['git','status','--porcelain'],root/directory)
assert run(['git','rev-parse','HEAD'],root/'native/ffi/webgpu-headers')=='aef5e428a1fdab2ea770581ae7c95d8779984e0a'
hashes={'wgpu-xlib-owned.patch': '558d6cfbe4ce6623ffbfb5a30fff8507a0788c21cd8543c07ffd7ca7a85dc324', 'wgpu-native-xlib-entry.patch': '17bbff4129a7b29bc1f7da7774989cf42eef8593482d1e8e24174a2863e71852', 'native-surface-probe.c': '8c10804ca5ffc885f10bacad04b16e8976e6b3df2e4ed29d7c371ac6637096ba'}
for name,expected in hashes.items():assert hashlib.sha256((root/'eng/diagnostic-egl'/name).read_bytes()).hexdigest()==expected
for directory,name in [('wgpu','wgpu-xlib-owned.patch'),('native','wgpu-native-xlib-entry.patch')]:
 subprocess.run(['git','apply','--check',str(root/'eng/diagnostic-egl'/name)],cwd=root/directory,check=True)
 subprocess.run(['git','apply',str(root/'eng/diagnostic-egl'/name)],cwd=root/directory,check=True)
config=root/'native/.cargo/config.toml';assert not config.exists();config.parent.mkdir(exist_ok=True)
config.write_text('[patch."https://github.com/gfx-rs/wgpu"]\n'+''.join(f'{name} = {{ path = "{root/"wgpu"/name}" }}\n' for name in ('d3d12','naga','wgpu-core','wgpu-hal','wgpu-types')))
lock=root/'native/Cargo.lock';lock_hash=hashlib.sha256(lock.read_bytes()).hexdigest()
subprocess.run(['rustup','toolchain','install','1.99.0','--profile','minimal'],check=True)
subprocess.run(['cargo','+1.99.0','build','--release','--locked','--jobs','2'],cwd=root/'native',check=True,timeout=900)
assert hashlib.sha256(lock.read_bytes()).hexdigest()==lock_hash
library=root/'native/target/release/libwgpu_native.so';assert library.is_file()
subprocess.run(['clang','-std=c11','-Wall','-Wextra','-Werror','-I',str(root/'native/ffi'),'-I',str(root/'native/ffi/webgpu-headers'),str(root/'eng/diagnostic-egl/native-surface-probe.c'),'-L',str(library.parent),'-Wl,-rpath,'+str(library.parent),'-lwgpu_native','-lX11','-o',str(artifacts/'native-surface-probe')],check=True)
record=dict(qualified=False,inputSent=False,sourcePins=pins,patches=hashes,lockSha256=lock_hash,librarySha256=hashlib.sha256(library.read_bytes()).hexdigest(),probeSha256=hashlib.sha256((artifacts/'native-surface-probe').read_bytes()).hexdigest(),rust=run(['rustc','+1.99.0','--version']),elapsedSeconds=time.monotonic()-started)
(artifacts/'ubuntu26-native-dependency-provenance.json').write_text(json.dumps(record,indent=2)+'\n')
