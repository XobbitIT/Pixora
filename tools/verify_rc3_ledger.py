"""Verify RC2 test identities and intentional test-file changes against RC3 ledger."""
import argparse,json,re,subprocess
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--core-log',type=Path,required=True);p.add_argument('--wpf-log',type=Path,required=True);a=p.parse_args()
root=Path(__file__).resolve().parents[1]
baseline=json.loads((root/'Testing/RC2_Test_Baseline.json').read_text(encoding='utf-8-sig'))
ledger=json.loads((root/'Testing/RC3_Test_Changes_Ledger.json').read_text(encoding='utf-8-sig'))
for name,log in [('Core',a.core_log),('WPF',a.wpf_log)]:
    text=log.read_text(encoding='utf-8-sig');assert not re.search(r'^FAIL\b',text,re.M),f'{name} contains failed tests'
    assert 'ALL ' in text and ' PASSED' in text,f'{name} did not complete'
    actual={line[5:] for line in text.splitlines() if line.startswith('PASS ')}
    missing=set(baseline[name])-actual
    approved=set(ledger['removedTests'])|set(ledger['renamedTests'])
    assert missing<=approved,f'{name}: unreviewed missing tests {sorted(missing-approved)}'
changed=subprocess.check_output(['git','diff','--name-only','--','tests'],cwd=root,text=True).splitlines()
assert set(changed)<=set(ledger['modifiedTestFiles']),f'Unreviewed modified test files: {set(changed)-set(ledger["modifiedTestFiles"])}'
print('PASS baseline identities, complete suites, no failed cases, and reviewed test-file ledger')
