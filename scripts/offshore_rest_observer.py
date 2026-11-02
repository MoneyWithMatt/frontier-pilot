"""Offline diagnostic rest candidates, NEVER touchdown/flight/mission proof.

Thresholds below are observation filters for this one H125/OL400 experiment.
Contact indices remain unmapped; a candidate needs external visual/physics
review. In particular a spawned or repositioned aircraft can satisfy them.
"""
import argparse
import datetime
import json
import math

from offshore_probe import distance_nm


class RestObserver:
    def __init__(self, pad, deck_meters, title='H125 Passengers'):
        self.pad, self.deck_meters, self.title = pad, deck_meters, title
        self.previous = self.start = None
        self.longest = 0
        self.samples = 0

    def add(self, row):
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
            self.longest = max(self.longest, duration)
            self.samples += 1
            return duration
        except (KeyError, ValueError, TypeError, OverflowError):
            self.previous = self.start = None
            return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('recording')
    args = parser.parse_args()
    observer = RestObserver((-22.93892875313759, 14.50461745262146), 39.89600372314453)
    with open(args.recording, encoding='utf-8') as recording:
        for line in recording:
            row = json.loads(line)
            if row.get('kind') == 'aircraft_sample':
                observer.add(row)
    print(json.dumps(dict(longestCandidateSeconds=observer.longest,
                          candidateSamples=observer.samples, liveLanding='unverified',
                          note='Offline diagnostic only. Can include placement. No touchdown, flight or mission proof.')))


if __name__ == '__main__':
    main()
