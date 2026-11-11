import runpy,json
from pathlib import Path
ns=runpy.run_path('investigation/visibility/clock-registration.py');result={}
for name,a in [('GetCacheTargetListExec',0x7128bc0),('CalcLockActorExec',0x7128aa4),('TryDoAutoAimExec',0x7128d80),('GetNearestAutoAimEnemyExec',0x784acf0),('RaycastReachExec',0x784a770),('CanSeeCustomVarExec',0xb581258)]:
 rows=ns['assembly'](a,500);result[name]={'entry':hex(a),'assembly':rows};print('\n'+name);print('\n'.join(rows[:80]))
Path('investigation/visibility/alternative-autoaim-execs.json').write_text(json.dumps(result,indent=2))
