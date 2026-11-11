import sys,json,re,struct,time
from pathlib import Path
sys.path.insert(0,str(Path(__file__).parent))
from remote_probe import Memory
from runtime_reflection import Reflection

m=Memory();r=Reflection(m)
inventory=json.loads(Path(__file__).with_name('native-inventory.json').read_text())
selected=['BP_Rifle_M762_Wrapper_C','BP_Ammo_556mm_Pickup_C','Drink_Pickup_C','PickUp_BP_Helmet_Lv1_C','GoldenTokenWrapper_C']
output={'pid':m.pid,'base':hex(m.base),'classes':{}}
destination=Path(__file__).with_name('loot-live-reflection.json')
for class_name in selected:
    candidates=[a for a in inventory['Actors'] if a['Class']==class_name]
    for actor in candidates:
        address=int(actor['Address'],16)
        try:
            cls=m.u64(address+0x10)
            if r.obj(cls)!=class_name:continue
            print('CLASS',class_name,hex(address),flush=True)
            properties=r.properties(cls)
            parents=[];current=cls
            for _ in range(24):
                if not current:break
                parents.append(r.obj(current));current=m.u64(current+0x30)
            data={'object':hex(address),'class_pointer':hex(cls),'parents':parents,'properties':properties,'samples':{}}
            for p in properties:
                if re.search('item|pick|count|wrapper|info|name|display|type|id',p['name'],re.I) and p['owner'] not in ['Actor','Object']:
                    print(p,flush=True)
                    off=int(p['offset'],16)
                    if off>=0:
                        try:data['samples'][p['name']]=m.read(address+off,16).hex()
                        except Exception as e:data['samples'][p['name']]={'error':str(e)}
            output['classes'][class_name]=data;destination.write_text(json.dumps(output,indent=2));break
        except Exception as e:print('SKIP',class_name,str(e),flush=True)
output['pages_read']=m.reads;destination.write_text(json.dumps(output,indent=2))
