"""Fixed OL400/H125 constrained vertical diagnostic, NOT freely flown takeoff proof.

SDK-documented lat/lon and attitude freeze toggles are verified on the ground.
Only collective then drives vertical physics. No position, payload, fuel, faults,
camera or career setters. Abort freezes in slew; identity mismatch never controls
a newly selected aircraft. Exact artifact and live-scenario approval required.
"""
import argparse
import ctypes as C
import hashlib
import json
import math
import pathlib
import time

from offshore_probe import Session, CORE, OPTIONAL, distance_nm, utc
from offshore_static_placement import PAD, DECK_METERS
from offshore_hover_test import HoverLaw, axis_value, CONTROL_FIELDS

FREEZES = [('IS LATITUDE LONGITUDE FREEZE ON', 'bool'),
           ('IS ATTITUDE FREEZE ON', 'bool'), ('IS ALTITUDE FREEZE ON', 'bool')]
DETAILS = [field for field in CONTROL_FIELDS if field[0] in (
    'COLLECTIVE HANDLE POSITION', 'ROTOR RPM PCT:1', 'VELOCITY WORLD X', 'VELOCITY WORLD Z')] + FREEZES
EVENTS = ('FREEZE_LATITUDE_LONGITUDE_TOGGLE', 'FREEZE_ATTITUDE_TOGGLE',
          'AXIS_COLLECTIVE_SET', 'SLEW_ON')


def guard(core, detail, contacts, constrained, on_deck=False):
    names = [x[0] for x in CORE] + [x[0] for x in FREEZES] + [
        'COLLECTIVE HANDLE POSITION', 'ROTOR RPM PCT:1']
    state = dict(core, **detail)
    if any(n not in state or not math.isfinite(state[n]) for n in names):
        raise ValueError('Missing/nonfinite native telemetry')
    if (core['IS SLEW ACTIVE'] != 0 or core['SIMULATION RATE'] != 1 or
        detail[FREEZES[2][0]] != 0 or
        any(detail[n] != int(constrained) for n, _ in FREEZES[:2])):
        raise ValueError('Unexpected slew/rate/freeze state')
    if (not 0 <= detail['COLLECTIVE HANDLE POSITION'] <= .7 or
        not .87 <= detail['ROTOR RPM PCT:1'] <= 1.15 or
        not all(contacts.get(f'CONTACT POINT IS ON GROUND:{i}') in (0, 1) for i in range(20))):
        raise ValueError('Invalid control/rotor/contact state')
    height = core['PLANE ALTITUDE'] * .3048 - DECK_METERS
    if (distance_nm((core['PLANE LATITUDE'], core['PLANE LONGITUDE']), PAD) * 1852 > 2 or
        core['GROUND VELOCITY'] > 1 or abs(core['VERTICAL SPEED']) > 3 or
        abs(core['PLANE BANK DEGREES']) > 5 or
        abs(core['PLANE PITCH DEGREES']) > 5 or not .2 <= height <= 4):
        raise ValueError('Constrained diagnostic envelope exceeded')
    if on_deck and (core['SIM ON GROUND'] != 1 or not .3 <= height <= 1.5 or
                    core['GROUND VELOCITY'] > .5 or abs(core['VERTICAL SPEED']) > .1 or
                    abs(core['GROUND ALTITUDE'] * .3048 - DECK_METERS) > .5 or
                    not any(contacts[f'CONTACT POINT IS ON GROUND:{i}'] == 1 for i in range(20))):
        raise ValueError('Requires actual stationary contact on the inspected OL400 deck')


def clear_above_deck(core, contacts):
    return (core['SIM ON GROUND'] == 0 and core['PLANE ALTITUDE'] * .3048 - DECK_METERS > 1.5 and
            all(contacts.get(f'CONTACT POINT IS ON GROUND:{i}') == 0 for i in range(20)))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--sdk', required=True)
    parser.add_argument('--output', required=True)
    parser.add_argument('--confirm-constrained-diagnostic', action='store_true', required=True)
    args = parser.parse_args()
    with pathlib.Path(args.output).open('x', encoding='utf-8', buffering=1) as log:
        def emit(kind, **values):
            log.write(json.dumps(dict(kind=kind, utc=utc(), diagnosticOnly=True, constrained=True, **values), allow_nan=False) + '\n')
            log.flush()
        sources = ('offshore_vertical_contact_test.py', 'offshore_probe.py', 'offshore_static_placement.py', 'offshore_hover_test.py')
        emit('intent', sources={n: hashlib.sha256(pathlib.Path(__file__).with_name(n).read_bytes()).hexdigest() for n in sources},
             note='No free-flight/takeoff/route/mission payment qualification. Latitude/longitude and attitude constrained.')
        session, send, initial, modified, success = Session(args.sdk), None, None, False, False
        try:
            session.check(session.add(session.handle, 1, b'TITLE', None, 9, 0, 0xffffffff))
            if session.data(1, 1, True) != 'H125 Passengers':
                raise ValueError('Only actual H125 Passengers allowed')
            pinned = session.aircraft_object
            session.numeric_definition(2, CORE)
            session.numeric_definition(3, DETAILS)
            session.numeric_definition(4, OPTIONAL)
            def read():
                if session.data(1, 1, True) != 'H125 Passengers' or session.aircraft_object != pinned:
                    raise ValueError('Actual aircraft identity changed')
                core = dict(zip((f[0] for f in CORE), session.data(2, len(CORE))))
                detail = dict(zip((f[0] for f in DETAILS), session.data(3, len(DETAILS))))
                contacts = dict(zip((f[0] for f in OPTIONAL), session.data(4, len(OPTIONAL))))
                emit('sample', actualObjectId=pinned, core=core, detail=detail, contacts=contacts)
                return core, detail, contacts
            core, initial, contacts = read()
            guard(core, initial, contacts, False, True)
            mapper = session.dll.SimConnect_MapClientEventToSimEvent
            mapper.argtypes, mapper.restype = [C.c_void_p, C.c_uint32, C.c_char_p], C.c_int32
            transmit = session.dll.SimConnect_TransmitClientEvent
            transmit.argtypes, transmit.restype = [C.c_void_p] + [C.c_uint32] * 5, C.c_int32
            for index, name in enumerate(EVENTS):
                session.check(mapper(session.handle, 500 + index, name.encode()))
                read()  # surface asynchronous errors before control authority
            def command(name, value=0):
                if name not in EVENTS or not isinstance(value, int) or not -16384 <= value <= 16384:
                    raise ValueError('Command outside fixed allowlist')
                # Fresh explicit proxy identity must still return the pinned native object.
                if session.data(1, 1, True) != 'H125 Passengers' or session.aircraft_object != pinned:
                    raise ValueError('Aircraft changed before command')
                emit('command_intent', event=name, value=value, actualObjectId=pinned)
                session.check(transmit(session.handle, pinned, 500 + EVENTS.index(name), value & 0xffffffff, 1, 16))
            send = command
            # Toggle only after known false baseline; readback, never assumed boolean SET parameters.
            guard(*read(), False, True)
            modified = True
            send(EVENTS[0]); send(EVENTS[1])
            core, detail, contacts = read()
            guard(core, detail, contacts, True, True)
            emit('constraints_confirmed', note='Altitude freeze remains OFF; real vertical/contact telemetry must still change.')
            law = HoverLaw(core['PLANE ALTITUDE'], core['PLANE HEADING DEGREES TRUE'])
            baseline = core['PLANE ALTITUDE']; phase = 'baseline'; phase_sim = core['SIMULATION TIME']
            previous = core['SIMULATION TIME']; progress_wall = time.monotonic(); start_wall = progress_wall; clear_since = None
            while time.monotonic() - start_wall < 150:
                time.sleep(.05)
                core, detail, contacts = read(); guard(core, detail, contacts, True)
                sim = core['SIMULATION TIME']; dt = sim - previous
                if dt == 0:
                    if time.monotonic() - progress_wall > 2:
                        raise TimeoutError('Simulation stopped/paused; abort')
                    continue
                if not .001 <= dt <= .5:
                    raise ValueError('Discontinuous control sample')
                previous = sim; progress_wall = time.monotonic()
                if phase == 'baseline':
                    guard(core, detail, contacts, True, True)
                    if sim - phase_sim >= 25:
                        phase, phase_sim = 'lift', sim; emit('phase', name=phase)
                    else:
                        continue
                if phase == 'lift':
                    if clear_above_deck(core, contacts):
                        clear_since = sim if clear_since is None else clear_since
                        if sim - clear_since >= 5:
                            phase, phase_sim = 'descend', sim; emit('phase', name=phase)
                    else:
                        clear_since = None
                if phase == 'descend' and core['SIM ON GROUND'] == 1:
                    phase, phase_sim = 'rest', sim; send('AXIS_COLLECTIVE_SET', axis_value(0)); emit('phase', name=phase)
                if phase == 'rest':
                    guard(core, detail, contacts, True, True)
                    if sim - phase_sim >= 25:
                        success = True; emit('physical_cycle_complete', note='Constrained contact cycle only; independent .NET recording decides diagnostic milestones.')
                        break
                    continue
                target = baseline + 6 if phase == 'lift' else max(baseline - .3, core['PLANE ALTITUDE'] - .5)
                collective, _, _, _ = law.update(dict(core, **detail), dt, target, core['SIM ON GROUND'] == 1)
                send('AXIS_COLLECTIVE_SET', axis_value(collective))
            else:
                raise TimeoutError('Bounded 150 second test expired')
        except Exception as error:
            emit('error', error=str(error)); raise
        finally:
            try:
                if modified and send is not None:
                    if not success:
                        send('SLEW_ON')  # stopped diagnostic, never successful takeoff/return
                    # Read actual flags before any restoring toggle; do not blindly toggle after partial failure.
                    _, detail, _ = read()
                    send('AXIS_COLLECTIVE_SET', axis_value(initial['COLLECTIVE HANDLE POSITION']))
                    for (name, _), event in zip(FREEZES[:2], EVENTS[:2]):
                        if detail[name] not in (0, 1):
                            raise ValueError('Unknown freeze state; manual recovery required')
                        if detail[name] != initial[name]:
                            send(event)
                    _, final, _ = read()
                    if any(final[name] != initial[name] for name, _ in FREEZES):
                        raise ValueError('Freeze restoration failed; stop and inspect')
                    emit('restored', abortedInSlew=not success, collective=initial['COLLECTIVE HANDLE POSITION'])
            except Exception as error:
                emit('recovery_unresolved', error=str(error), instruction='Stop; inspect actual simulator state. Never control a replacement aircraft or blindly retry.')
                raise
            finally:
                session.dispose()


if __name__ == '__main__':
    main()
