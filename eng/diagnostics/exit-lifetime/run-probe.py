"""Three bounded owned children compare actual DirectWrite process-exit lifetime."""
import importlib.util,json,os,shutil,subprocess,sys,tempfile,time,uuid
from pathlib import Path
assert os.name=='nt' and all(os.environ.get(k)==v for k,v in {
 'GITHUB_ACTIONS':'true','RUNNER_ENVIRONMENT':'github-hosted','RUNNER_OS':'Windows',
 'GITHUB_REPOSITORY':'wieslawsoltes/LibreWPF'}.items())
repo=Path.cwd();root=Path(os.environ['CASE_ROOT']);consumer=root/'consumer';out=root/'exit-lifetime';out.mkdir(exist_ok=False)
sys.path.insert(0,str(repo/'eng'))
import showcase_idle_crash as crash
import showcase_idle_debugger as debugger
spec=importlib.util.spec_from_file_location('idle',repo/'eng/progpu-wpf-showcase-idle.py')
idle=importlib.util.module_from_spec(spec);spec.loader.exec_module(idle)
spec=importlib.util.spec_from_file_location('producer',repo/'eng/WindowsGalleryClipboardConsumer/prepare.py')
producer=importlib.util.module_from_spec(spec);spec.loader.exec_module(producer)
prepared=json.loads((consumer/'preparation.json').read_text())
assert prepared['compiled'] and prepared['head']=='c1ca1004d0d8a44e0ed527168e5e514390884879'
original=Path(prepared['output']['directory'])
project=(consumer/'GalleryClipboardApp.csproj').read_text()
a=project.index('  <ItemGroup>');b=project.index('  </ItemGroup>',a)+len('  </ItemGroup>')
project=project[:a]+'  <ItemGroup><Compile Include="ExitGlyphProbe.cs" /></ItemGroup>'+project[b:]
(out/'ExitGlyphProbe.csproj').write_text(project)
shutil.copyfile(consumer/'global.json',out/'global.json')
shutil.copyfile(Path(__file__).parent/'ExitGlyphProbe.cs',out/'ExitGlyphProbe.cs')
env=dict(os.environ);env.update(prepared['buildEnvironment'])
cmd=producer.build_command(Path(shutil.which('dotnet')),out/'ExitGlyphProbe.csproj','arm64',consumer/'NuGet.config',consumer/'packages')
with (out/'build.log').open('xb') as log:
 result=subprocess.run(cmd,cwd=out,env=env,stdout=log,stderr=subprocess.STDOUT,timeout=180)
assert result.returncode==0,'Private diagnostic compilation failed'
binary=out/'bin/Release/net10.0-windows'
def inventory():
 result={}
 for p in binary.rglob('*.dll'):
  if p.name=='ExitGlyphProbe.dll':continue
  relative=p.relative_to(binary);source=original/relative
  assert source.is_file() and producer.sha(p)==producer.sha(source),'Substituted product/dependency '+str(relative)
  result[str(relative)]=producer.sha(p)
 return result
before=inventory()
assert all(n in before for n in ['PresentationCore.dll','PresentationFramework.dll','WindowsBase.dll','ProGPU.Wpf.Interop.dll','DirectWriteForwarder.dll','progpu_native.dll'])
app=binary/'ExitGlyphProbe.exe';helper=Path(os.environ['RUNNER_TEMP'])/'showcase-native-debugger/ShowcaseNativeDebugger.exe'
assert debugger.machine(app)==debugger.machine(helper)==0xAA64
receipt={'diagnosticOnly':True,'qualified':False,'sourceHead':prepared['head'],'productSourcesChanged':False,
 'plan':'One pre-exit control and two actual process-exit glyph exports, without/with exact module process-lifetime pin. Each owned child has a45-second outer bound. No window, GPU rendering, input, clipboard, GC/JIT override, or product patch.',
 'productsBefore':before,'appSha256':producer.sha(app),'debuggerSha256':producer.sha(helper),'cases':[]}
for mode in ['before-exit','exit-unpinned','exit-pinned']:
 case=out/mode;case.mkdir();image=None
 raw_root=Path(tempfile.mkdtemp(prefix='exit-lifetime-owned-raw-'))
 report={'mode':mode,'diagnosticOnly':True,'qualified':False};start=time.monotonic()
 try:
  assert app.read_bytes().count(b'ExitGlyphProbe.dll\0')==1,'Private apphost binding differs'
  image=binary/('ShowcaseIdle-'+uuid.uuid4().hex+'.exe');shutil.copyfile(app,image)
  assert crash.digest(image)==crash.digest(app)
  report['privateApphostBinding']='ExitGlyphProbe.dll';report['ownedImage']=image.name
  event_path=case/'native-debugger-event.json'
  raw=Path(tempfile.mkdtemp(prefix='exit-lifetime-first-av-'))/'first-chance-av.dmp'
  assert raw.name=='first-chance-av.dmp' and raw.is_absolute() and not raw.exists()
  assert not any(raw.parent.iterdir()) and not any(raw_root.iterdir()) and raw.parent!=raw_root
  assert event_path.is_absolute() and not event_path.exists() and len(image.name)==49
  child_env=env.copy()
  for name in idle.CONFLICTING_MODES:child_env.pop(name,None)
  assert not any(child_env.get(n) for n in ['DOTNET_JitDisasm','DOTNET_GCStress','COMPlus_GCStress'])
  child_env['EXIT_PROBE_OUTPUT']=str(case);child_env['EXIT_PROBE_MODE']=mode
  code,timed=idle.run_child([str(helper),str(image),str(raw_root),str(event_path),'--first-chance-av',str(raw)],binary,child_env,case,timeout=45,outcome=report)
  report.update(exitCode=code,timedOut=timed)
  if timed:raise RuntimeError('Owned child exceeded45seconds')
  event=debugger.load_event(event_path,0xAA64);report['nativeEvent']=event
  assert event['exitCode']==code and event['processId']!=report['childProcessId'] and event['firstChanceAvEnabled']
  report['firstChance']=debugger.collect_first_chance_av(raw,case,event,0xAA64)
  report['unhandled']={'captured':event['captured']}
  if event['captured']:
   original_dump=raw_root/(image.name+'.'+str(event['processId'])+'.dmp')
   report['unhandled']['validated']=debugger.correlate_exception(original_dump,event)
   shutil.copyfile(original_dump,case/'validated-unhandled.dmp')
 except Exception as error:report['error']=type(error).__name__+': '+str(error)
 finally:
  report['cleanupErrors']=[]
  if image is not None:
   try:image.unlink()
   except OSError as error:report['cleanupErrors'].append(str(error))
  report['elapsedSeconds']=time.monotonic()-start
  report['productsUnchanged']=inventory()==before
  for name in ['before-exit','callback-entered','callback-completed']:
   path=case/(name+'.json')
   if path.is_file():report[name]=json.loads(path.read_text())
  (case/'receipt.json').write_text(json.dumps(report,indent=2)+'\n');receipt['cases'].append(report)
receipt['productsAfter']=inventory();receipt['productsUnchanged']=receipt['productsAfter']==before
(out/'probe-receipt.json').write_text(json.dumps(receipt,indent=2)+'\n')
assert receipt['productsUnchanged'] and all(not r.get('error') and not r['cleanupErrors'] and r['productsUnchanged'] for r in receipt['cases'])
assert all(receipt['cases'][i]['exitCode']==0 and receipt['cases'][i].get('callback-completed',{}).get('exactSnapshot') for i in [0,2]),'A paired control failed; no cause claim is admitted'
print(json.dumps([{'mode':r['mode'],'exitCode':r['exitCode'],'moduleAtCallback':r.get('callback-entered',{}).get('moduleAtEntry'),'snapshotCompleted':bool(r.get('callback-completed'))} for r in receipt['cases']]))
