import runpy,json
from pathlib import Path
ns=runpy.run_path('investigation/visibility/clock-registration.py');b=ns['b'];rel=ns['relative'];off=ns['off'];resolved=ns['resolved'];assembly=ns['assembly']
def string(a):
 o=off(a)
 if o is None or not 0x37c0380<=a<0x4410272:return None
 raw=b[o:o+150].split(b'\0')[0]
 return raw.decode('ascii') if raw and all(32<=x<127 for x in raw) else None
result={}
for name,center in [('bCanSee1',0xdc1e5b0),('bCanSee2',0xdc471a8),('EnemyInfoList',0xe022b28),('AutoAimInfo',0xd79ad90),('AutoAimSelector',0xddbcf98),('CanSeeTargetFlag',0xe01ff70)]:
 rows=[]
 for row in rel[(rel['offset']>=center-0x500)&(rel['offset']<=center+0x500)]:
  a=int(row['offset']);v=int(row['addend']);text=string(v)
  if text:rows.append({'address':hex(a),'text':text,'next':hex(resolved(a+8) or 0),'plus40':hex(resolved(a+40) or 0),'plus48':hex(resolved(a+48) or 0),'minus8':hex(resolved(a-8) or 0)})
 result[name]=rows;print('\n'+name);print('\n'.join(json.dumps(x) for x in rows))
for name,address in [('bCanSee1Setter',0x76741a0),('bCanSee2Setter',0x76aa9b8),('AutoAimInfoStruct',0x712863c),('AutoAimInfoPropertyStruct',0x7128780)]:
 result[name]=ns['assembly'](address,128);print('\n'+name);print('\n'.join(result[name]))
Path('investigation/visibility/alternative-owner-context.json').write_text(json.dumps(result,indent=2))
