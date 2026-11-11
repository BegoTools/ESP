import sys,json,re,subprocess,hashlib
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'investigation/gameloop-21518'))
from access_probe import shell,privileged,ADB,SERIAL
from live_candidate import bootstrap
out=Path(__file__).parent
pid=int(shell('pidof com.tencent.ig')['stdout'].split()[0])
maps=shell(bootstrap(f'cat /proc/{pid}/maps'))
if maps['exit_code']!=0:raise RuntimeError(maps)
rows=[line.split() for line in maps['stdout'].splitlines() if line.endswith('/libUE4.so')]
base=min(int(row[0].split('-')[0],16)-int(row[2],16) for row in rows)
library=rows[0][-1]
digest=shell(privileged('/system/bin/toybox sha256sum '+library))['stdout'].split()[0]
assert digest=='ad923890b4e12e389aba66253ef0259285b351b3742fbda7ae0d0716ea070484',digest
subprocess.run([ADB,'-s',SERIAL,'push',str(out/'visibility-probe'),'/data/local/tmp/esp-visibility-probe'],capture_output=True,check=True)
assert shell('chmod 700 /data/local/tmp/esp-visibility-probe')['exit_code']==0
result=shell(bootstrap(f'/data/local/tmp/esp-visibility-probe {pid} {base:x}'),timeout=30)
enforcement=shell('getenforce')
(out/'transport.json').write_text(json.dumps({'pid':pid,'base':hex(base),'sha256':digest,'enforcement':enforcement,**result},indent=2))
assert enforcement['stdout']=='Enforcing',enforcement
assert result['exit_code']==0,result
data=json.loads(next(line for line in result['stdout'].splitlines() if line.startswith('{')))
assert 'Error' not in data,data
(out/'live-reflection.json').write_text(json.dumps(data,indent=2))
for obj in data['Objects']:
    if obj:print(obj['Kind'],obj['Name'],obj['Address'],len(obj['Properties']),obj['Mesh480'])
print('SELinux',enforcement['stdout'])
