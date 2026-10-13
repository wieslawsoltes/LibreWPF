"""Bounded diagnostic export probe using exact whole-qualified product payloads."""
import hashlib,importlib.util,json,os,shutil,subprocess,time,uuid
from pathlib import Path
assert os.name=='nt' and all(os.environ.get(k)==v for k,v in {
 'GITHUB_ACTIONS':'true','RUNNER_ENVIRONMENT':'github-hosted','RUNNER_OS':'Windows',
 'GITHUB_REPOSITORY':'wieslawsoltes/LibreWPF'}.items())
root=Path(os.environ['CASE_ROOT']);consumer=root/'consumer';out=root/'glyph-lifetime';out.mkdir(exist_ok=False)
spec=importlib.util.spec_from_file_location('producer',Path.cwd()/'eng/WindowsGalleryClipboardConsumer/prepare.py')
producer=importlib.util.module_from_spec(spec);spec.loader.exec_module(producer)
prepared=json.loads((consumer/'preparation.json').read_text())
assert prepared['compiled'] and prepared['head']=='1fb4f1e90fc1c833ac7c063cc6cd93e4c6134640'
original=Path(prepared['output']['directory'])
project=(consumer/'GalleryClipboardApp.csproj').read_text()
a=project.index('  <ItemGroup>');b=project.index('  </ItemGroup>',a)+len('  </ItemGroup>')
project=project[:a]+'  <ItemGroup><Compile Include="GlyphLifetimeProbe.cs" /></ItemGroup>'+project[b:]
(out/'GlyphLifetimeProbe.csproj').write_text(project)
shutil.copyfile(consumer/'global.json',out/'global.json')
shutil.copyfile(Path(__file__).parent/'GlyphLifetimeProbe.cs',out/'GlyphLifetimeProbe.cs')
env=dict(os.environ);env.update(prepared['buildEnvironment'])
cmd=producer.build_command(Path(shutil.which('dotnet')),out/'GlyphLifetimeProbe.csproj','arm64',consumer/'NuGet.config',consumer/'packages')
with (out/'build.log').open('xb') as log:
 result=subprocess.run(cmd,cwd=out,env=env,stdout=log,stderr=subprocess.STDOUT,timeout=180)
assert result.returncode==0,'Private diagnostic compilation failed'
binary=out/'bin/Release/net10.0-windows'
def inventory():
 result={}
 for p in binary.rglob('*.dll'):
  if p.name=='GlyphLifetimeProbe.dll':continue
  relative=p.relative_to(binary);source=original/relative
  assert source.is_file() and producer.sha(p)==producer.sha(source),'Substituted product/dependency '+str(relative)
  result[str(relative)]=producer.sha(p)
 return result
before=inventory()
assert all(n in before for n in ['PresentationCore.dll','PresentationFramework.dll','WindowsBase.dll','ProGPU.Wpf.Interop.dll','DirectWriteForwarder.dll','progpu_native.dll'])
app=binary/('ShowcaseIdle-'+uuid.uuid4().hex+'.exe');shutil.copyfile(binary/'GlyphLifetimeProbe.exe',app)
debugger=Path(os.environ['RUNNER_TEMP'])/'showcase-native-debugger/ShowcaseNativeDebugger.exe'
raw=out/'raw';first=out/'first-chance';raw.mkdir();first.mkdir()
env['GLYPH_PROBE_OUTPUT']=str(out)
env['DOTNET_JitDisasm']='*TryGetPortableNativeGlyphRun*';env['DOTNET_JitDisasmWithCodeBytes']='1'
receipt={'diagnosticOnly':True,'qualified':False,'sourceHead':prepared['head'],'originalShowcaseNotReplaced':True,
 'plan':'Three ten-second original GlyphRun export phases: retained face, retained face with forced GC, fresh face with forced GC; no window/render/input/clipboard.',
 'productsBefore':before,'appSha256':producer.sha(app),'debuggerSha256':producer.sha(debugger)}
start=time.monotonic()
try:
 with (out/'stdout.log').open('xb') as stdout,(out/'stderr.log').open('xb') as stderr:
  proc=subprocess.Popen([str(debugger),str(app),str(raw),str(out/'native-debugger-event.json'),'--first-chance-av',str(first/'first-chance-av.dmp')],stdout=stdout,stderr=stderr,env=env)
  try:receipt['exit']=proc.wait(timeout=125)
  except subprocess.TimeoutExpired:
   proc.kill();proc.wait(timeout=10);receipt['timeout']=True
finally:
 receipt['elapsedSeconds']=time.monotonic()-start
 receipt['productsAfter']=inventory();receipt['productsUnchanged']=receipt['productsAfter']==before
 (out/'probe-receipt.json').write_text(json.dumps(receipt,indent=2)+'\n')
assert receipt.get('exit')==0 and (out/'glyph-lifetime.json').exists(),'Probe failed; preserve fault and original output'

