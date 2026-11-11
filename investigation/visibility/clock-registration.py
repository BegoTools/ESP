from pathlib import Path
import sys,struct,json,hashlib
import numpy as np
ROOT=Path.cwd();sys.path.insert(0,str(ROOT/'investigation/python-libs'))
from elftools.elf.elffile import ELFFile
from capstone import Cs,CS_ARCH_ARM64,CS_MODE_LITTLE_ENDIAN
path=ROOT/'investigation/gameloop-21518/libUE4.so';b=path.read_bytes()
with path.open('rb') as f:
 elf=ELFFile(f);loads=[(int(s['p_offset']),int(s['p_vaddr']),int(s['p_filesz']),int(s['p_flags'])) for s in elf.iter_segments() if s['p_type']=='PT_LOAD']
 section=elf.get_section_by_name('.rela.dyn');rel=np.frombuffer(b,dtype=[('offset','<u8'),('info','<u8'),('addend','<i8')],count=int(section['sh_size'])//24,offset=int(section['sh_offset']))
relative=rel[(rel['info']&0xffffffff)==1027]
resolved_relocs={int(x['offset']):int(x['addend']) for x in relative}
def va(o):
 for base,addr,n,flags in loads:
  if base<=o<base+n:return addr+o-base
def off(a):
 for base,addr,n,flags in loads:
  if addr<=a<addr+n:return base+a-addr
def exec_address(a):return any(addr<=a<addr+n and flags&1 for base,addr,n,flags in loads)
def resolved(a):
 if a in resolved_relocs:return resolved_relocs[a]
 o=off(a);return struct.unpack_from('<Q',b,o)[0] if o is not None else None
cs=Cs(CS_ARCH_ARM64,CS_MODE_LITTLE_ENDIAN)
def assembly(a,n=256):
 o=off(a);return [f'{i.address:#x}: {i.mnemonic} {i.op_str}' for i in cs.disasm(b[o:o+n],a)] if o is not None else []
names=['GetTimeSeconds','GetRealTimeSeconds','GetWorldDeltaSeconds','GetGameTimeInSeconds']
output={'path':str(path),'sha256':hashlib.sha256(b).hexdigest(),'strings':{},'candidate_execs':[]}
for name in names:
 hits=[];cursor=0
 while True:
  pos=b.find(name.encode()+b'\0',cursor)
  if pos<0:break
  cursor=pos+len(name)+1
  if pos>0 and b[pos-1]!=0:continue
  a=va(pos);refs=[]
  for row in relative[relative['addend']==a]:
   storage=int(row['offset']);near={hex(storage+delta):hex(resolved(storage+delta) or 0) for delta in range(-16,40,8)}
   candidate=resolved(storage+8);entry={'storage':hex(storage),'near':near,'next':hex(candidate or 0)}
   if candidate is not None and exec_address(candidate):
    entry['assembly']=assembly(candidate,256);output['candidate_execs'].append({'name':name,'entry':hex(candidate),'assembly':entry['assembly']})
   refs.append(entry)
  hits.append({'file_offset':hex(pos),'va':hex(a),'refs':refs})
 output['strings'][name]=hits
(ROOT/'investigation/visibility/clock-registration.json').write_text(json.dumps(output,indent=2))
print(json.dumps({'sha256': output['sha256'], 'executables': [(x['name'],x['entry']) for x in output['candidate_execs']]},indent=2))
