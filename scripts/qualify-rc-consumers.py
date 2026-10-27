#!/usr/bin/env python3
"""Run cold, locked package consumers without changing the working consumer repositories."""
from __future__ import annotations
import argparse, base64, hashlib, json, os, re, shutil, subprocess, xml.etree.ElementTree as ET
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]

PACKAGE_IDS = tuple("Boutquin.MarketData." + name for name in (
    "Abstractions", "Cache", "Calendars", "Catalog", "Conventions", "DayCount",
    "DependencyInjection", "Normalization", "Orchestration", "Storage", "Testing", "Transport"))
ADAPTER_PIN_FILES = (
    "Directory.Packages.props",
    "tests/Boutquin.MarketData.Adapter.Tests.Integration/KernelPackageIdentityTests.cs",
)

def validate_identity_evidence(manifest, inspection):
    version = manifest["version"]
    expected = {f"{name}.{version}.{extension}" for name in PACKAGE_IDS for extension in ("nupkg", "snupkg")}
    if set(manifest["sha256"]) != expected:
        raise ValueError("Frozen inventory must contain exactly all 12 kernel packages and symbol archives")
    if (inspection.get("status") != "PASS"
            or any(inspection.get(key) != manifest[key] for key in ("version", "sourceCommit", "sha256"))):
        raise ValueError("Package inspection does not describe the frozen manifest")
    records = inspection.get("packages", [])
    if len(records) != len(PACKAGE_IDS) or {record.get("id") for record in records} != set(PACKAGE_IDS):
        raise ValueError("Runtime identity evidence must cover each of the 12 kernel assemblies exactly once")
    if any(re.fullmatch(r"[0-9a-f]{64}", record.get("dllSha256", "")) is None for record in records):
        raise ValueError("Runtime identity evidence contains an invalid DLL hash")
    return sorted(PACKAGE_IDS)

def validate_adapter_checkout(adapter):
    if not adapter.is_relative_to((ROOT / "artifacts").resolve()):
        raise ValueError("Adapter qualification may modify only an isolated checkout under this run's artifacts directory")
    top = Path(subprocess.check_output(["git", "rev-parse", "--show-toplevel"], cwd=adapter, text=True).strip()).resolve()
    if top != adapter:
        raise ValueError("Adapter path must be the root of its own isolated Git checkout")
    if subprocess.check_output(["git", "status", "--porcelain", "--untracked-files=all"], cwd=adapter, text=True).strip():
        raise ValueError("Adapter qualification requires a clean baseline checkout")
    return subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=adapter, text=True).strip()

def validate_adapter_lock_changes(adapter, changed, old_version, version, feed):
    """Allow only regenerated kernel version/hash pins in committed consumer locks."""
    signed_path = feed.parent / 'signed-packages.json'
    signed_hashes = {item['id']: item['contentHash'] for item in json.loads(signed_path.read_text())} if signed_path.exists() else {}
    for relative in changed - set(ADAPTER_PIN_FILES):
        if Path(relative).name != 'packages.lock.json':
            raise ValueError('Unexpected changed Adapter source: ' + relative)
        original = subprocess.check_output(['git', 'show', 'HEAD:' + relative], cwd=adapter, text=True)
        expected = json.loads(original.replace(old_version, version))
        actual = json.loads((adapter / relative).read_text())
        for dependencies in expected['dependencies'].values():
            for name, item in dependencies.items():
                if name in PACKAGE_IDS and 'contentHash' in item:
                    archive = feed / (name + '.' + version + '.nupkg')
                    item['contentHash'] = signed_hashes.get(name, base64.b64encode(hashlib.sha512(archive.read_bytes()).digest()).decode())
        if actual != expected:
            raise ValueError('Consumer lock changed beyond exact kernel version/hash pins: ' + relative)

def verify_restored_packages(cache, cwd, packages, version, manifest, require_all):
    resolved = set()
    for assets in cwd.rglob("project.assets.json"):
        for identity, library in json.loads(assets.read_text(encoding="utf-8"))["libraries"].items():
            name, resolved_version = identity.rsplit("/", 1)
            if library.get("type") == "package" and name in packages:
                if resolved_version != version:
                    raise ValueError("Consumer resolved a different kernel candidate: " + identity)
                resolved.add(name)
    if not resolved or (require_all and resolved != set(packages)):
        raise ValueError("Consumer restore did not resolve its required kernel package inventory")
    for package in resolved:
        actual = cache / package.lower() / version.lower() / (package.lower() + "." + version.lower() + ".nupkg")
        if not actual.is_file():
            raise ValueError("Restored candidate archive is missing: " + package)
        if hashlib.sha256(actual.read_bytes()).hexdigest() != manifest["sha256"][package + "." + version + ".nupkg"]:
            raise ValueError("Restored bytes differ: " + package)
    return sorted(resolved)

def run(command, cwd, log, env):
    with log.open('w', encoding='utf-8') as stream:
        stream.write('COMMAND: ' + repr(command) + '\n'); stream.flush()
        result = subprocess.run(command, cwd=cwd, env=env, stdout=stream, stderr=subprocess.STDOUT)
    if result.returncode:
        raise RuntimeError(f'Exit {result.returncode}: {log}')

def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('manifest',type=Path);p.add_argument('feed',type=Path)
    p.add_argument('adapter',type=Path);p.add_argument('output',type=Path)
    p.add_argument('--work-dir',type=Path,default=ROOT/'artifacts',help='Fresh consumer/cache parent beneath artifacts; existing lane directories are never reused.')
    p.add_argument('--published', action='store_true', help='Restore the published kernel only from NuGet.org')
    args=p.parse_args(); manifest=json.loads(args.manifest.read_text(encoding='utf-8')); version=manifest['version']; commit=manifest['sourceCommit']
    inspection=json.loads((args.manifest.parent/'package-inspection.json').read_text(encoding='utf-8'))
    packages=validate_identity_evidence(manifest, inspection)
    adapter=args.adapter.resolve(); baseline=validate_adapter_checkout(adapter)
    feed=args.feed.resolve(); output=args.output.resolve(); output.mkdir(exist_ok=True)
    for lane in ('packaged-kernel', 'adapter'):
        if (output/(lane+'-trx')).exists() or (output/(lane+'-locks')).exists():
            raise ValueError('Consumer evidence directories must be fresh: '+lane)
    if (output/'consumer-summary.json').exists():
        raise ValueError('Do not overwrite a completed consumer qualification')
    for name,digest in manifest['sha256'].items():
        if hashlib.sha256((feed/name).read_bytes()).hexdigest()!=digest: raise ValueError('Changed candidate: '+name)
    work=args.work_dir.resolve()
    if not work.is_relative_to((ROOT/'artifacts').resolve()):
        raise ValueError('Consumer work directory must be under artifacts')
    work.mkdir(parents=True,exist_ok=True)
    consumer=work/'package-consumer'
    if consumer.exists(): raise ValueError('Use a fresh consumer directory; do not reuse a warmed run')
    shutil.copytree(ROOT/'tests/Boutquin.MarketData.Tests.Integration',consumer,ignore=shutil.ignore_patterns('bin','obj'))
    props='<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><TreatWarningsAsErrors>true</TreatWarningsAsErrors><NoWarn>CA2007</NoWarn><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup><ItemGroup><Using Include="Boutquin.Domain.ValueObjects" /></ItemGroup></Project>'
    (consumer/'Directory.Build.props').write_text(props)
    (consumer/'Directory.Build.targets').write_text('<Project />')
    (consumer/'Directory.Packages.props').write_text('<Project />')
    project=consumer/'Boutquin.MarketData.Tests.Integration.csproj'; tree=ET.parse(project)
    versions={e.attrib['Include']:e.attrib['Version'] for e in ET.parse(ROOT/'Directory.Packages.props').iter('PackageVersion')}
    for group in tree.getroot().findall('ItemGroup'):
        for element in list(group):
            if element.tag=='ProjectReference': group.remove(element)
            elif element.tag=='PackageReference': element.set('Version', versions[element.attrib['Include']])
    group=ET.SubElement(tree.getroot(),'ItemGroup')
    for name in packages: ET.SubElement(group,'PackageReference',Include=name,Version='['+version+']')
    tree.write(project,encoding='unicode')
    (consumer/'PackageRuntimeIdentityTests.cs').write_text('''using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;
public sealed class PackageRuntimeIdentityTests {
[Fact] public void Every_package_loads_the_frozen_candidate_bytes() {
var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"runtime-identities.json")));
Assert.Equal(12, manifest.RootElement.GetArrayLength());
foreach(var item in manifest.RootElement.EnumerateArray()) {
var assembly=Assembly.Load(item.GetProperty("id").GetString()!);
Assert.Equal("2.0.0.0", assembly.GetName().Version!.ToString());
Assert.Equal("2.0.0.0", System.Diagnostics.FileVersionInfo.GetVersionInfo(assembly.Location).FileVersion);
Assert.Equal("'''+version+'+'+commit+'''", assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion);
Assert.Equal(item.GetProperty("dllSha256").GetString(),Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assembly.Location))));
}
}}
''')
    (consumer/'runtime-identities.json').write_text(json.dumps(inspection['packages']))
    ET.SubElement(group,'None',Update='runtime-identities.json',CopyToOutputDirectory='PreserveNewest');tree.write(project,encoding='unicode')
    config=output/'consumer.nuget.config'
    configuration=ET.Element('configuration');sources=ET.SubElement(configuration,'packageSources');ET.SubElement(sources,'clear')
    for key,value in ([('nuget.org','https://api.nuget.org/v3/index.json')] if args.published else [('candidate',str(feed)),('nuget.org','https://api.nuget.org/v3/index.json')]): ET.SubElement(sources,'add',key=key,value=value)
    mappings=ET.SubElement(configuration,'packageSourceMapping')
    for key,patterns in ([('nuget.org',['*'])] if args.published else [('candidate',['Boutquin.MarketData.*']),('nuget.org',['Boutquin.Storage.*','*'])]):
        source=ET.SubElement(mappings,'packageSource',key=key)
        for pattern in patterns: ET.SubElement(source,'package',pattern=pattern)
    ET.ElementTree(configuration).write(config,encoding='unicode')
    adapter_pins=ET.parse(adapter/'Directory.Packages.props')
    old_version=adapter_pins.findtext('.//MarketDataKernelVersion')
    old_commit=adapter_pins.findtext('.//MarketDataKernelCommit')
    if not old_version or not old_commit: raise ValueError('Missing Adapter baseline identity')
    pin_changes={}
    for relative in ADAPTER_PIN_FILES:
        path=adapter/relative;content=path.read_text(encoding='utf-8')
        if old_version not in content or old_commit not in content:
            raise ValueError('Unexpected Adapter baseline identity: '+relative)
        pin_changes[path]=content.replace(old_version,version).replace(old_commit,commit)
    for path,content in pin_changes.items():
        path.write_text(content,encoding='utf-8')
    (output/'adapter-pin.patch').write_bytes(subprocess.check_output(['git','diff','--','Directory.Packages.props','tests/Boutquin.MarketData.Adapter.Tests.Integration/KernelPackageIdentityTests.cs'],cwd=adapter))
    summaries=[]
    for lane,cwd,target in [('packaged-kernel',consumer,project.name),('adapter',adapter,'Boutquin.MarketData.Adapter.slnx')]:
        cache=work/('cold-'+lane);cache.mkdir();http=work/('http-'+lane);http.mkdir()
        env={**os.environ,'NUGET_PACKAGES':str(cache),'NUGET_HTTP_CACHE_PATH':str(http)}
        run(['dotnet','restore',target,'--configfile',str(config),'--no-http-cache','-p:RestorePackagesWithLockFile=true','-p:_WorkloadLibraryPacksFolder='+str(work/'absent-sdk-feed')],cwd,output/(lane+'-restore.txt'),env)
        run(['dotnet','restore',target,'--configfile',str(config),'--locked-mode','--no-http-cache','-p:_WorkloadLibraryPacksFolder='+str(work/'absent-sdk-feed')],cwd,output/(lane+'-locked-restore.txt'),env)
        run(['dotnet','test',target,'-c','Release','--no-restore','--logger','trx','--results-directory',str(output/(lane+'-trx'))],cwd,output/(lane+'-tests.txt'),env)
        run(['python3',str(ROOT/'scripts/verify-published-storage.py'),'--runtime-only','--packages',str(cache),'--outputs',str(cwd),'--report',str(output/(lane+'-storage-identities.json'))],ROOT,output/(lane+'-storage-identities.txt'),env)
        counts={'total':0,'passed':0,'failed':0,'notExecuted':0}
        for trx in (output/(lane+'-trx')).glob('*.trx'):
            counter=ET.parse(trx).find('.//{*}Counters')
            for key in counts: counts[key]+=int(counter.attrib[key])
        if not counts['total'] or counts['failed'] or counts['notExecuted']:raise ValueError(str(counts))
        resolved=verify_restored_packages(cache,cwd,packages,version,manifest,lane=='packaged-kernel')
        locks=output/(lane+'-locks');locks.mkdir()
        for lock in cwd.rglob('packages.lock.json'):
            relative=lock.relative_to(cwd);target_lock=locks/relative;target_lock.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(lock,target_lock)
        summaries.append({'lane':lane,'counts':counts,'coldCache':str(cache),'resolvedKernelPackages':resolved,'status':'PASS'})
    for name,digest in manifest['sha256'].items():
        if hashlib.sha256((feed/name).read_bytes()).hexdigest()!=digest:raise ValueError('Candidate changed during consumers')
    if subprocess.check_output(['git','rev-parse','HEAD'],cwd=adapter,text=True).strip()!=baseline:
        raise ValueError('Adapter baseline changed during qualification')
    changed=set(subprocess.check_output(['git','diff','HEAD','--name-only'],cwd=adapter,text=True).splitlines())
    validate_adapter_lock_changes(adapter, changed, old_version, version, feed)
    if not set(ADAPTER_PIN_FILES).issubset(changed) or any(path.read_text(encoding='utf-8')!=content for path,content in pin_changes.items()):
        raise ValueError('Adapter qualification changed more than the two reviewed identity pins')
    untracked=subprocess.check_output(['git','ls-files','--others','--exclude-standard'],cwd=adapter,text=True).splitlines()
    if any(Path(name).name!='packages.lock.json' for name in untracked):
        raise ValueError('Unexpected untracked Adapter source appeared during qualification')
    (output/'consumer-summary.json').write_text(json.dumps({'version':version,'sourceCommit':commit,'adapterSourceCommit':baseline,'lanes':summaries,'frozenBytesUnchanged':True},indent=2)+'\n')
    print(json.dumps(summaries))
if __name__=='__main__':main()
