"""Read-only live/offline rest diagnostics, NEVER touchdown/flight/mission proof.

Thresholds below are observation filters for this one H125/OL400 experiment.
Contact indices remain unmapped; a candidate needs external visual/physics
review. In particular a spawned or repositioned aircraft can satisfy them.
"""
import argparse
import datetime
import hashlib
import json
import math
import pathlib

from offshore_probe import distance_nm


class RestObserver:
    def __init__(self, pad, deck_meters, title='H125 Passengers'):
        self.pad, self.deck_meters, self.title = pad, deck_meters, title
        self.previous = self.start = None
        self.longest = 0
        self.samples = 0
        self.current = 0

    def add(self, row):
        self.current = 0
        try:
            core, optional = row['core'], row['optional']
            wall = datetime.datetime.fromisoformat(row['utc']).timestamp()
            sim = core['SIMULATION TIME']
            identity = (row['actualObjectId'], row['title'])
            fields = ['PLANE LATITUDE', 'PLANE LONGITUDE', 'PLANE ALTITUDE',
                      'GROUND VELOCITY', 'VERTICAL SPEED', 'SIM ON GROUND',
                      'IS SLEW ACTIVE', 'SIMULATION RATE', 'SIMULATION TIME']
            if not all(math.isfinite(core[n]) for n in fields) or not math.isfinite(wall):
                raise ValueError('Nonfinite observation')
            contact = any(optional.get(f'CONTACT POINT IS ON GROUND:{i}') == 1 for i in range(20))
            altitude_offset = core['PLANE ALTITUDE']*.3048-self.deck_meters
            valid = (row['title'] == self.title and core['IS SLEW ACTIVE'] == 0 and
                     core['SIMULATION RATE'] == 1 and core['SIM ON GROUND'] == 1 and contact and
                     0 <= core['GROUND VELOCITY'] <= .5 and abs(core['VERTICAL SPEED']) <= .1 and
                     .3 <= altitude_offset <= 1.5 and
                     distance_nm((core['PLANE LATITUDE'], core['PLANE LONGITUDE']), self.pad)*1852 <= 8)
            consecutive = (self.previous is not None and identity == self.previous[2] and
                           0 < wall-self.previous[0] <= 1 and 0 < sim-self.previous[1] <= 1)
            self.previous = (wall, sim, identity)
            if not valid:
                self.start = None
                return 0
            if not consecutive or self.start is None:
                self.start = sim
            duration = sim-self.start
            self.current = duration
            self.longest = max(self.longest, duration)
            self.samples += 1
            return duration
        except (KeyError, ValueError, TypeError, OverflowError):
            self.previous = self.start = None
            return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('recording')
    parser.add_argument('--live-sdk', help='Read actual simulator data and create a new recording; no writes to MSFS')
    parser.add_argument('--seconds', type=int, default=40)
    args = parser.parse_args()
    if not 1 <= args.seconds <= 300:
        parser.error('Live observation is bounded to 1..300 seconds')
    observer = RestObserver((-22.93892875313759, 14.50461745262146), 39.89600372314453)
    if args.live_sdk:
        from offshore_probe import Session, utc
        with pathlib.Path(args.recording).open('x', encoding='utf-8', buffering=1) as recording:
            last_present = None
            def emit(kind, **values):
                nonlocal last_present
                row = dict(kind=kind, utc=utc(), **values)
                recording.write(json.dumps(row, allow_nan=False)+'\n')
                if kind == 'aircraft_sample':
                    seconds = observer.add(row)
                    present = seconds >= 20
                    if present != last_present:
                        status = dict(kind='platform_presence', utc=utc(), diagnosticDeckRest=present,
                                      continuousSeconds=seconds, note='OL400/H125 contact only; no mission credit')
                        recording.write(json.dumps(status)+'\n')
                        print(json.dumps(status), flush=True)
                    last_present = present
            session = Session(args.live_sdk)
            try:
                emit('observer_start', scriptSha256=hashlib.sha256(pathlib.Path(__file__).read_bytes()).hexdigest(),
                     recorderSha256=hashlib.sha256(pathlib.Path(__file__).with_name('offshore_probe.py').read_bytes()).hexdigest(),
                     sdkSha256=hashlib.sha256(session.dll_path.read_bytes()).hexdigest())
                session.record(emit, args.seconds)
            finally:
                session.dispose()
                observer.add({})
                status = dict(kind='monitor_closed', utc=utc(), diagnosticDeckRest=False,
                              note='Observation ended/disconnected; previous presence is not current authority')
                recording.write(json.dumps(status)+'\n')
                print(json.dumps(status), flush=True)
    else:
        with open(args.recording, encoding='utf-8') as recording:
            for line in recording:
                row = json.loads(line)
                if row.get('kind') == 'aircraft_sample':
                    observer.add(row)
    print(json.dumps(dict(longestCandidateSeconds=observer.longest,
                          candidateSamples=observer.samples, liveLanding='unverified',
                          note='Diagnostic only. Can include placement. No touchdown, flight or mission proof.')))


if __name__ == '__main__':
    main()
