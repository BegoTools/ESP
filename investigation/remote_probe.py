"""Scoped read-only Android process memory probe, using the tested LDPlayer transport."""
import base64, json, re, struct, subprocess, time
from pathlib import Path

ADB = r'D:\LDPlayer\LDPlayer14\adb.exe'
SERIAL = 'emulator-5554'

def adb(*args):
    result = subprocess.run([ADB, '-s', SERIAL, *args], capture_output=True, timeout=12)
    if result.returncode:
        raise RuntimeError(result.stderr.decode(errors='replace'))
    return result.stdout

def shell(command):
    return adb('shell', command).decode(errors='replace').strip()

class Memory:
    def __init__(self):
        self.pid = int(shell('pidof com.tencent.ig').split()[0])
        self.maps_text = shell("su -c 'cat /proc/%d/maps'" % self.pid)
        self.maps = []
        for line in self.maps_text.splitlines():
            m = re.match(r'^([\da-f]+)-([\da-f]+)\s+(\S+)\s+([\da-f]+)\s+\S+\s+\d+\s*(.*)$', line)
            if m:
                self.maps.append((int(m[1],16), int(m[2],16), m[3], int(m[4],16), m[5]))
        self.base = min(start-off for start,end,perm,off,name in self.maps if name.endswith('/libUE4.so'))
        self.cache = {}
        self.reads = 0

    def read(self, address, size):
        if not 0 < size <= 131072:
            raise ValueError('Read size exceeds scoped bound')
        result = bytearray()
        while len(result) < size:
            current = address + len(result)
            if not any(s <= current < e and p.startswith('r') for s,e,p,_,_ in self.maps):
                raise ValueError('Address outside readable maps: '+hex(current))
            page = current & ~4095
            if page not in self.cache:
                command = f"su -c '/system/bin/toybox dd if=/proc/{self.pid}/mem bs=4096 skip={page//4096} count=1 2>/dev/null | /system/bin/toybox base64'"
                block = base64.b64decode(adb('exec-out',command),validate=False)
                if len(block) != 4096:
                    raise IOError(f'Expected page of 4096 bytes, received {len(block)} at {hex(page)}')
                self.cache[page] = block
                self.reads += 1
            offset = current-page
            result.extend(self.cache[page][offset:offset+min(size-len(result),4096-offset)])
        return bytes(result)

    def u64(self, a): return struct.unpack('<Q',self.read(a,8))[0]
    def i32(self, a): return struct.unpack('<i',self.read(a,4))[0]
    def floats(self, a, count=3): return struct.unpack('<'+'f'*count,self.read(a,count*4))

if __name__ == '__main__':
    m = Memory()
    engine_storage = m.base+0xf621d90
    got = m.u64(m.base+0xee956c8)
    engine = m.u64(engine_storage)
    contexts = m.u64(engine+0xc40)
    count = m.i32(engine+0xc48)
    result = {'pid':m.pid,'module_base':hex(m.base),'got':hex(got),'expected_global':hex(engine_storage),'engine':hex(engine),'context_count':count,'contexts':[]}
    if not 0 <= count <= 32:
        raise ValueError('Context count invalid')
    for i in range(count):
        c = m.u64(contexts+i*8)
        world = m.u64(c+0x2a0)
        result['contexts'].append({'address':hex(c),'type':m.read(c,1)[0],'world':hex(world),'world_vtable':hex(m.u64(world)) if world else None})
    result['game_instance'] = hex(m.u64(engine+0xe08))
    result['pages_read'] = m.reads
    Path(__file__).with_name('live-engine-validation.json').write_text(json.dumps(result,indent=2))
    print(json.dumps(result,indent=2))
