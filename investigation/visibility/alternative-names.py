import sys,json,re
from pathlib import Path
ROOT=Path.cwd();sys.path.insert(0,str(ROOT/'investigation/python-libs'))
from elftools.elf.elffile import ELFFile
path=ROOT/'investigation/gameloop-21518/libUE4.so'
with path.open('rb') as f:
 elf=ELFFile(f);sec=elf.get_section_by_name('.rodata');print('rodata',hex(int(sec['sh_offset'])),hex(int(sec['sh_size'])));data=sec.data();base=int(sec['sh_addr'])
keys=re.compile(r'CanSee|Visible|Visibility|Occlu|TraceResult|AimTarget|AimAssist|AutoAim|TargetData|EnemyInfo|SightResult|HitResultCache',re.I)
hits=[]
for m in re.finditer(rb'[A-Za-z_][A-Za-z_0-9:]{4,150}\x00',data):
 text=m.group()[:-1].decode()
 if keys.search(text):hits.append({'rva':hex(base+m.start()),'text':text})
Path('investigation/visibility/alternative-names.json').write_text(json.dumps(hits,indent=2))
print('count',len(hits));print('\n'.join(x['rva']+' '+x['text'] for x in hits if re.search(r'(Aim|CanSee|TraceResult|TargetData|EnemyInfo|SightResult)',x['text'],re.I)))
