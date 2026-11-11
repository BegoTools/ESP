import runpy,json
from pathlib import Path
ns=runpy.run_path('investigation/visibility/clock-registration.py');b=ns['b'];rel=ns['relative'];off=ns['off'];resolved=ns['resolved'];assembly=ns['assembly']
def string(a):
 o=off(a)
 if o is None or not 0x37c0380<=a<0x4410272:return None
 raw=b[o:o+150].split(b'\0')[0]
 return raw.decode('ascii') if raw and all(32<=x<127 for x in raw) else None
rows=[]
for row in rel[(rel['offset']>=0xd79a000)&(rel['offset']<=0xd79af00)]:
 a=int(row['offset']);s=string(int(row['addend']))
 if s:
  result={'address':hex(a),'name':s,'next':hex(resolved(a+8) or 0),'minus8':hex(resolved(a-8) or 0),'plus40':hex(resolved(a+40) or 0),'plus48':hex(resolved(a+48) or 0)};rows.append(result);print(json.dumps(result))
Path('investigation/visibility/alternative-autoaim-info.json').write_text(json.dumps(rows,indent=2))
