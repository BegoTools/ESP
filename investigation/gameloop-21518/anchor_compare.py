from pathlib import Path
import sys,json,struct
ROOT=Path(__file__).resolve().parent
sys.path.insert(0,str(ROOT.parent/'python-libs'))
from elftools.elf.elffile import ELFFile
from capstone import Cs,CS_ARCH_ARM64,CS_MODE_LITTLE_ENDIAN
paths=[ROOT.parent/'libUE4.so',ROOT/'libUE4.so']
anchors={'GameWorld':(0xcec53ec,0x100),'GameInstance':(0xcec5fe8,0xd0),'WorldContexts':(0xa858440,0x100),'NamesStateAccessor':(0x8be1bfc,0x300),'NamesEntryAccessor':(0x8be2ed8,0xc0),'LevelActorsGetter':(0xa98c454,0x90)}
strings=['PixUI-UE  Warn        PixUtil::GameWorld GEngine is nullptr','PixUtil::GameInstance GEngine is nullptr','GetPersistentLevelURLByWorld','GetLevelActors']
result=[]
cs=Cs(CS_ARCH_ARM64,CS_MODE_LITTLE_ENDIAN)
for p in paths:
    b=p.read_bytes()
    with p.open('rb') as fp:
        elf=ELFFile(fp);loads=[(int(s['p_offset']),int(s['p_vaddr']),int(s['p_filesz'])) for s in elf.iter_segments() if s['p_type']=='PT_LOAD']
        relocs=[{'offset':hex(r['r_offset']),'type':r['r_info_type'],'addend':hex(r['r_addend'])} for r in elf.get_section_by_name('.rela.dyn').iter_relocations() if r['r_offset'] in [0xee956c8,0xdb8f968,0xdb8f970,0xdbce540]]
    def off(a):
        for o,v,n in loads:
            if v<=a<v+n:return a-v+o
        return None
    item={'path':str(p),'relocs':relocs,'strings':{s:hex(b.find(s.encode())) for s in strings},'anchors':{}}
    for name,(a,n) in anchors.items():
        o=off(a);raw=b[o:o+n]
        item['anchors'][name]={'address':hex(a),'bytes':raw.hex(),'assembly':[f'{i.address:#x}: {i.mnemonic} {i.op_str}' for i in cs.disasm(raw,a)]}
    result.append(item)
(ROOT/'anchor-comparison.json').write_text(json.dumps(result,indent=2))
for item in result:
    print(item['path']);print(item['relocs']);print(item['strings'])
    for name,v in item['anchors'].items():
        print(name);print('\n'.join(v['assembly']))
