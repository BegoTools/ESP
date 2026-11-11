from pathlib import Path
import sys,json,struct
import numpy as np
ROOT=Path(__file__).resolve().parent
sys.path.insert(0,str(ROOT.parent/'python-libs'))
from elftools.elf.elffile import ELFFile
names=['LocalPlayers','EncryptedLocalPlayers','OwningGameInstance','GameInstance','PlayerController','Player']
result=[]
for p in [ROOT.parent/'libUE4.so',ROOT/'libUE4.so']:
    b=p.read_bytes()
    with p.open('rb') as f:
        e=ELFFile(f);seg=[(int(s['p_offset']),int(s['p_vaddr']),int(s['p_filesz'])) for s in e.iter_segments() if s['p_type']=='PT_LOAD'];sec=e.get_section_by_name('.rela.dyn');rel=np.frombuffer(b,dtype=[('offset','<u8'),('info','<u8'),('addend','<i8')],count=int(sec['sh_size'])//24,offset=int(sec['sh_offset']))
    def va(o):
        for off,v,n in seg:
            if off<=o<off+n:return o-off+v
    def off(a):
        for o,v,n in seg:
            if v<=a<v+n:return a-v+o
    item={'path':str(p),'names':{}}
    for name in names:
        cursor=0;refs=[];pattern=name.encode()+b'\0'
        while True:
            i=b.find(pattern,cursor)
            if i<0:break
            a=va(i);cursor=i+len(pattern)
            for r in rel[(rel['addend']==a)&((rel['info']&0xffffffff)==1027)]:
                addr=int(r['offset']);o=off(addr)
                if o is not None:refs.append({'descriptor':hex(addr),'words':[hex(t) for t in struct.unpack_from('<8Q',b,o)],'hex64':b[o:o+64].hex()})
        item['names'][name]=refs
    result.append(item)
(ROOT/'offline-localplayer-route.json').write_text(json.dumps(result,indent=2))
for name in names:
    print(name)
    for j,item in enumerate(result):print(j,[(r['descriptor'],r['words'][5]) for r in item['names'][name] if name not in ['PlayerController','Player'] or r['words'][5] in ['0x30','0x38','0x40','0x520','0x530']])
