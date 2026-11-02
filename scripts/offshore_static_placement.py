"""One-shot OL400 inspection placement. NEVER flight or mission evidence.

Separate from the read-only recorder. Requires an H125 already in slew within
500 m of the live-discovered OL400 reference. No faults, payload, engine events,
career state, continuous motion, or arbitrary positioning interface.
"""
import argparse
import ctypes as C
import hashlib
import json
import pathlib
import time

from offshore_probe import Session, CORE, distance_nm, utc

PAD = (-22.93892875313759, 14.50461745262146)
REFERENCE = (-22.938931100070477, 14.50408548116684)
DECK_METERS = 39.89600372314453


class InitialPosition(C.Structure):
    _fields_ = [(n, C.c_double) for n in
                ('latitude', 'longitude', 'altitude', 'pitch', 'bank', 'heading')]+[
                    ('on_ground', C.c_uint32), ('airspeed', C.c_uint32)]


def validate(title, state):
    if title != 'H125 Passengers':
        raise ValueError('Only the inspected H125 Passengers is allowed')
    if state['IS SLEW ACTIVE'] != 1 or state['GROUND VELOCITY'] > .1:
        raise ValueError('Requires stationary slew inspection, never active flight')
    if distance_nm((state['PLANE LATITUDE'], state['PLANE LONGITUDE']), REFERENCE)*1852 > 500:
        raise ValueError('Requires prior UI placement at OL400')


def outside_release_envelope(state):
    offset = distance_nm((state['PLANE LATITUDE'], state['PLANE LONGITUDE']), PAD)*1852
    return (offset > 10 or state['GROUND VELOCITY'] > 5 or
            abs(state['PLANE BANK DEGREES']) > 10 or abs(state['PLANE PITCH DEGREES']) > 10 or
            state['PLANE ALTITUDE'] < DECK_METERS/.3048-3 or
            state['PLANE ALTITUDE'] > DECK_METERS/.3048+15)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--sdk', required=True)
    parser.add_argument('--output', required=True)
    parser.add_argument('--release-with-watchdog', action='store_true',
                        help='Release slew BEFORE the single placement, observe physics, restore slew on exit')
    args = parser.parse_args()
    # Create-only durable intent before any simulator mutation.
    with pathlib.Path(args.output).open('x', encoding='utf-8') as log:
        def emit(kind, **values):
            line = dict(kind=kind, utc=utc(), diagnosticOnly=True, **values)
            log.write(json.dumps(line)+'\n')
            log.flush()
            print(json.dumps(line), flush=True)
        emit('placement_intent', scriptSha256=hashlib.sha256(pathlib.Path(__file__).read_bytes()).hexdigest(),
             pad=PAD, deckMeters=DECK_METERS, clearanceFeet=3.5 if args.release_with_watchdog else 8,
             note='Repositioned inspection; cannot qualify flight or landing')
        session = Session(args.sdk)
        restore_slew = None
        try:
            session.check(session.add(session.handle, 1, b'TITLE', None, 9, 0, 0xffffffff))
            title = session.data(1, 1, True)
            session.numeric_definition(2, CORE)
            state = dict(zip((f[0] for f in CORE), session.data(2, len(CORE))))
            validate(title, state)
            emit('guarded_start', title=title, actualObjectId=session.aircraft_object, state=state)
            setter = session.dll.SimConnect_SetDataOnSimObject
            setter.argtypes = [C.c_void_p]+[C.c_uint32]*5+[C.c_void_p]
            setter.restype = C.c_int32
            session.check(session.add(session.handle, 90, b'Initial Position', None, 12, 0, 0xffffffff))
            position = InitialPosition(PAD[0], PAD[1], DECK_METERS/.3048+(3.5 if args.release_with_watchdog else 8), 0, 0, 180, 0, 0)
            if args.release_with_watchdog:
                mapper = session.dll.SimConnect_MapClientEventToSimEvent
                mapper.argtypes, mapper.restype = [C.c_void_p, C.c_uint32, C.c_char_p], C.c_int32
                transmitter = session.dll.SimConnect_TransmitClientEvent
                transmitter.argtypes, transmitter.restype = [C.c_void_p]+[C.c_uint32]*5, C.c_int32
                for ident, name in ((91, b'SLEW_OFF'), (92, b'SLEW_ON')):
                    session.check(mapper(session.handle, ident, name))
                def restore_slew():
                    session.check(transmitter(session.handle, 0, 92, 0, 1, 16))
                    emit('slew_restore_submitted', note='Command submitted; confirm using independent recorder/UI, not mission evidence')
                emit('release_requested', note='SLEW_OFF then single placement; abort restores SLEW_ON')
                session.check(transmitter(session.handle, 0, 91, 0, 1, 16))
            # Exactly one full INITPOSITION write, user proxy 0, per process.
            session.check(setter(session.handle, 90, 0, 0, 1, C.sizeof(position), C.byref(position)))
            emit('placement_submitted', altitudeFeet=position.altitude, onGround=0,
                 note='HRESULT is not readback or collision confirmation', released=args.release_with_watchdog)
            started = time.monotonic()
            for _ in range(600 if args.release_with_watchdog else 5):
                if session.data(1, 1, True) != title:
                    raise ValueError('Aircraft title changed after placement')
                state = dict(zip((f[0] for f in CORE), session.data(2, len(CORE))))
                emit('readback', state=state)
                if args.release_with_watchdog:
                    if outside_release_envelope(state):
                        raise ValueError('Static-release envelope exceeded; abort, not landing proof')
                    if time.monotonic()-started > 35:
                        break
                time.sleep(.05 if args.release_with_watchdog else .5)
        except Exception as error:
            emit('error', error=str(error))
            raise
        finally:
            try:
                if restore_slew is not None:
                    restore_slew()
            finally:
                session.close(session.handle)


if __name__ == '__main__':
    main()
