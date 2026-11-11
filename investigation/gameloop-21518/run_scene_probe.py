import json,subprocess
from pathlib import Path
from access_probe import shell,ADB,SERIAL
from live_candidate import bootstrap

out=Path(__file__).parent
subprocess.run([ADB,'-s',SERIAL,'push',str(out/'scene-probe'),'/data/local/tmp/esp-scene-probe'],check=True,capture_output=True)
shell('chmod 700 /data/local/tmp/esp-scene-probe')
result=shell(bootstrap('/data/local/tmp/esp-scene-probe 2143 400023600000'),timeout=30)
print(result['stderr'])
data=json.loads(result['stdout'])
(out/'live-scene-reflection.json').write_text(json.dumps(data,indent=2))
print(json.dumps(data,indent=2))
print(shell('getenforce'))
