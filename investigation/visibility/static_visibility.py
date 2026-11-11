from pathlib import Path
import sys,json,struct,hashlib
import numpy as np
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'investigation/python-libs'))
from elftools.elf.elffile import ELFFile
from capstone import Cs,CS_ARCH_ARM64,CS_MODE_LITTLE_ENDIAN
path=ROOT/'investigation/gameloop-21518/libUE4.so'
b=path.read_bytes()
with path.open('rb') as fp:
    elf=ELFFile(fp)
    loads=[(int(s['p_offset']),int(s['p_vaddr']),int(s['p_filesz'])) for s in elf.iter_segments() if s['p_type']=='PT_LOAD']
    sec=elf.get_section_by_name('.rela.dyn')
    rel=np.frombuffer(b,dtype=[('offset','<u8'),('info','<u8'),('addend','<i8')],count=int(sec['sh_size'])//24,offset=int(sec['sh_offset']))
def va(o):
    for off,v,n in loads:
        if off<=o<off+n:return o-off+v
def off(a):
    for o,v,n in loads:
        if v<=a<v+n:return a-v+o
names=['WasRecentlyRendered','GetLastRenderTimeOnScreen','LastRenderTimeOnScreen','LastRenderTime','bRecentlyRendered','Mesh','TimeSeconds','VisibilityBasedAnimTickOption','bIsVisible','LineOfSightTo','GetActorEyesViewPoint','bOccluded','OcclusionState','IsVisibleForObserver','IsVisible']
result={'path':str(path),'sha256':hashlib.sha256(b).hexdigest(),'names':{}}
cs=Cs(CS_ARCH_ARM64,CS_MODE_LITTLE_ENDIAN)
for name in names:
    cursor=0;hits=[]
    while True:
        pos=b.find(name.encode()+b'\0',cursor)
        if pos<0:break
        cursor=pos+len(name)+1;a=va(pos);refs=[]
        if a is None:continue
        for row in rel[(rel['addend']==a)&((rel['info']&0xffffffff)==1027)]:
            address=int(row['offset']);o=off(address)
            if o is None:continue
            words=struct.unpack_from('<12Q',b,o)
            entry={'address':hex(address),'words':[hex(v) for v in words]}
            if name in ['WasRecentlyRendered','LineOfSightTo','GetLastRenderTimeOnScreen']:
                target=words[1];targetOff=off(target)
                if targetOff is not None:entry['candidate_disassembly']=[f'{i.address:#x}: {i.mnemonic} {i.op_str}' for i in cs.disasm(b[targetOff:targetOff+256],target)]
            refs.append(entry)
        hits.append({'string_va':hex(a),'refs':refs})
    result['names'][name]=hits
(Path(__file__).parent/'static-visibility.json').write_text(json.dumps(result,indent=2))
for name,hits in result['names'].items():
    print(name,[(hit['string_va'],[(r['address'],r['words'][1],r['words'][5]) for r in hit['refs']]) for hit in hits])
