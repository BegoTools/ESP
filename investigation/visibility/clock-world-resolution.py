import runpy,json
from pathlib import Path
ns=runpy.run_path('investigation/visibility/clock-registration.py');rows=ns['assembly'](0xae57d04,224)
Path('investigation/visibility/clock-world-resolution.json').write_text(json.dumps({'entry':'0xae57d04','assembly':rows,'GEngine_pointer_slot':hex(ns['resolved'](0xee956c8) or 0),'world_float_offsets':{'GetTimeSeconds':'0xcc8','GetRealTimeSeconds':'0xcd0','GetWorldDeltaSeconds':'0xcd8'}},indent=2))
print('GEngine GOT resolution',hex(ns['resolved'](0xee956c8) or 0));print('\n'.join(rows))
