import json
from access_probe import shell, privileged

command='trap "setenforce 1" EXIT HUP INT TERM; setenforce 0 || exit 1; /data/local/tmp/esp-open-handle-probe 2143'
print(json.dumps(shell(privileged(command)),indent=2))
print(json.dumps(shell('getenforce'),indent=2))
