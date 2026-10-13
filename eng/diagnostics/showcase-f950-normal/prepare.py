"""Recover the exact known-failed Showcase inputs for diagnosis; never qualify them."""
from pathlib import Path
import json,os,subprocess,hashlib,zipfile
assert os.name=='nt' and all(os.environ.get(k)==v for k,v in {'GITHUB_ACTIONS':'true','RUNNER_ENVIRONMENT':'github-hosted','RUNNER_OS':'Windows','GITHUB_REPOSITORY':'wieslawsoltes/LibreWPF'}.items())
repo=Path.cwd();source=Path(__file__).resolve().parent;out=repo/'artifacts/glyph-heap-diagnostic';out.mkdir(parents=True,exist_ok=True)
assert {p.name for p in out.iterdir()}=={'gflags.json'}
expected=json.loads((source/'expected.json').read_text());head=expected['source']
assert subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()==head
def gh(endpoint):return json.loads(subprocess.check_output(['gh','api',endpoint],text=True,timeout=60))
def keep(name,value):(out/name).write_text(json.dumps(value,indent=2)+'\n');return value
base='repos/wieslawsoltes/LibreWPF/actions/'
build=keep('build.json',gh(base+'runs/38314985874'))
assert build['head_sha']==head and build['status']=='completed' and build['conclusion']=='failure'
jobs=keep('jobs.json',gh(base+'runs/38314985874/jobs?per_page=100'))['jobs']
assert len(jobs)==16 and all(j['status']=='completed' for j in jobs)
failed=[j for j in jobs if j['conclusion']!='success'];assert len(failed)==1 and failed[0]['id']==115038887397 and failed[0]['conclusion']=='failure'
artifact=keep('artifact.json',gh(base+'artifacts/11778006469'))
assert artifact['name']=='librewpf-ci-packages-'+head and not artifact['expired'] and artifact['workflow_run']['id']==38314985874 and artifact['workflow_run']['head_sha']==head
assert artifact['size_in_bytes']==658058079 and artifact['digest']=='sha256:d8183dc17b8907786edb770a454aafc7618d58ba43569c791292b674da011132'
archive=out/'packages.zip'
with archive.open('xb') as f:subprocess.run(['gh','api',base+'artifacts/11778006469/zip'],stdout=f,check=True,timeout=300)
assert archive.stat().st_size==artifact['size_in_bytes'] and hashlib.sha256(archive.read_bytes()).hexdigest()==artifact['digest'][7:]
dest=repo/'artifacts/packages/Release/NonShipping';dest.mkdir(parents=True,exist_ok=False)
with zipfile.ZipFile(archive) as z:
 assert all(not Path(i.filename).is_absolute() and '..' not in Path(i.filename).parts for i in z.infolist())
 z.extractall(dest)
keep('plan.json',dict(diagnosticOnly=True,qualified=False,source=head,build=38314985874,failedProducerDeliberatelyRetained=True,normalConsumerOrReleaseStaging=False,scope='Recover original failed apphost and allfive recorded managed payload hashes before one original120-second image-scoped PageHeap diagnostic. Any byte mismatch forbids application execution.',expected=expected))
original=repo/'eng/progpu-wpf-windows-native-mil-showcase.ps1'
s=original.read_text(encoding='utf-8-sig');marker='Assert-RequestedRendererMode $appHost';assert s.count(marker)==1
prepared=s.split(marker)[0]+'$appHost | Set-Content -LiteralPath (Join-Path $repoRoot "artifacts/glyph-heap-diagnostic/app-path.txt") -Encoding utf8\n}\nfinally {'+s.rsplit('\n}\nfinally {',1)[1]
# Preserve the exact original intermediate/output path as well as source and
# packages, because deterministic PDB identity can include generated file paths.
# This is an unused directory on a disposable hosted runner, never local state.
import tempfile
original_root=Path(expected['originalSmokeRoot'])
assert original_root.parent.samefile(Path(tempfile.gettempdir())) and not original_root.exists()
root_line=next(line for line in prepared.splitlines() if line.startswith('$smokeRoot = '))
assert '$([guid]::NewGuid().ToString(' in root_line
prepared=prepared.replace(root_line,"$smokeRoot = '"+str(original_root)+"'",1)
path=repo/'eng/prepare-original-failed-showcase.ps1';assert not path.exists();path.write_text(prepared)
keep('preparation-source.json',dict(originalSha256=hashlib.sha256(original.read_bytes()).hexdigest(),preparedSha256=hashlib.sha256(path.read_bytes()).hexdigest(),applicationSourceChanged=False,scope='Original build and exact package-asset checks; stop before application execution'))
