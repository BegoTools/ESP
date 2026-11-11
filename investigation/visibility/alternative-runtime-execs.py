from pathlib import Path
import json,sys
sys.path.insert(0,str(Path('investigation/python-libs').resolve()))
from capstone import Cs,CS_ARCH_ARM64,CS_MODE_LITTLE_ENDIAN
cs=Cs(CS_ARCH_ARM64,CS_MODE_LITTLE_ENDIAN);d=json.loads(Path('investigation/visibility/live-reflection.json').read_text());out={}
for code in d['Code']:
 a=int(code['Rva'],16);rows=[]
 for i in cs.disasm(bytes.fromhex(code['BytesHex']),a):
  rows.append(f'{i.address:#x}: {i.mnemonic} {i.op_str}')
  if i.mnemonic=='ret':break
 out[code['Rva']]=rows;print(code['Rva']);print('\n'.join(rows))
Path('investigation/visibility/alternative-runtime-execs.json').write_text(json.dumps(out,indent=2))
for obj in d['Objects']:
 if 'AutoAim' in obj.get('Class',''):
  print('\nCOMP',obj['Address'],obj['Class']);print(json.dumps(obj['Properties'],indent=2))
  break
