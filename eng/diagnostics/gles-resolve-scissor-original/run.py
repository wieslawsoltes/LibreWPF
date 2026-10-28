"""One original-runtime negative control. No framework application or native input."""
from pathlib import Path
import hashlib, io, json, os, re, signal, subprocess, time, zipfile
import stage_frozen
ROOT=Path(__file__).resolve().parent

def require(value,message):
    if not value: raise ValueError(message)

def digest(path): return hashlib.sha256(path.read_bytes()).hexdigest()

def stage(archive,output):
    config=json.loads((ROOT/'consumers.json').read_text())
    selected=config['renderers']['ManagedPortable']
    require(selected['artifact']['workflow_run']['head_sha']==config['buildHead'] and
            selected['artifact']['workflow_run']['id']==config['buildRun'] and
            selected['frozen']['producerHead']==config['producerHead'],'Frozen identity mismatch')
    raw,original=stage_frozen.read_artifact(archive,selected)
    require(not output.exists(),'Fresh owned output required');output.mkdir()
    with zipfile.ZipFile(io.BytesIO(raw)) as z:
        for name in ('libwgpu_native.so','wgpu-native-linux.json'):
            (output/name).write_bytes(z.read('application/'+name))
    return dict(artifact=selected['artifact'],producerHead=config['producerHead'],
                originalReceiptSha256=hashlib.sha256(original).hexdigest(),
                outputs={p.name:digest(p) for p in output.iterdir()})

def run(command,log,timeout):
    start=time.monotonic()
    with log.open('xb') as stream:
        process=subprocess.Popen(command,stdout=stream,stderr=subprocess.STDOUT,start_new_session=True)
        try: code=process.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            os.killpg(process.pid,signal.SIGKILL);process.wait();raise
    return dict(command=command,exitCode=code,seconds=time.monotonic()-start,logSha256=digest(log))

def main():
    require(os.environ.get('RUNNER_ENVIRONMENT')=='github-hosted' and
            os.environ.get('GITHUB_REPOSITORY')=='wieslawsoltes/LibreWPF' and
            os.environ.get('GITHUB_RUN_ATTEMPT')=='1','Disposable first-attempt LibreWPF host required')
    evidence=ROOT/'evidence';require(not evidence.exists(),'Retain prior evidence');evidence.mkdir()
    record=dict(inputSent=False,frameworkApplicationExecuted=False,probeExecuted=False,qualified=False)
    try:
        pin=json.loads((ROOT/'inputs.json').read_text())
        require(all(digest(ROOT/n)==h for n,h in pin['files'].items()),'Diagnostic inputs changed')
        head=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()
        require(head==os.environ['EXPECTED_HEAD'],'Exact diagnostic checkout required')
        record.update(head=head,inputs=pin)
        selected=json.loads((ROOT/'consumers.json').read_text())['renderers']['ManagedPortable']
        archive=evidence/'original-artifact.zip'
        with archive.open('xb') as output:
            downloaded=subprocess.run(['gh','api','repos/wieslawsoltes/LibreWPF/actions/artifacts/'+str(selected['artifact']['id'])+'/zip'],stdout=output,timeout=180)
        require(downloaded.returncode==0,'Exact original download failed')
        runtime=evidence/'runtime';record['stage']=stage(archive,runtime)
        library=runtime/'libwgpu_native.so';require(digest(library)==pin['runtimeSha256'],'Original runtime changed')
        record['runtimeReceipt']=json.loads((runtime/'wgpu-native-linux.json').read_text())
        elf=library.read_bytes()[:20]
        require(record['runtimeReceipt']['rid']=='linux-arm64' and elf[:6]==b'\x7fELF\x02\x01' and int.from_bytes(elf[18:20],'little')==183,'Exact ELF64 ARM64 required')
        require(os.uname().machine=='aarch64','Actual ARM64 runner required')
        executable=evidence/'resolve-scissor-original'
        command=['clang','-std=c11','-Wall','-Wextra','-Werror',str(ROOT/'native-resolve-scissor-probe.c'),'-I',str(ROOT),'-L',str(runtime),'-lwgpu_native','-lX11','-ldl','-Wl,-rpath,'+str(runtime),'-o',str(executable)]
        record['compile']=run(command,evidence/'compile.log',60)
        require(record['compile']['exitCode']==0,'Original ABI probe compile failed')
        record['executableSha256']=digest(executable)
        command=['xvfb-run','-a','timeout','--signal=TERM','--kill-after=2s','60s',str(executable),str(library)]
        record['probeExecuted']=True
        record['probe']=run(command,evidence/'probe.log',75)
        output=(evidence/'probe.log').read_text()
        cases=re.findall(r'case(\d) samples=4 pixels=192 mismatches=(\d+) draw=',output)
        record['cases']=[dict(index=int(i),mismatches=int(n)) for i,n in cases]
        record['originalControlFailed']=record['probe']['exitCode']==1
        require('loaded_runtime='+str(library.resolve()) in output,'Actual loaded runtime not witnessed')
        require(len(cases)==3 and [int(i) for i,n in cases]==[0,1,2],'Complete three-pass observations required')
        require(record['originalControlFailed'] and int(cases[0][1])>0 and int(cases[1][1])>0 and int(cases[2][1])==0,
                'Expected original resolve failure was not established')
        record['negativeControlObserved']=True
        # Preserve receipts and original GitHub artifact identity; archive bytes remain retained externally.
        archive.unlink()
    except BaseException as error:
        record['error']=repr(error);raise
    finally:
        (evidence/'result.json').write_text(json.dumps(record,indent=2)+'\n')
if __name__=='__main__':main()
