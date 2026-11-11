from pathlib import Path
import json,sys
sys.path.insert(0,str(Path('investigation/python-libs').resolve()))
from capstone import Cs,CS_ARCH_ARM64,CS_MODE_LITTLE_ENDIAN
cs=Cs(CS_ARCH_ARM64,CS_MODE_LITTLE_ENDIAN);d=json.loads(Path('investigation/visibility/live-reflection.json').read_text());out={}
for code in d['Code']:
 a=int(code['Rva'],16);rows=[]
 for i in cs.disasm(bytes.fromhex(code['BytesHex']),a):rows.append(f'{i.address:#x}: {i.mnemonic} {i.op_str}')
 out[code['Rva']]=rows
 if code['Rva'] in ['0x5c81228','0x5c7cce0','0x5c7c698']:print('\n'+code['Rva']+'\n'+'\n'.join(rows[:150]))
Path('investigation/visibility/alternative-runtime-bodies.json').write_text(json.dumps(out,indent=2))
for obj in d['Objects']:
 if 'AutoAim' in obj.get('Class',''):
  print('FIELDS');print('\n'.join(str((x['Name'],x['Type'],hex(x['Offset']),x['Owner'],x.get('ValueHex16'))) for x in obj['Properties'] if x['Offset']>=0x160 and x['Offset']<0x650));break
