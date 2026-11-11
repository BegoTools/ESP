from pathlib import Path
import sys, json, struct
sys.path.insert(0,str(Path(__file__).parent/'python-libs'))
import numpy as np
from elftools.elf.elffile import ELFFile
from capstone import Cs, CS_ARCH_ARM64, CS_MODE_LITTLE_ENDIAN

ROOT=Path(__file__).parent
ART=ROOT/'libUE4.so'
with ART.open('rb') as fp:
    elf=ELFFile(fp)
    sections={s.name:(int(s['sh_addr']),int(s['sh_offset']),int(s['sh_size'])) for s in elf.iter_sections()}
    loads=[(int(s['p_offset']),int(s['p_vaddr']),int(s['p_filesz'])) for s in elf.iter_segments() if s['p_type']=='PT_LOAD']
b=ART.read_bytes()
def va(off):
    for o,v,n in loads:
        if o<=off<o+n:return v+off-o
    return None
def off(addr):
    for o,v,n in loads:
        if v<=addr<v+n:return o+addr-v
    return None

targets={
 'PixUtil_GameWorld_GEngine_null':0x409f06a,
 'PixUtil_GameInstance_GEngine_null':0x3ffdd6e,
 'PersistentLevel_reflection':0x39b96b0,
 'AcknowledgedPawn_reflection':0x406b974,
 'ShaderPrecompile_GEngine_null':0x39b085c,
 'MainCity_GEngine_null':0x3b0b6e8,
}
if len(sys.argv)>1:
    targets.update({k:int(v,0) for k,v in json.loads(Path(sys.argv[1]).read_text()).items()})
target_addr={k:va(o) for k,o in targets.items()}
tv=np.array(list(target_addr.values()),dtype=np.uint64)
textva,textoff,textsize=sections['.text']
words=np.frombuffer(b,dtype='<u4',count=textsize//4,offset=textoff)
idx=np.flatnonzero((words&0x9f000000)==0x90000000)
ops=words[idx].astype(np.uint64)
imm=((ops>>29)&3)|(((ops>>5)&0x7ffff)<<2)
signed=imm.astype(np.int64);signed[signed&(1<<20)!=0]-=1<<21
pages=((textva+idx.astype(np.int64)*4)&~4095)+(signed<<12)
regs=(ops&31)
hits=[]
for d in range(1,9):
    valid=idx+d<len(words)
    nxt=words[np.minimum(idx+d,len(words)-1)].astype(np.uint64)
    match=valid&((nxt&0xff000000)==0x91000000)&(((nxt>>5)&31)==regs)
    adds=((nxt>>10)&0xfff)<<np.where((nxt>>22)&1,12,0).astype(np.uint64)
    dest=pages.astype(np.uint64)+adds
    match&=np.isin(dest,tv)
    for j in np.flatnonzero(match):
        addr=int(dest[j]);names=[k for k,v in target_addr.items() if v==addr]
        hits.append({'target':names,'target_va':hex(addr),'adrp_va':hex(textva+int(idx[j])*4),'add_va':hex(textva+int(idx[j]+d)*4),'distance':d})

relva,reloff,relsize=sections['.rela.dyn']
relocs=np.frombuffer(b,dtype=np.dtype([('offset','<u8'),('info','<u8'),('addend','<i8')]),count=relsize//24,offset=reloff)
relhits=[]
for r in relocs[np.isin(relocs['addend'],tv)]:
    value=int(r['addend']);relhits.append({'target':[k for k,v in target_addr.items() if v==value],'target_va':hex(value),'storage_va':hex(int(r['offset'])),'type':int(r['info'])&0xffffffff})
result={'targets':{k:hex(v) for k,v in target_addr.items()},'code_xrefs':hits,'relocation_xrefs':relhits,'adrp_candidates':len(idx),'scope':'All .text words; ADRP followed within 8 instructions by same-base ADD immediate, plus .rela.dyn addend equality. No data-flow or runtime validation.'}
(ROOT/'native-xrefs.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
cs=Cs(CS_ARCH_ARM64,CS_MODE_LITTLE_ENDIAN)
lines=[]
for hit in hits:
    a=int(hit['adrp_va'],16);o=off(a-96)
    lines.append('\n'+json.dumps(hit))
    for i in cs.disasm(b[o:o+256],a-96):lines.append(f'{i.address:#x}: {i.mnemonic:8} {i.op_str}')
for h in relhits:
    a=int(h['storage_va'],16);o=off(a)
    lines.append('\nDATA '+json.dumps(h))
    if o is not None:
        for delta in range(-32,96,8):lines.append(f'{a+delta:#x}: {b[o+delta:o+delta+8].hex()}  {struct.unpack_from("<Q",b,o+delta)[0]:#x}')
(ROOT/'native-xrefs-disassembly.txt').write_text('\n'.join(lines),encoding='utf-8')
print(json.dumps(result,indent=2))
