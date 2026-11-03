"""Read-only MSFS diagnostic. No setters, events, controls, placement or career access.

SDK packet layouts follow SimConnect.h; facility replies remain diagnostic until
their actual packet ID/layout and aircraft-specific fields have been observed.
"""
import argparse
import ctypes as C
import datetime as D
import hashlib
import json
import math
import pathlib
import struct
import time

CORE = [('PLANE LATITUDE', 'degrees'), ('PLANE LONGITUDE', 'degrees'),
        ('PLANE ALTITUDE', 'feet'), ('PLANE ALT ABOVE GROUND', 'feet'),
        ('PLANE ALT ABOVE GROUND MINUS CG', 'feet'), ('GROUND ALTITUDE', 'feet'),
        ('GROUND VELOCITY', 'knots'), ('VERTICAL SPEED', 'feet per second'),
        ('SIM ON GROUND', 'bool'), ('IS SLEW ACTIVE', 'bool'),
        ('SIMULATION TIME', 'seconds'), ('SIMULATION RATE', 'number'),
        ('PLANE PITCH DEGREES', 'degrees'), ('PLANE BANK DEGREES', 'degrees'),
        ('PLANE HEADING DEGREES TRUE', 'degrees')]
OPTIONAL = [('AIRCRAFT AGL', 'feet'), ('AIRCRAFT ALTITUDE ABOVE OBSTACLES', 'feet'),
            ('SURFACE RELATIVE GROUND SPEED', 'feet per second'),
            ('SURFACE TYPE', 'enum'), ('ROTOR RPM:1', 'rpm'),
            ('GENERAL ENG COMBUSTION:1', 'bool'), ('HELICOPTER MASTER THROTTLE POSITION', 'percent over 100'),
            ('PLANE TOUCHDOWN LATITUDE', 'degrees'), ('PLANE TOUCHDOWN LONGITUDE', 'degrees'),
            ('PLANE TOUCHDOWN NORMAL VELOCITY', 'feet per second')]
# Documented contact indices 0..19 are candidates, not a claimed H125 skid map.
OPTIONAL += [(f'CONTACT POINT IS ON GROUND:{i}', 'bool') for i in range(20)]


def utc():
    return D.datetime.now(D.timezone.utc).isoformat()


def distance_nm(a, b):
    lat1, lat2 = map(math.radians, (a[0], b[0]))
    dlat, dlon = math.radians(b[0]-a[0]), math.radians(b[1]-a[1])
    h = math.sin(dlat/2)**2+math.cos(lat1)*math.cos(lat2)*math.sin(dlon/2)**2
    return 3440.065*2*math.asin(math.sqrt(min(1, h)))


class Airports:
    def __init__(self, request):
        self.request, self.total, self.seen, self.rows = request, None, set(), []

    def add(self, raw):
        if len(raw) < 28:
            raise ValueError('Truncated airport packet')
        size, _, kind, req, count, entry, total = struct.unpack_from('<7I', raw)
        if kind != 18 or req != self.request:
            return False
        expected = 28+count*36
        if size != len(raw) or len(raw) not in (expected, expected+36) or not 1 <= total <= 100000 or entry >= total or entry in self.seen or self.total not in (None, total) or len(self.rows)+count > 200000:
            raise ValueError('Invalid/incomplete/duplicate airport packet')
        self.total = total
        for i in range(count):
            ident, region, lat, lon, alt = struct.unpack_from('<9s3sddd', raw, 28+36*i)
            ident, region = ident.split(b'\0')[0].decode('ascii'), region.split(b'\0')[0].decode('ascii')
            if not ident or not all(map(math.isfinite, (lat, lon, alt))) or not -90 <= lat <= 90 or not -180 <= lon <= 180:
                raise ValueError('Invalid airport coordinates')
            self.rows.append(dict(ident=ident, region=region, latitude=lat, longitude=lon, altitudeMeters=alt))
        self.seen.add(entry)
        return len(self.seen) == self.total


def decode_data(raw, request, definition, count, strings=False, expected_object=0):
    if len(raw) < 40:
        raise ValueError('Truncated aircraft packet')
    req, obj, df, _, _, _, n = struct.unpack_from('<7I', raw, 12)
    if req != request:
        return None
    if df != definition or expected_object is not None and obj != expected_object or n != count:
        raise ValueError(f'Aircraft object/definition/count mismatch: received object={obj}, definition={df}, count={n}; expected object={expected_object}, definition={definition}, count={count}')
    needed = 256 if strings else count*8
    if len(raw) != 40+needed:
        raise ValueError('Unexpected aircraft packet length')
    if strings:
        return raw[40:].split(b'\0')[0].decode('utf-8', errors='strict')
    values = struct.unpack_from('<'+'d'*count, raw, 40)
    if not all(map(math.isfinite, values)):
        raise ValueError('Nonfinite aircraft data')
    return values


class Session:
    """Binds an explicit read-only native allowlist; cannot issue flight commands."""
    def __init__(self, sdk):
        self.dll_path = pathlib.Path(sdk)/'SimConnect SDK/lib/SimConnect.dll'
        self.dll = C.WinDLL(str(self.dll_path))
        self.handle, self.sequence, self.aircraft_object = C.c_void_p(), 100, None
        U, H, S = C.c_uint32, C.c_void_p, C.c_char_p
        def bind(name, args):
            fn = getattr(self.dll, 'SimConnect_'+name)
            fn.argtypes, fn.restype = args, C.c_int32
            return fn
        self.open = bind('Open', [C.POINTER(H), S, H, U, H, U])
        self.close = bind('Close', [H])
        self.add = bind('AddToDataDefinition', [H, U, S, S, U, C.c_float, U])
        self.request = bind('RequestDataOnSimObject', [H, U, U, U, U, U, U, U, U])
        self.dispatch = bind('GetNextDispatch', [H, C.POINTER(H), C.POINTER(U)])
        self.facilities = bind('RequestFacilitiesList', [H, U, U])
        self.define_facility = bind('AddToFacilityDefinition', [H, U, S])
        self.facility = bind('RequestFacilityData', [H, U, U, S, S])
        self.check(self.open(C.byref(self.handle), b'Frontier offshore read-only probe', None, 0, None, 0xffffffff))

    @staticmethod
    def check(hr):
        if hr < 0:
            raise OSError(f'SimConnect HRESULT 0x{hr & 0xffffffff:08X}')

    def next(self):
        ptr, size = C.c_void_p(), C.c_uint32()
        if self.dispatch(self.handle, C.byref(ptr), C.byref(size)) < 0:
            time.sleep(.01)
            return None
        if not 12 <= size.value <= 16000000:
            raise ValueError('Invalid dispatch size')
        raw = C.string_at(ptr, size.value)
        kind = struct.unpack_from('<I', raw, 8)[0]
        if struct.unpack_from('<I', raw)[0] != len(raw):
            raise ValueError('Declared packet size differs')
        if kind == 1:
            raise OSError('SimConnect exception: '+str(struct.unpack_from('<III', raw, 12)))
        if kind == 3:
            raise ConnectionError('Simulator disconnected')
        return kind, raw

    def numeric_definition(self, definition, fields):
        for name, unit in fields:
            self.check(self.add(self.handle, definition, name.encode(), unit.encode(), 4, 0, 0xffffffff))

    def data(self, definition, count, strings=False):
        if self.aircraft_object is None and (definition != 1 or not strings):
            raise ValueError('Read aircraft identity through proxy 0 before numeric data')
        self.sequence += 1
        request = self.sequence
        self.check(self.request(self.handle, request, definition, 0, 1, 0, 0, 0, 0))
        deadline = time.monotonic()+3
        while time.monotonic() < deadline:
            item = self.next()
            if item and item[0] == 8:
                result = decode_data(item[1], request, definition, count, strings, self.aircraft_object)
                if result is not None:
                    # Proxy 0 is the request target, not necessarily the actual returned object ID.
                    # Bind from that explicit aircraft TITLE response; every later reply must match.
                    if self.aircraft_object is None:
                        self.aircraft_object = struct.unpack_from('<I', item[1], 16)[0]
                    return result
        raise TimeoutError('Fresh aircraft read timed out')

    def discover(self, emit, radius):
        self.check(self.facilities(self.handle, 0, 700))
        packets, deadline = Airports(700), time.monotonic()+60
        while time.monotonic() < deadline:
            item = self.next()
            if item and item[0] == 18 and packets.add(item[1]):
                break
        else:
            raise TimeoutError('Worldwide list incomplete; absence cannot be concluded')
        origins = [r for r in packets.rows if r['ident'] == 'FYWB']
        if len(origins) != 1:
            raise ValueError('Expected unique simulator FYWB, not guessed coordinates')
        origin = origins[0]
        emit('origin', **origin, totalFacilities=len(packets.rows))
        nearby = []
        for row in packets.rows:
            distance = distance_nm((origin['latitude'], origin['longitude']), (row['latitude'], row['longitude']))
            if distance <= radius:
                nearby.append(dict(row, distanceNm=distance))
        for row in sorted(nearby, key=lambda r:r['distanceNm']):
            emit('nearby_facility', **row, qualification='unverified; not proof of a platform or collision')
        for field in ('OPEN AIRPORT', 'LATITUDE', 'LONGITUDE', 'ALTITUDE', 'N_HELIPADS',
                      'OPEN HELIPAD', 'LATITUDE', 'LONGITUDE', 'ALTITUDE', 'HEADING', 'LENGTH', 'WIDTH', 'SURFACE', 'TYPE', 'CLOSE HELIPAD', 'CLOSE AIRPORT'):
            self.check(self.define_facility(self.handle, 80, field.encode()))
        for row in sorted(nearby, key=lambda r:r['distanceNm']):
            self.sequence += 1
            request = self.sequence
            self.check(self.facility(self.handle, 80, request, row['ident'].encode(), row['region'].encode()))
            deadline, finished = time.monotonic()+5, False
            while time.monotonic() < deadline:
                item = self.next()
                if not item:
                    continue
                kind, raw = item
                # Keep exact wire replies: SDK header conditional PICK changes later enum numbering.
                # Parse by the complete request/structure shape, record kind; never a mission proof.
                if len(raw) >= 16 and struct.unpack_from('<I', raw, 12)[0] == request:
                    emit('facility_packet', ident=row['ident'], packetKind=kind, hex=raw.hex())
                    if kind in (29, 30) and len(raw) == 16:
                        finished = True
                        break
                    if kind in (28, 29) and len(raw) >= 40:
                        _, unique, parent, typ, listed, index, size = struct.unpack_from('<7I', raw, 12)
                        payload = raw[40:]
                        if typ == 0 and len(payload) in (28, 32):
                            lat, lon, altitude, count = struct.unpack_from('<dddI', payload)
                            emit('airport_detail', ident=row['ident'], latitude=lat, longitude=lon, altitudeMeters=altitude, helipadCount=count)
                        elif typ == 4 and len(payload) in (44, 48):
                            lat, lon, altitude, heading, length, width, surface, pad_type = struct.unpack_from('<dddfffii', payload)
                            if not all(map(math.isfinite, (lat,lon,altitude,heading,length,width))) or not -90<=lat<=90 or not -180<=lon<=180 or length<=0 or width<=0:
                                raise ValueError('Invalid helipad values; raw packet retained')
                            emit('helipad_candidate', ident=row['ident'], unique=unique, parent=parent, index=index, listSize=size, latitude=lat, longitude=lon, altitudeMeters=altitude, headingDegrees=heading, lengthMeters=length, widthMeters=width, surface=surface, type=pad_type, qualification='unverified; visual/contact inspection required')
                        else:
                            emit('unparsed_facility_payload', ident=row['ident'], type=typ, bytes=len(payload))
            if not finished:
                emit('facility_incomplete', ident=row['ident'], note='Cannot conclude no helipads')

    def record(self, emit, seconds):
        self.check(self.add(self.handle, 1, b'TITLE', None, 9, 0, 0xffffffff))
        self.numeric_definition(2, CORE)
        initial_title = self.data(1, 1, True)
        if not initial_title:
            raise ValueError('Aircraft TITLE missing')
        emit('aircraft_identity', title=initial_title, requestProxy=0, actualObjectId=self.aircraft_object)
        emit('fields', core=CORE, optionalCandidates=OPTIONAL, contactMapping='unqualified numeric indices, not identified skids')
        supported = []
        for i, field in enumerate(OPTIONAL):
            try:
                self.numeric_definition(1000+i, [field])
                value = self.data(1000+i, 1)[0]
                supported.append(field)
                emit('optional_probe', field=field, value=value, available=True)
            except (OSError, TimeoutError) as error:
                emit('optional_probe', field=field, available=False, problem=str(error))
        if supported:
            self.numeric_definition(3, supported)
        title, start = initial_title, time.monotonic()
        while time.monotonic()-start < seconds:
            before = self.data(1, 1, True)
            if not before or title is not None and before != title:
                raise ValueError('Aircraft title missing/changed; start a new recording')
            title = before
            sample_start = utc()
            core = dict(zip((f[0] for f in CORE), self.data(2, len(CORE))))
            optional = dict(zip((f[0] for f in supported), self.data(3, len(supported)))) if supported else {}
            if self.data(1, 1, True) != title:
                raise ValueError('Aircraft changed during sample')
            emit('aircraft_sample', title=title, aircraftProxy=0, actualObjectId=self.aircraft_object, startedUtc=sample_start, core=core, optional=optional, qualification='diagnostic only; no mission credit')
            time.sleep(.2)

    def dispose(self):
        if self.handle:
            self.close(self.handle)
            self.handle = C.c_void_p()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=['discover', 'record'])
    parser.add_argument('--sdk', required=True)
    parser.add_argument('--output', type=pathlib.Path, required=True)
    parser.add_argument('--seconds', type=int, default=120)
    parser.add_argument('--radius-nm', type=float, default=100)
    args = parser.parse_args()
    if not 1 <= args.seconds <= 7200 or not math.isfinite(args.radius_nm) or not 1 <= args.radius_nm <= 200:
        parser.error('Duration 1..7200 seconds; discovery radius 1..200 NM')
    # Create-only, never reads/writes Frontier saves or Community.
    with args.output.open('x', encoding='utf-8', buffering=1) as log:
        def emit(kind, **values):
            row = dict(kind=kind, utc=utc(), **values)
            log.write(json.dumps(row, allow_nan=False)+'\n')
            if kind != 'aircraft_sample':
                print(json.dumps(row, allow_nan=False), flush=True)
        emit('session_start', mode=args.mode, scriptSha256=hashlib.sha256(pathlib.Path(__file__).read_bytes()).hexdigest(), diagnostic=True)
        session = None
        try:
            session = Session(args.sdk)
            emit('sdk_binary', sha256=hashlib.sha256(session.dll_path.read_bytes()).hexdigest())
            if args.mode == 'discover':
                session.discover(emit, args.radius_nm)
            else:
                session.record(emit, args.seconds)
            emit('session_end', outcome='completed', liveLanding='unverified')
        except Exception as error:
            emit('session_error', problem=str(error), liveLanding='unverified')
            raise
        finally:
            if session:
                session.dispose()


if __name__ == '__main__':
    main()
