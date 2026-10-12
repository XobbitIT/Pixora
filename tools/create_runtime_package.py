"""Create a clean RC user runtime from a verified runtime kit and fresh DLLs.

Stage -> test the staged assemblies in an external headless test host -> finish.
No source, tests, reviews, screenshots or user configuration enter the user ZIP.
"""
import argparse, hashlib, json, os, re, shutil, struct, subprocess, zipfile
from pathlib import Path
from verify_package import verify

def digest(data): return hashlib.sha256(data).hexdigest()
def text(path):
    b=path.read_bytes(); return b.decode('utf-16' if b.startswith((b'\xff\xfe',b'\xfe\xff')) else 'utf-8-sig')
def save(path,obj): path.write_text(json.dumps(obj,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
def main():
    p=argparse.ArgumentParser();p.add_argument('action',choices=['stage','check','finish']);p.add_argument('--stage',type=Path,required=True)
    p.add_argument('--runtime-base',type=Path);p.add_argument('--runtime-sha');p.add_argument('--publish',type=Path)
    p.add_argument('--version',required=True);p.add_argument('--commit',required=True);p.add_argument('--tag',required=True)
    p.add_argument('--core-log',type=Path);p.add_argument('--wpf-log',type=Path);p.add_argument('--output',type=Path)
    p.add_argument('--test-host',type=Path);p.add_argument('--render-dir',type=Path);p.add_argument('--receipt',type=Path)
    a=p.parse_args();root=Path(__file__).resolve().parents[1]
    assert re.fullmatch('[0-9a-f]{40}',a.commit)
    head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=root,text=True).strip();assert head==a.commit
    assert not subprocess.check_output(['git','status','--porcelain','--untracked-files=no'],cwd=root,text=True).strip(),'Tracked source changed'
    assert subprocess.check_output(['git','rev-parse',a.tag+'^{commit}'],cwd=root,text=True).strip()==a.commit
    if a.action=='stage':
        assert not a.stage.exists(),'Never overlay an old runtime stage';assert a.runtime_base and a.publish and a.runtime_sha
        assert digest(a.runtime_base.read_bytes())==a.runtime_sha,'Runtime kit SHA mismatch'
        with zipfile.ZipFile(a.runtime_base) as kit:
            old=json.loads(kit.read('Build_Manifest.json'));verify(a.runtime_base,old['version'])
            a.stage.mkdir(parents=True)
            for info in kit.infolist():
                n=info.filename
                if '/' not in n and not info.is_dir() and n.lower().endswith(('.dll','.exe')) and not n.endswith('.Tests.dll'):
                    (a.stage/n).write_bytes(kit.read(n))
            for name in ['Pixora.exe','Pixora.dll','CanvasForge.Core.dll','SkiaSharp.dll']:
                shutil.copy2(a.publish/name,a.stage/name)
            assert (a.publish/'runtimes/win-x64/native/libSkiaSharp.dll').read_bytes()==(a.stage/'libSkiaSharp.dll').read_bytes()
            (a.stage/'Pixora.deps.json').write_bytes(kit.read('Pixora.deps.json').replace(old['version'].encode(),a.version.encode()))
            (a.stage/'Pixora.runtimeconfig.json').write_bytes(kit.read('Pixora.runtimeconfig.json'))
        shutil.copytree(root/'release/Licenses',a.stage/'Licenses');shutil.copy2(root/'release/Quick_Start.md',a.stage/'Quick_Start.md')
        runtime=json.loads(text(a.stage/'Pixora.runtimeconfig.json'))['runtimeOptions'];assert 'framework' not in runtime and 'frameworks' not in runtime
        for name in ['Pixora.exe','coreclr.dll','libSkiaSharp.dll']:
            data=(a.stage/name).read_bytes();offset=struct.unpack_from('<I',data,0x3c)[0];assert data[offset:offset+4]==b'PE\0\0' and struct.unpack_from('<H',data,offset+4)[0]==0x8664
        save(a.stage/'Build_Manifest.json',dict(artifactKind='user',version=a.version,commit=a.commit,tag=a.tag,localChanges=False,inGameTest=False,
            selfContained=True,runtime=runtime['includedFrameworks'],runtimeBase=dict(version=old['version'],sha256=a.runtime_sha),nativeClipboardSmoke=False,
            packagedCoreChecksPassed=False,packagedWpfChecksPassed=False,sourceFilesSha256={},filesSha256={},accuracyCriteriaChanged=False,
            scope='Offline headless checks; RC Rust test is performed by the user.'))
        print('STAGED clean user runtime',a.stage)
    elif a.action=='check':
        assert a.test_host and a.core_log and a.wpf_log and a.render_dir and a.receipt
        hashes={n:digest((a.stage/n).read_bytes()) for n in ['Pixora.dll','CanvasForge.Core.dll']}
        assert all(digest((a.test_host/n).read_bytes())==h for n,h in hashes.items()),'Test host DLL does not match runtime stage'
        env=os.environ.copy();env.update(PIXORA_NATIVE_CLIPBOARD_SMOKE='0',DOTNET_EnableHWIntrinsic='0',DOTNET_TieredCompilation='0')
        versions={}
        for name in hashes:
            env['PIXORA_PACKAGE_DLL']=str((a.stage/name).resolve())
            info=subprocess.check_output(['pwsh','-NoProfile','-Command','[System.Diagnostics.FileVersionInfo]::GetVersionInfo($env:PIXORA_PACKAGE_DLL).ProductVersion'],env=env,text=True).strip()
            assert info==a.version+'+'+a.commit,'DLL version or informational commit mismatch: '+name+' '+info
            versions[name]=info
        for assembly,log,args in [('CanvasForge.Core.Tests.dll',a.core_log,[]),('CanvasForge.App.Tests.dll',a.wpf_log,[str(a.render_dir.resolve())])]:
            result=subprocess.run(['dotnet',str((a.test_host/assembly).resolve()),*args],cwd=root,env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
            log.write_bytes(result.stdout);assert result.returncode==0,'Packaged DLL tests failed: '+str(log)
        assert all(digest((a.stage/n).read_bytes())==h==digest((a.test_host/n).read_bytes()) for n,h in hashes.items()),'DLL changed during tests'
        save(a.receipt,dict(version=a.version,commit=a.commit,dllsSha256=hashes,productVersions=versions,
            coreLogSha256=digest(a.core_log.read_bytes()),wpfLogSha256=digest(a.wpf_log.read_bytes()),nativeClipboardSmoke=False))
        print('PASS exact packaged DLLs, informational commit and bound headless test receipt')
    else:
        assert a.core_log and a.wpf_log and a.output
        assert a.receipt,'A bound test receipt from the check step is required'
        receipt=json.loads(text(a.receipt));assert receipt['commit']==a.commit and receipt['version']==a.version
        assert all(digest((a.stage/n).read_bytes())==h for n,h in receipt['dllsSha256'].items()),'Runtime changed after tests'
        assert receipt['coreLogSha256']==digest(a.core_log.read_bytes()) and receipt['wpfLogSha256']==digest(a.wpf_log.read_bytes()),'Tests do not match receipt'
        core=re.search(r'ALL (\d+) TESTS PASSED',text(a.core_log));wpf=re.search(r'ALL (\d+) WPF UI CHECKS PASSED',text(a.wpf_log));assert core and wpf
        assert not re.search(r'^FAIL\b',text(a.core_log),re.M) and not re.search(r'^FAIL\b',text(a.wpf_log),re.M)
        manifest=json.loads(text(a.stage/'Build_Manifest.json'));assert manifest['commit']==a.commit and manifest['version']==a.version
        manifest.update(coreTests=int(core[1]),wpfChecks=int(wpf[1]),packagedCoreChecksPassed=True,packagedWpfChecksPassed=True,
            testEvidenceSha256=dict(core=digest(a.core_log.read_bytes()),wpf=digest(a.wpf_log.read_bytes())))
        manifest['filesSha256']={str(f.relative_to(a.stage)).replace('\\','/'):digest(f.read_bytes()) for f in sorted(a.stage.rglob('*')) if f.is_file() and f.name!='Build_Manifest.json'}
        save(a.stage/'Build_Manifest.json',manifest);assert not a.output.exists(),'Never overwrite a released ZIP'
        with zipfile.ZipFile(a.output,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=6) as z:
            for f in sorted(a.stage.rglob('*')):
                if f.is_file():z.write(f,str(f.relative_to(a.stage)).replace('\\','/'))
        result=verify(a.output,a.version,commit=a.commit);result.update(sha256=digest(a.output.read_bytes()),bytes=a.output.stat().st_size,coreTests=int(core[1]),wpfChecks=int(wpf[1]),inGameTest=False)
        save(a.output.with_suffix('.evidence.json'),result);a.output.with_suffix('.sha256.txt').write_text(result['sha256']+'  '+a.output.name+'\n',encoding='ascii')
        print(json.dumps(result,ensure_ascii=False))

if __name__=='__main__':main()
