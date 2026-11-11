from pathlib import Path
import sys,json,struct
import numpy as np
ROOT=Path(__file__).resolve().parent
sys.path.insert(0,str(ROOT.parent/'python-libs'))
from elftools.elf.elffile import ELFFile
names=['AcknowledgedPawn','PlayerCameraManager','RootComponent','RelativeLocation','Health','HealthMax','TeamID','PlayerName','CameraCache','POV','FOV','CapsuleComponent','CapsuleHalfHeight','bIsAI','VehicleCommon','HP','HPMax','Fuel','FuelMax','DefineID','Count','bCanBePickUp','bHasBeenPickedUp','bHidden','Levels','PersistentLevel','NetDriver','ServerConnection','PlayerController']
all=[]
for p in [ROOT.parent/'libUE4.so',ROOT/'libUE4.so']:
    b=p.read_bytes()
    with p.open('rb') as f:
        e=ELFFile(f);segs=[(int(s['p_offset']),int(s['p_vaddr']),int(s['p_filesz'])) for s in e.iter_segments() if s['p_type']=='PT_LOAD']
        sec=e.get_section_by_name('.rela.dyn');rel=np.frombuffer(b,dtype=np.dtype([('offset','<u8'),('info','<u8'),('addend','<i8')]),count=int(sec['sh_size'])//24,offset=int(sec['sh_offset']))
        sections=[{'name':s.name,'offset':int(s['sh_offset']),'size':int(s['sh_size']),'flags':int(s['sh_flags'])} for s in e.iter_sections()]
    rel_by_offset=np.sort(rel,order='offset')
    def va(o):
        for off,v,n in segs:
            if off<=o<off+n:return o-off+v
        return None
    def off(a):
        for o,v,n in segs:
            if v<=a<v+n:return a-v+o
        return None
    item={'path':str(p),'sections':sections,'names':{}}
    for name in names:
        pattern=name.encode()+b'\0';cursor=0;hits=[]
        while True:
            i=b.find(pattern,cursor)
            if i<0:break
            addr=va(i);cursor=i+len(pattern)
            refs=[]
            for r in rel[(rel['addend']==addr)&((rel['info']&0xffffffff)==1027)]:
                a=int(r['offset']);o=off(a)
                if o is not None:
                    nested=rel_by_offset[np.searchsorted(rel_by_offset['offset'],a):np.searchsorted(rel_by_offset['offset'],a+96)]
                    refs.append({'address':hex(a),'hex96':b[o:o+96].hex(),'words':[hex(v) for v in struct.unpack_from('<12Q',b,o)],'relocations':[{'at':hex(int(t['offset'])-a),'addend':hex(int(t['addend']))} for t in nested]})
            hits.append({'string_va':hex(addr),'refs':refs})
        item['names'][name]=hits
    all.append(item)
(ROOT/'static-reflection-params.json').write_text(json.dumps(all,indent=2))
for name in names:
    print(name)
    for j,item in enumerate(all):
        refs=[r for h in item['names'][name] for r in h['refs']]
        print(j,'total',len(refs),'matching examples',[(r['address'],r['words'][5:7]) for r in refs if r['words'][5] in ['0x528','0x548','0x208','0x1e4','0xe60','0xe64','0x998','0x960','0x520','0x24','0x9f8','0x5f8','0x610','0x350','0x354','0x438','0x43c','0xc08','0x440','0x38','0x78','0x30']][:12])
