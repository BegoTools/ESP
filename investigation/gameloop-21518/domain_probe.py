"""Test an existing Android diagnostic domain, restoring enforcement before reading."""
import json, shlex
from access_probe import shell, privileged

inner = 'setenforce 1; echo "read_enforcement=$(getenforce)"; id; head -c 128 /proc/2143/maps'
command = """
original=$(getenforce)
echo "original=$original"
if [ "$original" != Enforcing ]; then exit 2; fi
trap 'setenforce 1' EXIT HUP INT TERM
setenforce 0 || exit 1
runcon u:r:su:s0 /system/bin/sh -c INNER
""".replace('INNER', shlex.quote(inner))
print(json.dumps(shell(privileged(command)), indent=2))
print(json.dumps(shell('getenforce'), indent=2))
