"""Read-only checks for the emulator's existing privileged command facility."""
import json, shlex, subprocess

ADB = r'D:\Program Files\Tencent\GameLoop\Application\7.0.167.0\adb.exe'
SERIAL = '127.0.0.1:5555'

def shell(command, timeout=12):
    p = subprocess.run([ADB, '-s', SERIAL, 'shell', command], capture_output=True, text=True, timeout=timeout)
    return {'exit_code': p.returncode, 'stdout': p.stdout.strip(), 'stderr': p.stderr.strip()}

def privileged(command):
    return '/system/xbin/txperm 0 /system/bin/sh -c ' + shlex.quote(command)

if __name__ == '__main__':
    checks = {
        'root_identity': privileged('id'),
        'root_shell_maps': privileged('head -c 128 /proc/2143/maps'),
        'su_context_maps': privileged('runcon u:r:su:s0 /system/bin/sh -c ' + shlex.quote('id; head -c 128 /proc/2143/maps')),
        'root_shell_enforcement': privileged('getenforce; grep CapEff /proc/self/status'),
    }
    results = {name: shell(command) for name, command in checks.items()}
    print(json.dumps(results, indent=2))
