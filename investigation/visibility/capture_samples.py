import sys,json,subprocess
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'investigation/gameloop-21518'))
from access_probe import shell,privileged,ADB,SERIAL
from live_candidate import bootstrap
out=Path(__file__).parent
label=sys.argv[1] if len(sys.argv)>1 else 'sample'
assert label.replace('-','').isalnum()
pid=int(shell('pidof com.tencent.ig')['stdout'].split()[0])
maps=shell(bootstrap(f'cat /proc/{pid}/maps'))
assert maps['exit_code']==0,maps
rows=[line.split() for line in maps['stdout'].splitlines() if line.endswith('/libUE4.so')]
base=min(int(row[0].split('-')[0],16)-int(row[2],16) for row in rows)
digest=shell(privileged('/system/bin/toybox sha256sum '+rows[0][-1]))['stdout'].split()[0]
assert digest=='ad923890b4e12e389aba66253ef0259285b351b3742fbda7ae0d0716ea070484',digest
subprocess.run([ADB,'-s',SERIAL,'push',str(out/'visibility-samples'),'/data/local/tmp/esp-visibility-samples'],capture_output=True,check=True)
assert shell('chmod 700 /data/local/tmp/esp-visibility-samples')['exit_code']==0
screen=subprocess.run([ADB,'-s',SERIAL,'exec-out','screencap','-p'],capture_output=True,check=True,timeout=12)
(out/(label+'-before.png')).write_bytes(screen.stdout)
result=shell(bootstrap(f'/data/local/tmp/esp-visibility-samples {pid} {base:x}'),timeout=20)
enforcement=shell('getenforce')
(out/(label+'.jsonl')).write_text(result['stdout'])
(out/(label+'-transport.json')).write_text(json.dumps({'pid':pid,'base':hex(base),'sha256':digest,'enforcement':enforcement,'exit':result['exit_code'],'stderr':result['stderr']},indent=2))
assert enforcement['stdout']=='Enforcing',enforcement
assert result['exit_code']==0,result['stderr']
data=[json.loads(line) for line in result['stdout'].splitlines() if line.startswith('{')]
assert len(data)==60 and all('Error' not in row for row in data)
screen=subprocess.run([ADB,'-s',SERIAL,'exec-out','screencap','-p'],capture_output=True,check=True,timeout=12)
(out/(label+'-after.png')).write_bytes(screen.stdout)
print(json.dumps({'frames':len(data),'elapsed':data[-1]['Elapsed'],'actors':data[-1]['Samples'],'camera':data[-1]['Snapshot']['CameraRotation'],'enforcement':enforcement['stdout']},indent=2))
