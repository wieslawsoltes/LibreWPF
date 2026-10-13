"""Read the original verified WPR artifact without launching the application."""
from pathlib import Path
import hashlib,json,os,shutil,subprocess,zipfile
assert os.name=='nt' and os.environ.get('GITHUB_REPOSITORY')=='wieslawsoltes/LibreWPF'
out=Path(os.environ['RUNNER_TEMP'])/'gallery-cpu-analysis';out.mkdir(exist_ok=False)
archive=out/'original.zip'
with archive.open('xb') as f:subprocess.run(['gh','api','repos/wieslawsoltes/LibreWPF/actions/artifacts/11825165748/zip'],stdout=f,check=True,timeout=180)
assert archive.stat().st_size==94235770 and hashlib.sha256(archive.read_bytes()).hexdigest()=='d534faa7cfbd09973cbcca670814e17318081ec162da84b1bcd5d0e9243bc075'
with zipfile.ZipFile(archive) as z:
 for name in ('native-cpu-observation/gallery.etl','native-cpu-observation/receipt.json'):
  z.extract(name,out/'input')
trace=out/'input/native-cpu-observation/gallery.etl';receipt=json.loads((trace.parent/'receipt.json').read_text())
assert receipt['target']['pid']==8164 and receipt['stopExit']==0
assert hashlib.sha256(trace.read_bytes()).hexdigest()==receipt['traceSha256']=='389267c53fcaebe13a5252b2df637d47d840fda7653e5530de0a35bbfbf063e9'
src=out/'reader-source';shutil.copytree(Path(__file__).parent/'reader',src,ignore=shutil.ignore_patterns('bin','obj'))
binary=out/'reader'
with (out/'reader-build.log').open('xb') as log:
 subprocess.run(['dotnet','build',str(src/'TraceReader.csproj'),'-c','Release','--output',str(binary),
 '-p:CopyLocalLockFileAssemblies=true','-p:ImportDirectoryBuildProps=false','-p:ImportDirectoryBuildTargets=false','--nologo','-v:q'],
 stdout=log,stderr=subprocess.STDOUT,check=True,timeout=180,cwd=src)
assert (binary/'Microsoft.Diagnostics.Tracing.TraceEvent.dll').is_file()
with (out/'reader.log').open('xb') as log:
 result=subprocess.run(['dotnet',str(binary/'TraceReader.dll'),str(trace),'8164',str(out/'cpu-summary.json')],
 stdout=log,stderr=subprocess.STDOUT,timeout=120)
(out/'analysis-receipt.json').write_text(json.dumps({'diagnosticOnly':True,'qualified':False,'sourceRun':38415865442,'artifact':11825165748,
 'originalTraceSha256':receipt['traceSha256'],'traceUnchanged':hashlib.sha256(trace.read_bytes()).hexdigest()==receipt['traceSha256'],
 'applicationLaunched':False,'readerExit':result.returncode},indent=2)+'\n')
assert result.returncode==0,'Reader failed; original trace preserved'

