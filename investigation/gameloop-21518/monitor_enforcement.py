import json,time
from pathlib import Path
from access_probe import shell
samples=[]
for i in range(34):
    samples.append({'elapsed':i*2,'mode':shell('getenforce')['stdout']})
    time.sleep(2)
(Path(__file__).parent/'stream-enforcement.json').write_text(json.dumps(samples,indent=2))
print(json.dumps({'samples':len(samples),'modes':sorted(set(row['mode'] for row in samples))}))
