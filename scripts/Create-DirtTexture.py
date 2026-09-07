"""Frontier-owned deterministic procedural dirt albedo; Python standard library only."""
import pathlib, random, struct, zlib
root = pathlib.Path(__file__).resolve().parents[1]
dest = root/'src/MsfsSdk/Templates/SingleRunway/Dirt/Textures/FrontierDirt_Albedo.png'
rng = random.Random(20260907)
rows = bytearray()
for y in range(256):
    rows.append(0)
    for x in range(256):
        n = rng.randrange(-20, 21)
        rows.extend((max(0,min(255,c+n)) for c in (155,128,91)))
        rows.append(255)
def chunk(t, data):
    return struct.pack('>I',len(data))+t+data+struct.pack('>I',zlib.crc32(t+data))
png = b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',256,256,8,6,0,0,0))+chunk(b'IDAT',zlib.compress(rows,9))+chunk(b'IEND',b'')
dest.parent.mkdir(parents=True,exist_ok=True)
dest.write_bytes(png)
