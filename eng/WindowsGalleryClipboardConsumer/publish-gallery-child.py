"""Publish only the owned driver's child identity for a separate trace observer."""
import hashlib, json, os
from pathlib import Path
assert all(os.environ.get(k)==v for k,v in {
    'GITHUB_ACTIONS':'true','RUNNER_ENVIRONMENT':'github-hosted','RUNNER_OS':'Windows',
    'GITHUB_REPOSITORY':'wieslawsoltes/LibreWPF'}.items())
root=Path(os.environ['CASE_ROOT'])/'consumer';p=root/'run-windows.py';data=p.read_bytes()
original=json.loads((root/'preparation.json').read_text());assert hashlib.sha256(data).hexdigest()==original['inputs']['run-windows.py']
text=data.decode().replace('\r\n','\n');old='            result["pid"] = process.pid';assert text.count(old)==1
new=old+'\n            with (args.output / "diagnostic-child.json").open("x") as child:\n                json.dump({"pid": process.pid, "app": str(args.app.resolve()), "sha256": result["appSha256"]}, child)'
(root/'run-windows.original.py.txt').write_bytes(data);p.write_text(text.replace(old,new))
(root/'stack-observation-source.json').write_text(json.dumps({'diagnosticOnly':True,'qualified':False,'originalDriverSha256':hashlib.sha256(data).hexdigest(),'observedDriverSha256':hashlib.sha256(p.read_bytes()).hexdigest(),'applicationUnmodified':True,'originalDeadlineAndAssertionsUnchanged':True},indent=2)+'\n')
