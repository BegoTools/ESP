"""Inspect live Unreal reflection for selected known objects; no process modification."""
import json, struct
from pathlib import Path
from remote_probe import Memory

class Reflection:
    def __init__(self, memory):
        self.m = memory
        state=memory.base+0xf354be0
        encoded=memory.i32(state)
        count=(encoded-100)//3
        if not 0 <= count <= 20 or (encoded-100)%3:
            raise ValueError('Name indirection invalid')
        self.names=memory.u64(state+8)
        for _ in range(count): self.names=memory.u64(self.names)
        self.name_cache={}
        if self.name(0)!='None': raise ValueError('Names table failed sentinel validation')

    def name(self,index):
        if index in self.name_cache:return self.name_cache[index]
        if not 0 <= index <= 2000000: raise ValueError('Invalid name index')
        m=self.m
        entry=m.u64(m.u64(self.names+(index//0x4000)*8)+(index%0x4000)*8)
        wide=bool(m.i32(entry+8)&1)
        raw=bytearray(); current=entry+12
        while len(raw)<192:
            amount=min(32,4096-(current&4095),192-len(raw))
            raw.extend(m.read(current,amount)); current+=amount
            text=raw.decode('utf-16-le',errors='replace') if wide else raw.decode(errors='replace')
            if '\0' in text:
                text=text.split('\0')[0]; break
        self.name_cache[index]=text
        return text

    def obj(self,a):return self.name(self.m.i32(a+0x18))
    def properties(self, cls):
        result=[]; seen=set(); field=self.m.u64(cls+0x58)
        while field and field not in seen and len(seen)<3000:
            seen.add(field)
            b=self.m.read(field,0x80)
            name=self.name(struct.unpack_from('<i',b,0x18)[0])
            type_name=self.obj(struct.unpack_from('<Q',b,0x10)[0])
            owner=struct.unpack_from('<Q',b,0x20)[0]
            offset=struct.unpack_from('<i',b,0x44)[0]
            extra=struct.unpack_from('<Q',b,0x70)[0]
            result.append({'name':name,'type':type_name,'offset':hex(offset),'owner':self.obj(owner),'field':hex(field),'extra':hex(extra)})
            field=struct.unpack_from('<Q',b,0x50)[0]
        return result

if __name__=='__main__':
    m=Memory(); r=Reflection(m)
    engine=m.u64(m.base+0xf621d90)
    world=m.u64(m.u64(m.u64(engine+0xc40))+0x2a0)
    pc=m.u64(m.u64(m.u64(world+0x38)+0x78)+0x30)
    pawn=m.u64(pc+0x528); camera=m.u64(pc+0x548); level=m.u64(world+0x30)
    objects={'pawn':pawn,'camera':camera,'controller':pc,'world':world,'level':level,'root':m.u64(pawn+0x208)}
    output={}
    for label,obj in objects.items():
        cls=m.u64(obj+0x10)
        props=r.properties(cls)
        output[label]={'object':hex(obj),'class':r.obj(cls),'properties':props}
        print(label,r.obj(cls),'properties',len(props),flush=True)
        for prop in props:
            if prop['name'] in {'Health','HealthMax','TeamID','PlayerName','PersistentLevel','Actors','RelativeLocation','CameraCache','POV','Rotation','Location','FOV','LocalPlayers','PlayerController','RootComponent','CharacterMovement','Mesh','CapsuleComponent'}:
                print(prop,flush=True)
    # Follow the CameraCache and POV struct metadata to learn their actual live layout.
    cache=next(p for p in output['camera']['properties'] if p['name']=='CameraCache')
    cache_struct=int(cache['extra'],16)
    output['camera_cache_struct']={'class':r.obj(cache_struct),'properties':r.properties(cache_struct)}
    pov=next(p for p in output['camera_cache_struct']['properties'] if p['name']=='POV')
    pov_struct=int(pov['extra'],16)
    output['camera_pov_struct']={'class':r.obj(pov_struct),'properties':r.properties(pov_struct)}
    for label in ['camera_cache_struct','camera_pov_struct']:print(label,output[label],flush=True)
    output['pages_read']=m.reads
    Path(__file__).with_name('live-reflection-layout.json').write_text(json.dumps(output,indent=2))
