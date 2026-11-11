import sys,json,struct,re
from pathlib import Path
sys.path.insert(0,str(Path(__file__).parent))
from remote_probe import Memory,adb
from runtime_reflection import Reflection
m=Memory();r=Reflection(m)
text=adb('exec-out',f"su -c '/data/local/tmp/esp-workbench-reader {m.pid} {m.base:x} --inventory'").decode(errors='replace')
inventory=json.loads(next(line for line in text.splitlines() if line.startswith('{')))
if inventory.get('Error'):raise RuntimeError(inventory['Error'])
classes={};catalog=[]
for actor in inventory['Actors']:
    if not re.search('wrapper|pickup',actor['Class'],re.I):continue
    address=int(actor['Address'],16)
    try:
        cls=m.u64(address+0x10)
        if cls not in classes:
            current=cls;parents=[]
            for _ in range(24):
                if not current:break
                parents.append(r.obj(current));current=m.u64(current+0x30)
            classes[cls]=parents
        parents=classes[cls]
        if 'PickUpWrapperActor' not in parents:continue
        root=m.u64(address+0x208)
        row={'address':hex(address),'class':parents[0],'parents':parents,'item_type':m.i32(address+0x5f8),'item_id':m.i32(address+0x5fc),'count':m.i32(address+0x610),'can_pick_up':m.read(address+0x614,1)[0],'has_been_picked_up':m.read(address+0x615,1)[0],'position':list(m.floats(root+0x1e4))}
        catalog.append(row)
    except Exception as e:print('skip',actor['Class'],str(e),flush=True)
struct_props=r.properties(0x40000a5dea30)
output={'pid':m.pid,'base':hex(m.base),'define_id_struct_name':r.obj(0x40000a5dea30),'define_id_properties':struct_props,'pickups':catalog}
Path(__file__).with_name('loot-verified-catalog.json').write_text(json.dumps(output,indent=2))
unique={a['class']:a for a in catalog}
for name,a in sorted(unique.items()):print(name,a['item_type'],a['item_id'],a['count'],'flags',a['can_pick_up'],a['has_been_picked_up'])
print('TOTAL',len(catalog),'unique',len(unique),'DefineIDProperties',struct_props)
