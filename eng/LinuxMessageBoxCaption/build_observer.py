"""Compile only this observer against the already verified, unchanged framework closure."""
import hashlib,json,os,shutil,subprocess
from pathlib import Path
root=Path(__file__).resolve().parent
app=Path(os.environ['CAPTION_APP']).resolve(strict=True)
renderer=os.environ['CAPTION_RENDERER']
before=json.loads(Path('artifacts/ubuntu26-caption-preparation.json').read_text())
def inventory():return {str(f.relative_to(app.parent)):hashlib.sha256(f.read_bytes()).hexdigest() for f in sorted(app.parent.rglob('*')) if f.is_file()}
assert inventory()==before['outputs'] and renderer==before['renderer']
evidence=Path('artifacts/paint-tree-build');evidence.mkdir()
for name in ['LinuxMessageBoxCaption.dll','LinuxMessageBoxCaption.pdb']:shutil.copyfile(app.parent/name,evidence/('original-'+name))
with (evidence/'build.log').open('w') as log:
 result=subprocess.run(['dotnet','build',str(root/'Observer.csproj'),'-c','Release','-p:ReferenceRoot='+str(app.parent),'-p:CaptionRenderer='+renderer,'--nologo'],stdout=log,stderr=subprocess.STDOUT,timeout=120)
assert result.returncode==0,'Observer compile failed'
for name in ['LinuxMessageBoxCaption.dll','LinuxMessageBoxCaption.pdb']:shutil.copyfile(root/'bin/Release/net10.0'/name,app.parent/name)
after=inventory();assert set(after)==set(before['outputs'])
changed=[n for n in after if after[n]!=before['outputs'][n]]
assert sorted(changed)==['LinuxMessageBoxCaption.dll','LinuxMessageBoxCaption.pdb'],'Only diagnostic observer may change'
receipt=dict(qualified=False,applicationExecuted=False,inputEventsSent=0,renderer=renderer,application=str(app),originalOutputs=before['outputs'],outputs=after,changed=changed,sources={n:hashlib.sha256((root/n).read_bytes()).hexdigest() for n in ['Program.cs','Bootstrap.cs','Observer.csproj']})
(evidence/'receipt.json').write_text(json.dumps(receipt,indent=2)+'\n')
