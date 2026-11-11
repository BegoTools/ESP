"""Validate reflected vehicle statistics on several known training vehicles."""
from pathlib import Path
import json,struct
from remote_probe import Memory
from runtime_reflection import Reflection
HERE=Path(__file__).parent
m=Memory();r=Reflection(m)
j=json.loads((HERE/'native-inventory.json').read_text())
out=[]
for row in j['Actors']:
    if not (row['Class'].startswith(('VH_','BP_VH_','Mirado_')) or row['Class']=='PickUp_07_C'):continue
    a=int(row['Address'],16)
    result={'address':hex(a),'inventory_class':row['Class']}
    try:
        cls=m.u64(a+0x10);chain=[];seen=set()
        while cls and cls not in seen and len(chain)<16:
            seen.add(cls);chain.append(r.obj(cls));cls=m.u64(cls+0x30)
        result['class_chain']=chain
        if 'STExtraVehicleBase' not in chain:raise ValueError('Not the validated vehicle family')
        comp=m.u64(a+0xc08);result['common']=hex(comp);result['common_class']=r.obj(m.u64(comp+0x10))
        result.update({'hp_max':m.floats(comp+0x350,1)[0],'hp':m.floats(comp+0x354,1)[0],
                       'fuel_max':m.floats(comp+0x438,1)[0],'fuel':m.floats(comp+0x43c,1)[0]})
        root=m.u64(a+0x208);result['position']=m.floats(root+0x1e4)
        result['vehicle_type_bytes']=m.read(a+0x70c,4).hex()
        text=m.u64(a+0x9a0);result['text_data']=hex(text)
        raw=m.read(text,96);result['text_data_bytes']=raw.hex();candidates=[]
        for offset in range(0,80,8):
            ptr,count,cap=struct.unpack_from('<Qii',raw,offset)
            if 0<count<=128 and count<=cap<=1024:
                try:
                    textbytes=m.read(ptr,count*2)
                    string=textbytes.decode('utf-16-le',errors='strict').rstrip('\0')
                    if string and all(c.isprintable() or c.isspace() for c in string):candidates.append({'offset':hex(offset),'pointer':hex(ptr),'count':count,'capacity':cap,'text':string})
                except Exception:pass
        result['display_string_candidates']=candidates
    except Exception as exc:result['error']=str(exc)
    out.append(result);print(json.dumps(result,ensure_ascii=True),flush=True)
(HERE/'vehicle-live-samples.json').write_text(json.dumps({'pid':m.pid,'samples':out,'pages_read':m.reads},ensure_ascii=False,indent=2),encoding='utf-8')
