import runpy,json
from pathlib import Path
ns=runpy.run_path('investigation/visibility/clock-registration.py');b=ns['b'];rel=ns['relative'];va=ns['va'];off=ns['off'];resolved=ns['resolved'];assembly=ns['assembly'];exe=ns['exec_address'];result={}
for name in ['GetNearestAutoAimEnemy','RegisterAutoAimCandidateWithAimPoint','UnregisterAutoAimCandidate','AutoAimInfo','AutoAimParams','AutoAimTargetSelector','EnemyInfoList','bCanSee','CanSeeCustomVar','bSetCanSeeTargetFlag']:
 p=b.find(name.encode()+b'\0');rows=[]
 if p<0:continue
 a=va(p)
 for row in rel[rel['addend']==a]:
  address=int(row['offset']);near=[resolved(address+d) for d in range(-24,80,8)];entry={'storage':hex(address),'near_start':hex(address-24),'words':[hex(v or 0) for v in near]}
  candidate=resolved(address+8)
  if candidate is not None and exe(candidate):entry['candidate']=hex(candidate);entry['assembly']=assembly(candidate,180)
  rows.append(entry)
 result[name]={'string':hex(a),'refs':rows}
Path('investigation/visibility/alternative-registration.json').write_text(json.dumps(result,indent=2))
for name,item in result.items():
 print(name,item['string']);print('\n'.join(json.dumps(x) for x in item['refs']))
