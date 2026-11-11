from pathlib import Path
import sys, json, hashlib, struct
import numpy as np
ROOT=Path(__file__).resolve().parent
sys.path.insert(0,str(ROOT.parent/'python-libs'))
from elftools.elf.elffile import ELFFile
from capstone import Cs, CS_ARCH_ARM64, CS_MODE_LITTLE_ENDIAN

paths=[ROOT.parent/'libUE4.so',ROOT/'libUE4.so']
data=[p.read_bytes() for p in paths]
def layout(p):
    with p.open('rb') as fp:
        e=ELFFile(fp)
        return {'sections':[{k:int(s[k]) for k in ['sh_addr','sh_offset','sh_size','sh_flags']}|{'name':s.name} for s in e.iter_sections()],
                'segments':[{k:(str(s[k]) if k=='p_type' else int(s[k])) for k in ['p_type','p_offset','p_vaddr','p_filesz','p_memsz','p_flags']} for s in e.iter_segments()]}
layouts=[layout(p) for p in paths]
ranges=[]; total_ranges=0; changed_bytes=0; previous_end=-1
for o in range(0,min(map(len,data)),65536):
    a,b=data[0][o:o+65536],data[1][o:o+65536]
    if a==b:continue
    ix=np.flatnonzero(np.frombuffer(a,dtype=np.uint8)!=np.frombuffer(b,dtype=np.uint8))+o
    changed_bytes+=len(ix)
    starts=ix[np.r_[True,np.diff(ix)!=1]];ends=ix[np.r_[np.diff(ix)!=1,True]]+1
    total_ranges+=len(starts)-(1 if len(starts) and starts[0]==previous_end else 0)
    for s,e in zip(starts[:max(0,100-len(ranges))],ends[:max(0,100-len(ranges))]):
        if ranges and ranges[-1][1]==s:ranges[-1][1]=int(e)
        else:ranges.append([int(s),int(e)])
    previous_end=int(ends[-1])
def secat(pos):
    return [s['name'] for s in layouts[0]['sections'] if s['sh_offset']<=pos<s['sh_offset']+s['sh_size']]
result={'artifacts':[{'path':str(p),'size':len(b),'sha256':hashlib.sha256(b).hexdigest()} for p,b in zip(paths,data)],
        'layouts':layouts,'layout_equal':layouts[0]==layouts[1],
        'changed_bytes':changed_bytes,'range_count':total_ranges,'range_preview_limit':100,
        'ranges':[{'start':hex(s),'end':hex(e),'length':e-s,'sections':secat(s),'old_hex':data[0][s:min(e,s+128)].hex(),'new_hex':data[1][s:min(e,s+128)].hex()} for s,e in ranges]}
section_counts={}
for s in layouts[0]['sections']:
    o,n=s['sh_offset'],s['sh_size']
    if n and o+n<=min(map(len,data)):
        count=int(np.count_nonzero(np.frombuffer(data[0],dtype=np.uint8,count=n,offset=o)!=np.frombuffer(data[1],dtype=np.uint8,count=n,offset=o)))
        if count:section_counts[s['name']]=count
result['section_changed_bytes']=section_counts
(ROOT/'static-comparison.json').write_text(json.dumps(result,indent=2))
print(json.dumps({k:v for k,v in result.items() if k not in ['layouts','ranges']},indent=2))
print(json.dumps(result['ranges'][:40],indent=2))
