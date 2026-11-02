"""H125/OL400-only experimental controls. Diagnostic, never mission evidence.

Inspect and calibrate controls in stationary slew before any physics trial.
Hover mode has one labelled initial deck placement, then flight controls only.
No payload, failure, fuel, aircraft config or career-state writes.
"""
import argparse
import ctypes as C
import hashlib
import json
import pathlib
import time
import math

from offshore_probe import Session, CORE, OPTIONAL, utc, distance_nm
from offshore_static_placement import PAD, DECK_METERS, validate, InitialPosition

CONTROL_FIELDS = [('COLLECTIVE HANDLE POSITION', 'percent over 100'),
                  ('COLLECTIVE POSITION', 'percent over 100'),
                  ('ROTOR COLLECTIVE BLADE PITCH PCT', 'percent over 100'),
                  ('YOKE X POSITION', 'percent over 100'),
                  ('YOKE Y POSITION', 'percent over 100'),
                  ('TAIL ROTOR PEDAL POSITION', 'percent over 100'),
                  ('DISK BANK PCT:0', 'percent over 100'),
                  ('DISK PITCH PCT:0', 'percent over 100'),
                  ('DISK BANK PCT:1', 'percent over 100'),
                  ('DISK PITCH PCT:1', 'percent over 100'),
                  ('ROTOR CYCLIC BLADE PITCH PCT', 'percent over 100'),
                  ('ROTOR GOV SWITCH POS:1', 'bool'), ('ROTOR GOV ACTIVE:1', 'bool'),
                  ('ROTOR GOV TARGET PCT:1', 'percent over 100'),
                  ('ROTOR RPM PCT:1', 'percent over 100'),
                  ('GENERAL ENG RPM:1', 'rpm'), ('GENERAL ENG THROTTLE LEVER POSITION:1', 'percent'),
                  ('TOTAL WEIGHT', 'pounds'), ('EMPTY WEIGHT', 'pounds'),
                  ('FUEL TOTAL QUANTITY WEIGHT', 'pounds'), ('ROTOR BRAKE ACTIVE', 'bool'),
                  ('ROTOR BRAKE HANDLE POS', 'percent over 100'), ('ROTOR BRAKE AVAILABLE', 'bool'),
                  ('VELOCITY WORLD X', 'feet per second'),
                  ('VELOCITY WORLD Y', 'feet per second'),
                  ('VELOCITY WORLD Z', 'feet per second'),
                  ('ROTATION VELOCITY BODY X', 'radians per second'),
                  ('ROTATION VELOCITY BODY Y', 'radians per second'),
                  ('ROTATION VELOCITY BODY Z', 'radians per second')]
EVENTS = ('AXIS_COLLECTIVE_SET', 'AXIS_CYCLIC_LATERAL_SET',
          'AXIS_CYCLIC_LONGITUDINAL_SET', 'ROTOR_AXIS_TAIL_ROTOR_SET', 'SLEW_ON', 'SLEW_OFF')


def axis_value(fraction):
    if not 0 <= fraction <= 1:
        raise ValueError('Collective fraction out of range')
    return round(32768*fraction-16384)


def ground_guard(title, state):
    if title != 'H125 Passengers' or state['IS SLEW ACTIVE'] != 0 or state['SIM ON GROUND'] != 1:
        raise ValueError('Requires normal-physics H125 ground contact')
    offset = distance_nm((state['PLANE LATITUDE'], state['PLANE LONGITUDE']), PAD)*1852
    if (offset > 5 or state['GROUND VELOCITY'] > 1 or abs(state['PLANE BANK DEGREES']) > 5 or
        abs(state['PLANE PITCH DEGREES']) > 5 or
        not .3 <= state['PLANE ALTITUDE']*.3048-DECK_METERS <= 1.5):
        raise ValueError('Outside stationary deck calibration envelope')


def clamp(value, limit):
    return max(-limit, min(limit, value))


def hover_guard(state):
    offset = distance_nm((state['PLANE LATITUDE'], state['PLANE LONGITUDE']), PAD)*1852
    if (offset > 7 or state['GROUND VELOCITY'] > 5 or
        abs(state['PLANE BANK DEGREES']) > 12 or abs(state['PLANE PITCH DEGREES']) > 12 or
        not DECK_METERS/.3048-2 <= state['PLANE ALTITUDE'] <= DECK_METERS/.3048+20 or
        state['IS SLEW ACTIVE'] != 0 or state['SIMULATION RATE'] != 1):
        raise ValueError('Hover envelope exceeded; abort, no landing proof')


def airborne_hold_eligible(core, optional, baseline):
    return (core['SIM ON GROUND'] == 0 and core['IS SLEW ACTIVE'] == 0 and
            1 <= core['PLANE ALTITUDE']-baseline <= 5 and
            core['GROUND VELOCITY'] <= .5 and abs(core['VERTICAL SPEED']) <= .3 and
            all(optional.get(f'CONTACT POINT IS ON GROUND:{i}') == 0 for i in range(20)))


class HoverLaw:
    """Bounded experimental feedback. Not a qualified aircraft autopilot."""
    def __init__(self, baseline, heading):
        self.baseline, self.heading = baseline, heading
        self.collective, self.base = 0., None
        self.previous = None
        self.integral = 0.

    def update(self, state, dt, target, landed):
        if not .001 <= dt <= .5:
            raise ValueError('Telemetry interval outside control limits')
        pitch, bank, yaw = (state[n] for n in ('PLANE PITCH DEGREES', 'PLANE BANK DEGREES', 'PLANE HEADING DEGREES TRUE'))
        yaw_error = (yaw-self.heading+180)%360-180
        dp, db, dy = (0.,0.,0.) if self.previous is None else (
            (pitch-self.previous[0])/dt, (bank-self.previous[1])/dt,
            ((yaw-self.previous[2]+180)%360-180)/dt)
        self.previous = (pitch, bank, yaw)
        heading = math.radians(yaw)
        east = math.radians(state['PLANE LONGITUDE']-PAD[1])*6371000*math.cos(math.radians(PAD[0]))
        north = math.radians(state['PLANE LATITUDE']-PAD[0])*6371000
        vx, vz = state['VELOCITY WORLD X']*.3048, state['VELOCITY WORLD Z']*.3048
        forward = vx*math.sin(heading)+vz*math.cos(heading)
        right = vx*math.cos(heading)-vz*math.sin(heading)
        ahead = east*math.sin(heading)+north*math.cos(heading)
        aside = east*math.cos(heading)-north*math.sin(heading)
        desired_pitch = clamp(-1.2*forward-.4*ahead, 4)
        desired_bank = clamp(1.2*right+.4*aside, 4)
        x = clamp(-.04*(bank-desired_bank)-.018*db, .35)
        y = clamp(-.04*(pitch-desired_pitch)-.018*dp, .35)
        pedal = clamp(.012*yaw_error+.008*dy, .5)
        if self.base is None:
            if landed:
                self.collective = min(.65, self.collective+.008*dt)
            else:
                self.base = self.collective
        if self.base is not None:
            desired_v = clamp(.5*(target-state['PLANE ALTITUDE']), .7)
            error_v = desired_v-state['VERTICAL SPEED']
            self.integral = clamp(self.integral+.01*dt*error_v, .15)
            wanted = max(0, min(.7, self.base+.04*error_v+self.integral))
            self.collective += clamp(wanted-self.collective, .08*dt)
        return self.collective, x, y, pedal


def run_hover(session, controls, read, detail, emit, stage):
    # Staging is explicitly diagnostic and precedes every measured flight phase.
    controls.send('AXIS_COLLECTIVE_SET', axis_value(0))
    controls.send('AXIS_CYCLIC_LATERAL_SET', 0)
    controls.send('AXIS_CYCLIC_LONGITUDINAL_SET', 0)
    controls.send('ROTOR_AXIS_TAIL_ROTOR_SET', 0)
    if stage:
        session.check(session.add(session.handle, 90, b'Initial Position', None, 12, 0, 0xffffffff))
        setter = session.dll.SimConnect_SetDataOnSimObject
        setter.argtypes, setter.restype = [C.c_void_p]+[C.c_uint32]*5+[C.c_void_p], C.c_int32
        controls.send('SLEW_OFF', 0)
        position = InitialPosition(PAD[0], PAD[1], DECK_METERS/.3048+3.5, 0, 0, 180, 0, 0)
        emit('staging_placement', note='One initial placement; NOT takeoff or touchdown proof')
        session.check(setter(session.handle, 90, session.aircraft_object, 0, 1, C.sizeof(position), C.byref(position)))
    else:
        emit('no_placement', note='Starting from actual ground state; no position/slew-off write')
    session.numeric_definition(4, OPTIONAL)
    start_wall, previous_sim, progress_wall = time.monotonic(), None, time.monotonic()
    phase, phase_sim, baseline, law, hover_start = 'settle', None, None, None, None
    while time.monotonic()-start_wall < 180:
        core, values = read(), detail()
        optional = dict(zip((f[0] for f in OPTIONAL), session.data(4, len(OPTIONAL))))
        emit('frame', phase=phase, core=core)
        hover_guard(core)
        if not 340 <= optional['ROTOR RPM:1'] <= 450:
            raise ValueError('Rotor RPM outside trial envelope')
        sim = core['SIMULATION TIME']
        if previous_sim is None:
            previous_sim, phase_sim = sim, sim
            continue
        dt = sim-previous_sim
        if dt == 0:
            if time.monotonic()-progress_wall > 2:
                raise ValueError('Simulation paused/stalled; abort')
            time.sleep(.02)
            continue
        progress_wall, previous_sim = time.monotonic(), sim
        if not .001 <= dt <= .5:
            raise ValueError('Stale/gapped telemetry; abort')
        emit('observation', phase=phase, core=core, controls=values, optional=optional)
        if phase == 'settle':
            # A placement can bounce before settling. Never count staging as
            # takeoff/landing; require a new uninterrupted ground baseline.
            if core['SIM ON GROUND'] != 1:
                phase_sim = sim
                continue
            ground_guard('H125 Passengers', core)
            if sim-phase_sim < 5:
                continue
            baseline = core['PLANE ALTITUDE']
            law = HoverLaw(baseline, core['PLANE HEADING DEGREES TRUE'])
            phase, phase_sim = 'lift', sim
            emit('phase', name=phase, baselineAltitudeFeet=baseline)
        if phase == 'lift' and core['SIM ON GROUND'] == 0:
            phase, phase_sim = 'hover', sim
            emit('phase', name=phase, note='Observed airborne, not yet controlled hover acceptance')
        if phase == 'hover':
            if airborne_hold_eligible(core, optional, baseline):
                if hover_start is None:
                    hover_start = sim
                if sim-hover_start >= 15:
                    phase, phase_sim = 'descend', sim
                    emit('phase', name=phase, controlledAirborneSeconds=sim-hover_start)
            else:
                hover_start = None
        if phase == 'descend' and core['SIM ON GROUND'] == 1:
            phase, phase_sim = 'rest', sim
            emit('phase', name=phase, note='Contact after airborne segment; requires independent evidence review')
        if phase == 'rest':
            ground_guard('H125 Passengers', core)
            controls.send('AXIS_COLLECTIVE_SET', axis_value(0))
            if sim-phase_sim >= 20:
                emit('trial_complete', note='Review raw trajectory/contacts before acceptance; no game proof')
                return
            continue
        target = baseline+3 if phase != 'descend' else max(baseline-.15, baseline+3-.3*(sim-phase_sim))
        combined = dict(core, **values)
        collective, x, y, pedal = law.update(combined, dt, target, core['SIM ON GROUND'] == 1)
        controls.send('AXIS_COLLECTIVE_SET', axis_value(collective))
        controls.send('AXIS_CYCLIC_LATERAL_SET', round(x*16384))
        controls.send('AXIS_CYCLIC_LONGITUDINAL_SET', round(y*16384))
        controls.send('ROTOR_AXIS_TAIL_ROTOR_SET', round(pedal*16384))
    raise TimeoutError('Bounded trial expired; no success claimed')


class Controls:
    def __init__(self, session, emit):
        self.session, self.emit = session, emit
        mapper = session.dll.SimConnect_MapClientEventToSimEvent
        mapper.argtypes, mapper.restype = [C.c_void_p, C.c_uint32, C.c_char_p], C.c_int32
        self.transmit = session.dll.SimConnect_TransmitClientEvent
        self.transmit.argtypes, self.transmit.restype = [C.c_void_p]+[C.c_uint32]*5, C.c_int32
        for index, event in enumerate(EVENTS):
            emit('mapping', event=event)
            session.check(mapper(session.handle, 100+index, event.encode()))
            # Surface asynchronous mapping errors before sending any controls.
            if session.data(1, 1, True) != 'H125 Passengers':
                raise ValueError('Aircraft changed while mapping controls')

    def send(self, name, value):
        if name not in EVENTS or not isinstance(value, int) or not -16384 <= value <= 16384:
            raise ValueError('Command outside allowlist/range')
        self.emit('control', event=name, value=value)
        self.session.check(self.transmit(self.session.handle, self.session.aircraft_object,
                                        100+EVENTS.index(name), value & 0xffffffff, 1, 16))


def main():
    global PAD, DECK_METERS
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=['status', 'inspect', 'calibrate', 'ground-calibrate', 'hover'])
    parser.add_argument('--site', choices=['OL400', 'FYWB'], default='OL400')
    parser.add_argument('--sdk', required=True)
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    with pathlib.Path(args.output).open('x', encoding='utf-8', buffering=1) as log:
        def emit(kind, **values):
            row = dict(kind=kind, utc=utc(), diagnosticOnly=True, **values)
            log.write(json.dumps(row, allow_nan=False)+'\n')
            print(json.dumps(row, allow_nan=False), flush=True)
        source = pathlib.Path(__file__).read_bytes()
        with pathlib.Path(args.output+'.source.py').open('xb') as snapshot:
            snapshot.write(source)
        emit('start', mode=args.mode, site=args.site, sourceSha256=hashlib.sha256(source).hexdigest(),
             recorderSha256=hashlib.sha256(pathlib.Path(__file__).with_name('offshore_probe.py').read_bytes()).hexdigest())
        session, controls, initial = Session(args.sdk), None, None
        try:
            session.check(session.add(session.handle, 1, b'TITLE', None, 9, 0, 0xffffffff))
            title = session.data(1, 1, True)
            session.numeric_definition(2, CORE)
            def read():
                if session.data(1, 1, True) != title:
                    raise ValueError('Aircraft changed')
                state = dict(zip((f[0] for f in CORE), session.data(2, len(CORE))))
                return state
            start = read()
            if title != 'H125 Passengers':
                raise ValueError('Only inspected H125 title allowed')
            if args.site == 'FYWB':
                PAD = (-22.977764792740345, 14.64223399758339)
                if distance_nm((start['PLANE LATITUDE'],start['PLANE LONGITUDE']), PAD)*1852 > 5 or not 80 <= start['GROUND ALTITUDE']*.3048 <= 85:
                    raise ValueError('Not the discovered FYWB H01 ground baseline')
                DECK_METERS = start['GROUND ALTITUDE']*.3048
                if args.mode not in ('status','hover'):
                    raise ValueError('FYWB supports read-only status or non-placement hover only')
            guard = ground_guard if args.mode == 'ground-calibrate' or args.site == 'FYWB' else validate
            if args.mode != 'status':
                guard(title, start)
            supported = []
            for index, field in enumerate(CONTROL_FIELDS):
                try:
                    session.numeric_definition(200+index, [field])
                    value = session.data(200+index, 1)[0]
                    supported.append(field)
                    emit('field', name=field, value=value)
                except (OSError, TimeoutError) as error:
                    emit('unsupported', name=field, error=str(error))
            session.numeric_definition(3, supported)
            def detail():
                return dict(zip((f[0] for f in supported), session.data(3, len(supported))))
            initial = detail()
            emit('baseline', core=read(), controls=initial)
            if args.mode in ('inspect', 'status'):
                return
            required = ['COLLECTIVE HANDLE POSITION', 'TAIL ROTOR PEDAL POSITION']
            if any(n not in initial or not -1 <= initial[n] <= 1 for n in required) or initial[required[0]] < 0:
                raise ValueError('Cannot interpret/restore original controls')
            if args.mode == 'hover' and not .87 <= initial.get('ROTOR RPM PCT:1', 0) <= 1.15:
                raise ValueError('Rotor speed unsuitable before trial; no controls sent')
            controls = Controls(session, emit)
            if args.mode == 'hover':
                run_hover(session, controls, read, detail, emit, args.site == 'OL400')
                return
            sequence = [('AXIS_COLLECTIVE_SET', axis_value(0)),
                        ('AXIS_COLLECTIVE_SET', axis_value(.05)),
                        ('AXIS_COLLECTIVE_SET', axis_value(.25)),
                        ('AXIS_CYCLIC_LATERAL_SET', 819), ('AXIS_CYCLIC_LATERAL_SET', -819),
                        ('AXIS_CYCLIC_LATERAL_SET', 0),
                        ('AXIS_CYCLIC_LONGITUDINAL_SET', 819), ('AXIS_CYCLIC_LONGITUDINAL_SET', -819),
                        ('AXIS_CYCLIC_LONGITUDINAL_SET', 0),
                        ('ROTOR_AXIS_TAIL_ROTOR_SET', 819), ('ROTOR_AXIS_TAIL_ROTOR_SET', -819), ('ROTOR_AXIS_TAIL_ROTOR_SET', 0),
                        ('AXIS_COLLECTIVE_SET', axis_value(0))]
            for event, value in sequence:
                guard(title, read())
                controls.send(event, value)
                time.sleep(.8 if args.mode == 'ground-calibrate' else .3)
                emit('calibration', core=read(), controls=detail())
        except Exception as error:
            emit('error', error=str(error))
            raise
        finally:
            try:
                if controls is not None:
                    controls.send('SLEW_ON', 0)
                    controls.send('AXIS_COLLECTIVE_SET', axis_value(initial['COLLECTIVE HANDLE POSITION']))
                    # Generic YOKE readback is not verified helicopter input state.
                    controls.send('AXIS_CYCLIC_LATERAL_SET', 0)
                    controls.send('AXIS_CYCLIC_LONGITUDINAL_SET', 0)
                    controls.send('ROTOR_AXIS_TAIL_ROTOR_SET', -round(initial['TAIL ROTOR PEDAL POSITION']*16384))
            finally:
                session.dispose()


if __name__ == '__main__':
    main()
