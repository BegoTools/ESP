"""Exact-artifact candidate validation; retains all fail-closed checks in the product."""
import hashlib, json, re, subprocess
from pathlib import Path
from access_probe import shell, privileged, ADB, SERIAL

ROOT=Path(__file__).resolve().parents[2]
OUT=Path(__file__).resolve().parent

def bootstrap(command):
    return privileged('trap "setenforce 1" EXIT HUP INT TERM; (sleep 2; setenforce 1) </dev/null >/dev/null 2>&1 &\nsetenforce 0 || exit 70\nexport ESP_RESTORE_ENFORCING=1\n'+command)

if __name__=='__main__':
    digest=hashlib.sha256((OUT/'libUE4.so').read_bytes()).hexdigest()
    assert digest=='ad923890b4e12e389aba66253ef0259285b351b3742fbda7ae0d0716ea070484'
    pid=int(shell('pidof com.tencent.ig')['stdout'].split()[0])
    maps=shell(bootstrap(f'cat /proc/{pid}/maps'))
    assert maps['exit_code']==0,maps
    (OUT/'live-maps.txt').write_text(maps['stdout'])
    bases=[]
    for line in maps['stdout'].splitlines():
        p=line.split()
        if len(p)>=6 and p[-1].endswith('/libUE4.so'): bases.append(int(p[0].split('-')[0],16)-int(p[2],16))
    base=min(bases)
    subprocess.run([ADB,'-s',SERIAL,'push',str(ROOT/'dist/native/esp-reader'),'/data/local/tmp/esp-workbench-reader'],check=True,capture_output=True)
    assert shell('chmod 700 /data/local/tmp/esp-workbench-reader')['exit_code']==0
    result=shell(bootstrap(f'/data/local/tmp/esp-workbench-reader {pid} {base:x} --once --session=21518'),timeout=20)
    (OUT/'live-candidate-transport.json').write_text(json.dumps(result,indent=2))
    enforcement=shell('getenforce')
    print(json.dumps({'pid':pid,'base':hex(base),'enforcement':enforcement,'transport_exit':result['exit_code'],'stderr':result['stderr']},indent=2))
    assert enforcement['stdout']=='Enforcing'
    assert result['exit_code']==0,result
    snapshot=json.loads(next(line for line in result['stdout'].splitlines() if line.startswith('{')))
    assert 'Error' not in snapshot,snapshot
    (OUT/'live-candidate-snapshot.json').write_text(json.dumps(snapshot,indent=2))
