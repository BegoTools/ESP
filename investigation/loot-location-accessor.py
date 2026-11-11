import sys,json,struct
from pathlib import Path
sys.path.insert(0,str(Path(__file__).parent))
sys.path.insert(0,str(Path(__file__).parent/'python-libs'))
from reflection_dump import data,relocs,resolved
from capstone import Cs,CS_ARCH_ARM64,CS_MODE_LITTLE_ENDIAN
from remote_probe import Memory

name=b'K2_GetComponentLocation';positions=[]
for raw in [name,bytes(v^0xc8 for v in name)]:
    offset=0
    while True:
        offset=data.find(raw,offset)
        if offset<0:break
        positions.append(offset);offset+=len(raw)
refs=[(a,v) for a,v in relocs.items() if v in positions]
print('STRINGS',list(map(hex,positions)),'REFS',[(hex(a),hex(v),hex(resolved(a+8))) for a,v in refs],flush=True)
cs=Cs(CS_ARCH_ARM64,CS_MODE_LITTLE_ENDIAN);m=Memory();lines=[]
for a,v in refs:
    func=resolved(a+8)
    if not 0x5bd8000<=func<0xd498000:continue
    page=func&~4095
    raw=m.read(m.base+page,8192)
    Path(__file__).with_name('loot-runtime-scene-location-'+hex(page)+'.bin').write_bytes(raw)
    lines.append(f'Name RVA={v:#x}; registration pair={a:#x}; native exec RVA={func:#x}')
    for instruction in cs.disasm(raw[func-page:func-page+400],func):
        lines.append(f'{instruction.address:#x}: {instruction.mnemonic:8} {instruction.op_str}')
Path(__file__).with_name('loot-component-location-accessor.asm.txt').write_text('\n'.join(lines))
print('\n'.join(lines))
