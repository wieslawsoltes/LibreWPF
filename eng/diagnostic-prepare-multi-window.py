"""Compile original source against exact public packages; no application execution."""
import hashlib,json,os,pathlib,shutil,subprocess,time,zipfile
repo=pathlib.Path.cwd(); root=pathlib.Path(os.environ['RUNNER_TEMP'])/'original-multi-window'
root.mkdir(); (root/'Fonts').mkdir()
record=dict(qualified=False,inputSent=False,applicationExecuted=False,version='0.1.0-preview.69',sources={},packages={},outputs={})
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
try:
    files={
        'Program.cs':(repo/'original/src/ProGPU.Wpf.MultiWindowSmokeHarness/Program.cs','41311b4b6498d8d5f8f47bfbd8283a5fccd3b5799c65336e90386a6fc9de23b7'),
        'GlyphComputeReadback.cs':(repo/'original/src/ProGPU.Wpf.MultiWindowSmokeHarness/GlyphComputeReadback.cs','d29fac45e01933379623095461aa890a2999f68c62c0b8f4333e96d25aa55167'),
        'Fonts/Inter-Regular.ttf':(repo/'font-source/src/ProGPU.Fonts.Inter/Fonts/Inter-Regular.ttf','40d692fce188e4471e2b3cba937be967878f631ad3ebbbdcd587687c7ebe0c82'),
        'Fonts/LICENSE.txt':(repo/'font-source/src/ProGPU.Fonts.Inter/Fonts/LICENSE.txt','262481e844521b326f5ecd053e59b98c8b2da78c8ee1bdbb6e8174305e54935a')}
    for name,(source,expected) in files.items():
        assert sha(source)==expected,name
        shutil.copyfile(source,root/name);record['sources'][name]=expected
    (root/'global.json').write_text('{"sdk":{"version":"10.0.201","rollForward":"disable"}}')
    (root/'NuGet.config').write_text('<configuration><packageSources><clear/><add key="NuGet" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>')
    (root/'MultiWindow.csproj').write_text('''<Project Sdk="LibreWPF.Sdk/0.1.0-preview.69">
<PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework><UseWPF>true</UseWPF><ImplicitUsings>disable</ImplicitUsings><Nullable>enable</Nullable><AllowUnsafeBlocks>true</AllowUnsafeBlocks><ProGpuWpfEnablePortableBootstrap>false</ProGpuWpfEnablePortableBootstrap></PropertyGroup>
<ItemGroup><Content Include="Fonts/*" CopyToOutputDirectory="PreserveNewest" /></ItemGroup></Project>''')
    env=os.environ.copy()
    for key in list(env):
        if key.startswith(('PROGPU','ProGpu','NUGET_','MSBuildSDK')):env.pop(key)
    env.update(NUGET_PACKAGES=str(root/'packages'),NUGET_HTTP_CACHE_PATH=str(root/'http-cache'),DOTNET_CLI_HOME=str(root/'cli-home'),MSBUILDDISABLENODEREUSE='1')
    command=['dotnet','build','MultiWindow.csproj','-c','Release','-r','linux-arm64','-m:1','-nr:false','-p:UseSharedCompilation=false','-p:RestorePackagesPath='+str(root/'packages'),'-o',str(root/'application')]
    record['command']=command;start=time.monotonic()
    with (root/'build.log').open('w') as log:result=subprocess.run(command,cwd=root,env=env,stdout=log,stderr=subprocess.STDOUT,timeout=600)
    record.update(buildExitCode=result.returncode,buildSeconds=time.monotonic()-start)
    assert result.returncode==0,'Build failed; original build log retained on runner'
    inputs=json.loads((repo/'eng/WindowsModalDesktop/public-inputs.json').read_text())['packages']
    for archive in sorted((root/'packages').rglob('*.nupkg')):
        package=archive.parent.parent.name;version=archive.parent.name
        record['packages'][package]={'version':version,'sha256':sha(archive)}
        if package.startswith(('progpu','librewpf','librewinforms')):
            expected=inputs[package];assert version==expected['version'] and sha(archive)==expected['sha256'],package
    out=root/'application'
    app=out/'MultiWindow.dll';record['emittedFiles']=[str(p.relative_to(root)) for p in root.rglob('MultiWindow.dll')];assert app.is_file(),'Expected explicit application output absent'
    for path in sorted(out.rglob('*')):
        if path.is_file():record['outputs'][str(path.relative_to(out))]=sha(path)
    for name,expected in record['sources'].items():assert sha(root/name)==expected
    record.update(compiled=True,output=str(out),application=str(app))
    (repo/'artifacts/ubuntu26-multi-window-preparation.json').write_text(json.dumps(record,indent=2)+'\n')
    with open(os.environ['GITHUB_ENV'],'a') as stream:stream.write('ORIGINAL_MULTI_WINDOW_APP='+str(app)+'\n')
except BaseException as error:
    record['error']=type(error).__name__+': '+str(error)
    (repo/'artifacts/ubuntu26-multi-window-preparation.json').write_text(json.dumps(record,indent=2)+'\n')
    raise
