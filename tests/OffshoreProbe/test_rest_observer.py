import datetime
import pathlib
import sys
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[2]/'scripts'))
from offshore_rest_observer import RestObserver


def sample(second):
    return dict(utc=(datetime.datetime(2026, 1, 1, tzinfo=datetime.timezone.utc)+datetime.timedelta(seconds=second)).isoformat(),
                title='H125 Passengers', actualObjectId=42,
                core={'SIMULATION TIME': second, 'SIMULATION RATE': 1, 'IS SLEW ACTIVE': 0,
                      'SIM ON GROUND': 1, 'PLANE LATITUDE': -23, 'PLANE LONGITUDE': 14,
                      'PLANE ALTITUDE': 40.75/.3048, 'GROUND VELOCITY': 0, 'VERTICAL SPEED': 0},
                optional={'CONTACT POINT IS ON GROUND:8': 1})


class RestTests(unittest.TestCase):
    def observer(self):
        return RestObserver((-23, 14), 40)

    def test_continuous_static_contact_candidate_is_not_flight_proof(self):
        observer = self.observer()
        for second in range(31):
            observer.add(sample(second))
        self.assertEqual(30, observer.longest)
        self.assertFalse(hasattr(observer, 'delivery_evidence'))

    def test_nearby_hover_and_water_or_lower_structure_are_not_deck_rest(self):
        for key, value in [('SIM ON GROUND', 0), ('PLANE ALTITUDE', 2/.3048),
                           ('PLANE ALTITUDE', 50/.3048), ('GROUND VELOCITY', 1),
                           ('VERTICAL SPEED', .2), ('PLANE LATITUDE', -23.001),
                           ('IS SLEW ACTIVE', 1), ('SIMULATION RATE', 2)]:
            observer = self.observer()
            for second in range(31):
                row = sample(second)
                row['core'][key] = value
                observer.add(row)
            with self.subTest(key=key, value=value):
                self.assertEqual(0, observer.longest)

    def test_missing_contact_is_not_inferred_from_zero_speed(self):
        observer = self.observer()
        for second in range(31):
            row = sample(second)
            row['optional'] = {}
            observer.add(row)
        self.assertEqual(0, observer.longest)

    def test_confirmed_presence_is_revoked_when_contact_or_elevation_is_lost(self):
        for kind in ('airborne','sea-level','slew','disconnect','repeated-time'):
            observer = self.observer()
            for second in range(25):
                observer.add(sample(second))
            self.assertGreaterEqual(observer.current,20)
            row = sample(25)
            if kind == 'airborne': row['core']['SIM ON GROUND']=0
            if kind == 'sea-level': row['core']['PLANE ALTITUDE']=2/.3048
            if kind == 'slew': row['core']['IS SLEW ACTIVE']=1
            if kind == 'disconnect': row={}
            if kind == 'repeated-time': row['core']['SIMULATION TIME']=24
            observer.add(row)
            with self.subTest(kind=kind):
                self.assertEqual(0,observer.current)
                self.assertEqual(24,observer.longest)

    def test_pause_gap_identity_and_missing_data_reset_current_interval(self):
        for change in ('pause', 'gap', 'identity', 'missing', 'nan'):
            observer = self.observer()
            for second in range(10):
                observer.add(sample(second))
            row = sample(10)
            if change == 'pause': row['core']['SIMULATION TIME'] = 9
            if change == 'gap': row = sample(100)
            if change == 'identity': row['actualObjectId'] = 43
            if change == 'missing': del row['core']['PLANE ALTITUDE']
            if change == 'nan': row['core']['PLANE ALTITUDE'] = float('nan')
            with self.subTest(change=change):
                self.assertEqual(0, observer.add(row))
                self.assertEqual(0, observer.add(sample(11)))
                self.assertEqual(9, observer.longest)
