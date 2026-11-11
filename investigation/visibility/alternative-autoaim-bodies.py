import runpy,json
from pathlib import Path
ns=runpy.run_path('investigation/visibility/clock-registration.py');result={}
for name,a,size in [('GetCacheTargetList',0x5c7cce0,32),('InitCacheTargetList',0x5c7c698,700),('TryDoAutoAim',0x5c7c38c,700),('CanSeeCustomVar',0xb530020,260)]:
 rows=ns['assembly'](a,size);result[name]={'entry':hex(a),'assembly':rows};print('\n'+name);print('\n'.join(rows[:100]))
Path('investigation/visibility/alternative-autoaim-bodies.json').write_text(json.dumps(result,indent=2))
