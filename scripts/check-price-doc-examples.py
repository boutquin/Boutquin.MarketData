#!/usr/bin/env python3
"""Compile the actual reference-data and snapshot price examples against current source."""
from pathlib import Path
import re
import subprocess
import tempfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
DOC_VERSION = ET.parse(ROOT / 'Directory.Build.props').findtext('.//MinVerMinimumMajorMinor') + '.0-doccheck'
sources = []
for name, marker in [('reference-data.md', 'ExecuteAsync<PriceHistoryRequest, Bar>(priceRequest'), ('time-series-cache.md', 'AcquireReadSnapshotAsync')]:
    blocks = re.findall(r'```csharp\n(.*?)```', (ROOT / 'docs' / name).read_text(), re.S)
    selected = [b for b in blocks if marker in b]
    assert len(selected) == 1, (name, len(selected))
    code = selected[0]
    if name == 'reference-data.md':
        code = 'var range = new DateRange(from, to);\n' + code
    sources.append(code)
artifacts = ROOT / 'artifacts'
artifacts.mkdir(exist_ok=True)
with tempfile.TemporaryDirectory(prefix='price-doc-examples-', dir=artifacts) as tmp:
    directory = Path(tmp)
    # These snippets are application usage, not source files in the library.
    # Keep consumer compilation independent of ancestor library style settings.
    (directory / '.editorconfig').write_text('root = true\n')
    (directory / 'Directory.Build.props').write_text('<Project />')
    (directory / 'Directory.Packages.props').write_text('<Project />')
    project = ET.Element('Project', Sdk='Microsoft.NET.Sdk')
    props = ET.SubElement(project, 'PropertyGroup')
    for key, value in dict(TargetFramework='net10.0', Nullable='enable', ImplicitUsings='enable', TreatWarningsAsErrors='true').items():
        ET.SubElement(props, key).text = value
    group = ET.SubElement(project, 'ItemGroup')
    ET.SubElement(group, 'ProjectReference', Include=str(ROOT / 'src/Boutquin.MarketData.Abstractions/Boutquin.MarketData.Abstractions.csproj'))
    ET.ElementTree(project).write(directory / 'Examples.csproj', encoding='unicode')
    imports = '\n'.join('using ' + n + ';' for n in ['Boutquin.Domain.ValueObjects', 'Boutquin.MarketData.Abstractions.Contracts', 'Boutquin.MarketData.Abstractions.Requests', 'Boutquin.MarketData.Abstractions.ReferenceData', 'Boutquin.MarketData.Abstractions.Records', 'Boutquin.MarketData.Abstractions.Exceptions'])
    methods = '\n'.join(f'public static async Task Example{i}(IDataPipeline pipeline, DateOnly from, DateOnly to, CancellationToken ct) {{\n{code}\n}}' for i, code in enumerate(sources))
    (directory / 'Examples.cs').write_text(imports + '\npublic static class Examples {\n' + methods + '\n}\n')
    subprocess.run(['dotnet', 'build', str(directory / 'Examples.csproj'), '-c', 'Release', '-p:GeneratePackageOnBuild=false', '-p:MinVerVersionOverride=' + DOC_VERSION], cwd=ROOT, check=True)
print('PASS: both actual price documentation examples compile')
