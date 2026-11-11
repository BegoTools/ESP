"""Read-only exact-build selected vehicle and player-bool reflection probe."""
from pathlib import Path
import json,struct,time
from remote_probe import Memory
from runtime_reflection import Reflection

HERE=Path(__file__).parent
m=Memory();r=Reflection(m)
inventory=json.loads((HERE/'native-inventory.json').read_text())
engine=m.u64(m.base+0xf621d90)
world=m.u64(m.u64(m.u64(engine+0xc40))+0x2a0)
controller=m.u64(m.u64(m.u64(world+0x38)+0x78)+0x30)
pawn=m.u64(controller+0x528)

field=0x40000a637890
meta=m.read(field,0x78)
bot={'pid':m.pid,'property_name':r.obj(field),'property_class':r.obj(m.u64(field+0x10)),
     'owner':r.obj(m.u64(field+0x20)),'offset':hex(m.i32(field+0x44)),
     'metadata_address':hex(field),'metadata_hex':meta.hex(),'metadata_70_73':list(meta[0x70:0x74]),'samples':[]}
for row in inventory['Actors']:
    if not row.get('IsPlayer') and int(row['Address'],16)!=pawn:continue
    a=int(row['Address'],16)
    try:
        cls=r.obj(m.u64(a+0x10))
        bot['samples'].append({'address':hex(a),'class':cls,'is_local':a==pawn,'flag_byte':m.read(a+0xa40,1)[0],
                               'pet_flag_byte':m.read(a+0xa42,1)[0]})
    except Exception as exc:bot['samples'].append({'address':hex(a),'error':str(exc)})
(HERE/'playerflags-verified.json').write_text(json.dumps(bot,indent=2))
print('BOT',json.dumps(bot,indent=2),flush=True)

vehicle=int(next(a['Address'] for a in inventory['Actors'] if a['Class']=='BP_VH_Buggy_2_C'),16)
cls=m.u64(vehicle+0x10)
chain=[];seen=set()
while cls and cls not in seen and len(chain)<20:
    seen.add(cls);chain.append({'address':hex(cls),'name':r.obj(cls)});cls=m.u64(cls+0x30)
print('VEHICLE CLASS',chain,flush=True)
properties=r.properties(m.u64(vehicle+0x10))
result={'pid':m.pid,'vehicle':hex(vehicle),'class_chain':chain,'properties':properties,'selected':[],'pages_read':m.reads}
for p in properties:
    if any(t in p['name'].lower() for t in ['health','fuel','common','name','vehicletype','vehicleid','vehicleconfig','vehiclehp','maxhp','hpmax']):
        q=dict(p);offset=int(p['offset'],16)
        q['raw_value']=m.read(vehicle+offset,16).hex()
        if p['type']=='ObjectProperty':
            addr=m.u64(vehicle+offset);q['pointer']=hex(addr)
            if addr:
                try:q['object_class']=r.obj(m.u64(addr+0x10))
                except Exception as exc:q['object_error']=str(exc)
        if p['type']=='FloatProperty':q['value']=m.floats(vehicle+offset,1)[0]
        if p['type']=='IntProperty':q['value']=m.i32(vehicle+offset)
        result['selected'].append(q);print('FIELD',q,flush=True)
(HERE/'vehicle-reflection-buggy.json').write_text(json.dumps(result,indent=2))
print('SAVED',len(properties),'properties',m.reads,'pages',flush=True)

for p in result['selected']:
    if p['type']=='ObjectProperty' and p.get('pointer') not in [None,'0x0'] and any(t in p['name'].lower() for t in ['common','health','fuel']):
        a=int(p['pointer'],16);component_properties=r.properties(m.u64(a+0x10));selected=[]
        for cp in component_properties:
            if any(t in cp['name'].lower() for t in ['health','fuel','hp','maxhp','name','type','vehicleid']):
                q=dict(cp);offset=int(cp['offset'],16);q['raw_value']=m.read(a+offset,16).hex()
                if cp['type']=='FloatProperty':q['value']=m.floats(a+offset,1)[0]
                if cp['type']=='IntProperty':q['value']=m.i32(a+offset)
                selected.append(q);print('COMPONENT FIELD',q,flush=True)
        (HERE/('vehicle-component-'+p['name']+'.json')).write_text(json.dumps({'address':hex(a),'class':r.obj(m.u64(a+0x10)),'properties':component_properties,'selected':selected,'pages_read':m.reads},indent=2))
