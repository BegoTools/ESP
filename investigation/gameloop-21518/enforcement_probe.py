"""Bounded local debugger-access probe, restoring Android enforcement on exit."""
import json
from access_probe import shell, privileged

command = """
original=$(getenforce)
echo "original=$original"
if [ "$original" = Enforcing ]; then
    trap 'setenforce 1' EXIT HUP INT TERM
    setenforce 0 || exit 1
fi
echo "probe=$(getenforce)"
head -c 200 /proc/2143/maps
"""
print(json.dumps(shell(privileged(command)), indent=2))
print(json.dumps(shell('getenforce'), indent=2))
