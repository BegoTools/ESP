import runpy,json
from pathlib import Path
ns=runpy.run_path('investigation/visibility/clock-registration.py');result={}
for name,a in [('GetTimeSeconds',0xa88377c),('GetRealTimeSeconds',0xa8837fc),('GetWorldDeltaSeconds',0xa88373c),('GetGameTimeInSeconds',0xa979700),('OtherGetRealTimeSeconds',0x68d2c04)]:
 rows=ns['assembly'](a,160);result[name]={'entry':hex(a),'assembly':rows};print('\n'+name+' '+hex(a));print('\n'.join(rows))
Path('investigation/visibility/clock-accessors.json').write_text(json.dumps(result,indent=2))
