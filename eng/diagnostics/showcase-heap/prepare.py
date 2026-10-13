"""Stage only the whole-qualified original Showcase packages for one diagnostic."""
import importlib.util,json,os,subprocess,zipfile
from pathlib import Path
assert os.name=='nt' and all(os.environ.get(k)==v for k,v in {
 'GITHUB_ACTIONS':'true','RUNNER_ENVIRONMENT':'github-hosted','RUNNER_OS':'Windows',
 'GITHUB_REPOSITORY':'wieslawsoltes/LibreWPF'}.items())
head='044a1461019c52fb9fd747e096036d7763a3fe3f';build_id=38326178889;docs_id=38326178966
repo=Path.cwd();spec=importlib.util.spec_from_file_location('producer',repo/'eng/WindowsGalleryClipboardConsumer/prepare-ci.py')
ci=importlib.util.module_from_spec(spec);spec.loader.exec_module(ci)
assert subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()==head
out=repo/'artifacts/glyph-heap-diagnostic';out.mkdir(parents=True,exist_ok=False)
def keep(name,data):
 (out/name).write_text(json.dumps(data,indent=2)+'\n');return data
fields='databaseId,name,headSha,status,conclusion,jobs'
build=keep('build.json',ci.gh_json('run','view',str(build_id),'--repo',ci.REPOSITORY,'--json',fields))
docs=keep('docs.json',ci.gh_json('run','view',str(docs_id),'--repo',ci.REPOSITORY,'--json',fields))
artifact=keep('artifact.json',ci.gh_json('api',f'repos/{ci.REPOSITORY}/actions/artifacts/11781641580'))
ci.validate_ci_producer(build,docs,artifact,head,build_id,docs_id)
assert 0<artifact['size_in_bytes']<2*1024**3
archive=out/'packages.zip'
with archive.open('xb') as f:
 subprocess.run(['gh','api',f'repos/{ci.REPOSITORY}/actions/artifacts/11781641580/zip'],stdout=f,check=True,timeout=300)
assert archive.stat().st_size==artifact['size_in_bytes'] and ci.prepare.sha(archive)==artifact['digest'].removeprefix('sha256:')
dest=repo/'artifacts/packages/Release/NonShipping';dest.mkdir(parents=True,exist_ok=False)
with zipfile.ZipFile(archive) as z:
 assert all(not Path(i.filename).is_absolute() and '..' not in Path(i.filename).parts for i in z.infolist())
 z.extractall(dest)
keep('plan.json',{'diagnosticOnly':True,'qualified':False,'source':head,'build':build_id,'docs':docs_id,
 'scope':'One unchanged original package Showcase idle sequence with image-scoped full PageHeap and original native debugger; diagnostic only; original120-second outer deadline',
 'heapTool':'Microsoft GFlags, unique child image only','sourceHash':ci.prepare.sha(repo/'src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/Media/GlyphRun.cs'),
 'reference':'https://learn.microsoft.com/en-us/windows-hardware/drivers/debugger/gflags-and-pageheap','causationProven':False})

original=repo/'eng/progpu-wpf-windows-native-mil-showcase.ps1'
source=original.read_text(encoding='utf-8-sig')
marker='Assert-RequestedRendererMode $appHost'
assert source.count(marker)==1
prefix=source.split(marker)[0]
finalizer=source.rsplit('\n}\nfinally {',1)[1]
prepared=prefix+'$appHost | Set-Content -LiteralPath (Join-Path $repoRoot "artifacts/glyph-heap-diagnostic/app-path.txt") -Encoding utf8\n}\nfinally {'+finalizer
path=repo/'eng/prepare-original-showcase-diagnostic.ps1'
assert not path.exists();path.write_text(prepared,encoding='utf-8')
keep('preparation-source.json',{'originalSha256':ci.prepare.sha(original),'preparedSha256':ci.prepare.sha(path),'scope':'Original package app preparation and exact package-asset checks only; no original acceptance or other application modes invoked'})
