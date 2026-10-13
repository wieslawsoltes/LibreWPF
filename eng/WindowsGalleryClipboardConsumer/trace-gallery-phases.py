"""Recompile only the private consumer with phase observations; not acceptance."""
import hashlib, json, os, subprocess
from pathlib import Path
assert all(os.environ.get(k)==v for k,v in {
    'GITHUB_ACTIONS':'true','RUNNER_ENVIRONMENT':'github-hosted','RUNNER_OS':'Windows',
    'GITHUB_REPOSITORY':'wieslawsoltes/LibreWPF'}.items())
root=Path(os.environ['CASE_ROOT'])/'consumer'
receipt=json.loads((root/'preparation.json').read_text())
source=root/'GalleryClipboardApp.cs';original=source.read_bytes()
assert hashlib.sha256(original).hexdigest()==receipt['inputs'][source.name]
text=original.decode('utf-8').replace('\r\n','\n');edits=[]
def replace(old,new,count=1):
    global text
    assert text.count(old)==count,(old,text.count(old))
    text=text.replace(old,new);edits.append({'original':old,'replacement':new,'count':count})
replace('int phase = 0;','int phase = 0;\n        int observedPhase = -1;\n        void Trace(string stage) => Console.Error.WriteLine($"GALLERY_PHASE {clock.Elapsed.TotalSeconds:F6} {stage}");')
replace('PrepareForeground(host.NativeWindowHandle.Handle);','PrepareForeground(host.NativeWindowHandle.Handle);\n                    Trace("foreground-confirmed");')
replace('expected = Rgb(source);','Trace("source-rgb-start");\n                    expected = Rgb(source);\n                    Trace("source-rgb-complete");')
for name,stage,count in [('Copy Image to Clipboard','copy',1),('Paste Image from Clipboard','paste',2)]:
    line=f'OriginalButton(page, "{name}").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));'
    replace(line,f'Trace("{stage}-start");\n                    {line}\n                    Trace("{stage}-complete");',count)
replace('Verify(retained);','Trace("verify-start");\n                    Verify(retained);\n                    Trace("verify-complete");')
replace('image.BringIntoView();','Trace("bring-into-view-start");\n                    image.BringIntoView();\n                    Trace("bring-into-view-complete");')
replace('if (phase == 0)','if (observedPhase != phase) { observedPhase = phase; Trace($"phase={phase} frame={host.PresentedFrameCount}"); }\n                if (phase == 0)')
replace('if (host.PresentedFrameCount <= afterFrame && phase == 1) return;','if (host.PresentedFrameCount <= afterFrame && phase == 1) return;\n                    Trace($"new-frame={host.PresentedFrameCount}");')
replace('Point a = image.PointToScreen(new Point());','Trace("image-arranged");\n                    Point a = image.PointToScreen(new Point());')
replace('void End(Exception? failure)\n        {','void End(Exception? failure)\n        {\n            Trace("end-start");')
(root/'GalleryClipboardApp.original.cs.txt').write_bytes(original);source.write_text(text)
env=os.environ.copy();env.update(receipt['buildEnvironment'])
with (root/'phase-build.log').open('xb') as log:
    built=subprocess.run(receipt['buildCommand'],cwd=root,env=env,stdout=log,stderr=subprocess.STDOUT,timeout=300)
record={'diagnosticOnly':True,'qualified':False,'originalSourceSha256':hashlib.sha256(original).hexdigest(),
        'diagnosticSourceSha256':hashlib.sha256(source.read_bytes()).hexdigest(),'edits':edits,'buildExit':built.returncode,
        'productAssembliesUnmodified':True,'originalSourceActionsAndDeadlineUnchanged':True}
(root/'phase-observation.json').write_text(json.dumps(record,indent=2)+'\n')
raise SystemExit(built.returncode)
