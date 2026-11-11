from pathlib import Path
import sys,json,hashlib
ROOT=Path(__file__).resolve().parent
sys.path.insert(0,str(ROOT.parent/'python-libs'))
from capstone import Cs,CS_ARCH_ARM64,CS_MODE_LITTLE_ENDIAN
params=json.loads((ROOT/'static-reflection-params.json').read_text())
anchors=json.loads((ROOT/'anchor-comparison.json').read_text())
def flatten(item):return {(name,r['address']):r for name,hits in item['names'].items() for hit in hits for r in hit['refs']}
left,right=map(flatten,params)
equal=[{'name':name,'descriptor_rva':address} for (name,address),r in left.items() if (name,address) in right and r['hex96']==right[name,address]['hex96']]
different=[{'name':name,'descriptor_rva':address} for (name,address),r in left.items() if (name,address) not in right or r['hex96']!=right[name,address]['hex96']]
expected={'AcknowledgedPawn':0x528,'PlayerCameraManager':0x548,'RootComponent':0x208,'RelativeLocation':0x1e4,'Health':0xe60,'HealthMax':0xe64,'TeamID':0x998,'PlayerName':0x960,'CameraCache':0x520,'FOV':0x24,'CapsuleHalfHeight':0x9f8,'HPMax':0x350,'DefineID':0x5f8,'Levels':0x440,'NetDriver':0x38,'ServerConnection':0x78,'PlayerController':0x30}
fields=[]
for name,value in expected.items():
    for (n,a),r in right.items():
        if n==name and int(r['words'][5],16)==value:
            fields.append({'name':name,'descriptor_rva':a,'declared_offset':hex(value),'record96_equal':r['hex96']==left[n,a]['hex96']})
cs=Cs(CS_ARCH_ARM64,CS_MODE_LITTLE_ENDIAN)
binaries=[(ROOT.parent/'libUE4.so').read_bytes(),(ROOT/'libUE4.so').read_bytes()]
segments=json.loads((ROOT/'static-comparison.json').read_text())['layouts'][0]['segments']
def off(a):
    for s in segments:
        if s['p_type']=='PT_LOAD' and s['p_vaddr']<=a<s['p_vaddr']+s['p_filesz']:return a-s['p_vaddr']+s['p_offset']
    raise ValueError(hex(a))
callbacks=[]
for label,a in [('UAECharacter_bIsAI_setbit',0x7f63250),('Actor_bHidden_setbit',0xaf08f14)]:
    o=off(a);raw=[b[o:o+32] for b in binaries]
    callbacks.append({'name':label,'rva':hex(a),'bytes_equal':raw[0]==raw[1],'hex':raw[1].hex(),'assembly':[f'{i.address:#x}: {i.mnemonic} {i.op_str}' for i in cs.disasm(raw[1],a)]})
summary={'descriptor_record_count':len(left),'equal_record_count':len(equal),'different_records':different,'selected_offset_records':fields,'bool_callbacks':callbacks,
         'anchor_regions':[{ 'name':name,'address':a['address'],'size':len(bytes.fromhex(a['bytes'])),'byte_equal':a['bytes']==anchors[1]['anchors'][name]['bytes']} for name,a in anchors[0]['anchors'].items()]}
(ROOT/'static-layout-summary.json').write_text(json.dumps(summary,indent=2))
print(json.dumps(summary,indent=2))
