"""Fixed OL400 contact diagnostics. Repositioning, never flight/mission proof.

One placement followed by up to 35 seconds of real physics. No aircraft
controls, failures, payload, fuel, configuration, saves or economy access.
"""
import argparse
import ctypes as C
import hashlib
import json
import math
import pathlib
import time

from offshore_probe import Session, CORE, OPTIONAL, distance_nm, utc
from offshore_static_placement import PAD, DECK_METERS, InitialPosition

WATER = (PAD[0], PAD[1]-.001)  # about 102 m west, inspected separately
SCENARIOS = {'deck-drop': (PAD, DECK_METERS, 8.), 'nearby-water': (WATER, 0., 3.5),
             'under-deck-water': (PAD, 0., 3.5)}


def guard_start(title, state, scenario):
    if title != 'H125 Passengers' or scenario not in SCENARIOS:
        raise ValueError('Only the fixed H125/OL400 experiment is allowed')
    required = ('PLANE LATITUDE', 'PLANE LONGITUDE', 'PLANE ALTITUDE', 'GROUND VELOCITY',
                'IS SLEW ACTIVE', 'SIM ON GROUND', 'SIMULATION RATE',
                'PLANE BANK DEGREES', 'PLANE PITCH DEGREES')
    if (not all(math.isfinite(state[n]) for n in required) or
        state['IS SLEW ACTIVE'] not in (0, 1) or state['SIM ON GROUND'] not in (0, 1) or
        state['SIMULATION RATE'] != 1 or state['GROUND VELOCITY'] < 0 or
        abs(state['PLANE BANK DEGREES']) > 10 or abs(state['PLANE PITCH DEGREES']) > 10):
        raise ValueError('Invalid or unsuitable preflight telemetry')
    pos = (state['PLANE LATITUDE'], state['PLANE LONGITUDE'])
    on_pad = (distance_nm(pos, PAD)*1852 <= 5 and
              .3 <= state['PLANE ALTITUDE']*.3048-DECK_METERS <= 1.5)
    inspected_pad_slew = (state['IS SLEW ACTIVE'] == 1 and distance_nm(pos, PAD)*1852 <= 5 and
                          DECK_METERS-1 <= state['PLANE ALTITUDE']*.3048 <= DECK_METERS+5)
    from_water_slew = (scenario == 'deck-drop' and state['IS SLEW ACTIVE'] == 1 and
                       distance_nm(pos, WATER)*1852 <= 10)
    if not (on_pad or from_water_slew or inspected_pad_slew) or state['GROUND VELOCITY'] > .5:
        raise ValueError('Requires stationary inspected pad, or frozen water-test recovery to pad')
    if state['IS SLEW ACTIVE'] == 0 and state['SIM ON GROUND'] != 1:
        raise ValueError('Never seize control of an active flight')


def outside_envelope(core, scenario):
    target, elevation, _ = SCENARIOS[scenario]
    return (distance_nm((core['PLANE LATITUDE'], core['PLANE LONGITUDE']), target)*1852 > 10 or
            core['GROUND VELOCITY'] > 5 or abs(core['PLANE BANK DEGREES']) > 10 or
            abs(core['PLANE PITCH DEGREES']) > 10 or
            not elevation/.3048-5 <= core['PLANE ALTITUDE'] <= elevation/.3048+15 or
            core['IS SLEW ACTIVE'] != 0 or core['SIMULATION RATE'] != 1)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('scenario', choices=SCENARIOS)
    parser.add_argument('--sdk', required=True)
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    source = pathlib.Path(__file__).read_bytes()
    with pathlib.Path(args.output).open('x', encoding='utf-8', buffering=1) as log:
        with pathlib.Path(args.output+'.source.py').open('xb') as snapshot:
            snapshot.write(source)
        def emit(kind, **values):
            log.write(json.dumps(dict(kind=kind, utc=utc(), diagnosticOnly=True, **values), allow_nan=False)+'\n')
        emit('intent', scenario=args.scenario, sourceSha256=hashlib.sha256(source).hexdigest(),
             note='One diagnostic placement, never flight or mission evidence')
        session, transmitter, armed = Session(args.sdk), None, False
        try:
            session.check(session.add(session.handle, 1, b'TITLE', None, 9, 0, 0xffffffff))
            title = session.data(1, 1, True)
            session.numeric_definition(2, CORE)
            session.numeric_definition(3, OPTIONAL)
            def read():
                if session.data(1, 1, True) != title:
                    raise ValueError('Aircraft changed')
                return dict(zip((f[0] for f in CORE), session.data(2, len(CORE))))
            initial = read()
            guard_start(title, initial, args.scenario)
            emit('baseline', title=title, actualObjectId=session.aircraft_object, core=initial)
            mapper = session.dll.SimConnect_MapClientEventToSimEvent
            mapper.argtypes, mapper.restype = [C.c_void_p, C.c_uint32, C.c_char_p], C.c_int32
            transmitter = session.dll.SimConnect_TransmitClientEvent
            transmitter.argtypes, transmitter.restype = [C.c_void_p]+[C.c_uint32]*5, C.c_int32
            for ident, name in ((91, b'SLEW_OFF'), (92, b'SLEW_ON')):
                session.check(mapper(session.handle, ident, name))
                if session.data(1, 1, True) != title:
                    raise ValueError('Aircraft changed while mapping')
            def slew(ident):
                emit('slew_command', enabled=ident == 92)
                session.check(transmitter(session.handle, session.aircraft_object, ident, 0, 1, 16))
            armed = True
            slew(92)
            if read()['IS SLEW ACTIVE'] != 1:
                raise ValueError('Slew freeze not confirmed')
            session.check(session.add(session.handle, 90, b'Initial Position', None, 12, 0, 0xffffffff))
            setter = session.dll.SimConnect_SetDataOnSimObject
            setter.argtypes, setter.restype = [C.c_void_p]+[C.c_uint32]*5+[C.c_void_p], C.c_int32
            target, elevation, clearance = SCENARIOS[args.scenario]
            position = InitialPosition(*target, elevation/.3048+clearance, 0, 0, 180, 0, 0)
            slew(91)
            emit('placement', target=target, altitudeFeet=position.altitude, onGround=0)
            session.check(setter(session.handle, 90, session.aircraft_object, 0, 1, C.sizeof(position), C.byref(position)))
            end, previous, progress = time.monotonic()+35, None, time.monotonic()
            while time.monotonic() < end:
                core = read()
                optional = dict(zip((f[0] for f in OPTIONAL), session.data(3, len(OPTIONAL))))
                emit('aircraft_sample', title=title, actualObjectId=session.aircraft_object, core=core, optional=optional)
                if outside_envelope(core, args.scenario):
                    raise ValueError('Scenario envelope exceeded; restore slew, no success claimed')
                sim = core['SIMULATION TIME']
                if previous is not None and (sim < previous or sim-previous > .5):
                    raise ValueError('Simulation time reset/gap')
                if sim != previous:
                    progress = time.monotonic()
                elif time.monotonic()-progress > 2:
                    raise ValueError('Simulation stalled')
                previous = sim
                time.sleep(.025)
            emit('observation_end', note='Review actual samples independently; not mission proof')
        except Exception as error:
            emit('error', error=str(error))
            raise
        finally:
            try:
                if armed:
                    session.check(transmitter(session.handle, session.aircraft_object, 92, 0, 1, 16))
                    emit('slew_restore_submitted', note='Confirm using independent readback')
            finally:
                session.dispose()


if __name__ == '__main__':
    main()
