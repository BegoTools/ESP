from pathlib import Path
import json,sys
sys.path.insert(0,str(Path('investigation/python-libs').resolve()))
from capstone import Cs,CS_ARCH_ARM64,CS_MODE_LITTLE_ENDIAN
cs=Cs(CS_ARCH_ARM64,CS_MODE_LITTLE_ENDIAN);d=json.loads(Path('investigation/visibility/live-reflection.json').read_text())
for code in d['Code']:
 if code['Rva']!='0x5c82d48':continue
 rows=[f'{i.address:#x}: {i.mnemonic} {i.op_str}' for i in cs.disasm(bytes.fromhex(code['BytesHex']),int(code['Rva'],16))]
 Path('investigation/visibility/alternative-raycast-runtime.json').write_text(json.dumps({'rva':code['Rva'],'assembly':rows},indent=2))
 print('\n'.join(rows[:240]));break
else:print('Code absent', [c['Rva'] for c in d['Code']])
