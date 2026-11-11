"""Read-only inspection of exact ELF reflection descriptors; offsets remain candidates until validated."""
import sys, struct, json
from pathlib import Path
sys.path.insert(0, str(Path(__file__).parent / 'python-libs'))
from elftools.elf.elffile import ELFFile

artifact = Path(__file__).parent / 'libUE4.so'
data = artifact.read_bytes()
with artifact.open('rb') as source:
    elf = ELFFile(source)
    relocs = {a: c for a, b, c in struct.iter_unpack('<QQq', elf.get_section_by_name('.rela.dyn').data()) if b & 0xffffffff == 1027}

def qword(a):
    return struct.unpack_from('<Q', data, a)[0]

def resolved(a):
    return relocs.get(a, qword(a))

def string(a):
    if not 0x37c0380 <= a < 0x4410272:
        return None
    raw = data[a:a+160].split(b'\0')[0]
    if raw and all(32 <= b < 127 for b in raw):
        return raw.decode('ascii')
    return None

if __name__ == '__main__':
    start = int(sys.argv[1], 0)
    end = int(sys.argv[2], 0) if len(sys.argv) > 2 else start+0x800
    for address in sorted(relocs):
        if not start <= address < end:
            continue
        name = string(relocs[address])
        if name:
            print(hex(address), repr(name), 'before', hex(qword(address-8)), 'plus8', hex(qword(address+8)), 'plus40', hex(qword(address+40)))
