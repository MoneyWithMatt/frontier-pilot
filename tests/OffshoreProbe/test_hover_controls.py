import pathlib
import sys
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[2]/'scripts'))
from offshore_hover_test import axis_value, ground_guard, Controls, HoverLaw, hover_guard, airborne_hold_eligible
from offshore_static_placement import PAD, DECK_METERS


class HoverControlsTests(unittest.TestCase):
    def test_collective_conversion_and_invalid_values(self):
        self.assertEqual(-16384, axis_value(0))
        self.assertEqual(0, axis_value(.5))
        self.assertEqual(16384, axis_value(1))
        for value in (-.1, 1.1, float('nan'), float('inf')):
            with self.assertRaises(ValueError):
                axis_value(value)

    def test_ground_guard_refuses_unqualified_state(self):
        state = {'PLANE LATITUDE': PAD[0], 'PLANE LONGITUDE': PAD[1],
                 'PLANE ALTITUDE': (DECK_METERS+.75)/.3048, 'SIM ON GROUND': 1,
                 'IS SLEW ACTIVE': 0, 'GROUND VELOCITY': 0,
                 'PLANE BANK DEGREES': 0, 'PLANE PITCH DEGREES': 0}
        ground_guard('H125 Passengers', state)
        for key, value in [('PLANE LATITUDE', PAD[0]+.001), ('SIM ON GROUND', 0),
                           ('IS SLEW ACTIVE', 1), ('GROUND VELOCITY', 2),
                           ('PLANE BANK DEGREES', 6), ('PLANE ALTITUDE', 2)]:
            with self.subTest(key=key), self.assertRaises(ValueError):
                ground_guard('H125 Passengers', dict(state, **{key:value}))
        with self.assertRaises(ValueError):
            ground_guard('Caravan', state)

    def test_event_control_refuses_other_events_and_values(self):
        controls = Controls.__new__(Controls)
        for event, value in [('ENGINE_AUTO_START', 0), ('AXIS_COLLECTIVE_SET', 20000),
                             ('AXIS_COLLECTIVE_SET', .1)]:
            with self.assertRaises(ValueError):
                controls.send(event, value)

    def test_hover_requires_airborne_clear_contacts_and_low_motion(self):
        core = {'SIM ON GROUND':0, 'IS SLEW ACTIVE':0, 'PLANE ALTITUDE':136,
                'GROUND VELOCITY':.1, 'VERTICAL SPEED':0}
        contacts = {f'CONTACT POINT IS ON GROUND:{i}':0 for i in range(20)}
        self.assertTrue(airborne_hold_eligible(core, contacts, 133))
        for key,value in [('SIM ON GROUND',1),('IS SLEW ACTIVE',1),
                          ('PLANE ALTITUDE',133),('GROUND VELOCITY',1),('VERTICAL SPEED',1)]:
            with self.subTest(key=key):
                self.assertFalse(airborne_hold_eligible(dict(core, **{key:value}),contacts,133))
        self.assertFalse(airborne_hold_eligible(core,{},133))
        contacts['CONTACT POINT IS ON GROUND:8']=1
        self.assertFalse(airborne_hold_eligible(core, contacts,133))

    def test_hover_law_bounds_commands_and_refuses_gaps(self):
        state = {'PLANE LATITUDE': PAD[0], 'PLANE LONGITUDE': PAD[1],
                 'PLANE ALTITUDE': 133, 'PLANE BANK DEGREES': 0, 'PLANE PITCH DEGREES': 0,
                 'PLANE HEADING DEGREES TRUE': 180, 'VELOCITY WORLD X': 0,
                 'VELOCITY WORLD Z': 0, 'VERTICAL SPEED': 0}
        law = HoverLaw(133, 180)
        for _ in range(1000):
            collective, x, y, pedal = law.update(state, .05, 136, True)
            self.assertTrue(0 <= collective <= .7)
            self.assertLessEqual(abs(x), .35)
            self.assertLessEqual(abs(y), .35)
            self.assertLessEqual(abs(pedal), .5)
        with self.assertRaises(ValueError):
            law.update(state, 1, 136, False)
        corrective = dict(state, **{'PLANE BANK DEGREES': 4, 'PLANE PITCH DEGREES': 4})
        _, x, y, _ = HoverLaw(133, 180).update(corrective, .05, 136, False)
        self.assertLess(x, 0)
        self.assertLess(y, 0)
        state.update({'GROUND VELOCITY': 0, 'IS SLEW ACTIVE': 0, 'SIMULATION RATE':1})
        hover_guard(state)
        for key, value in [('PLANE BANK DEGREES', 13), ('PLANE ALTITUDE', 1000),
                           ('GROUND VELOCITY', 6), ('IS SLEW ACTIVE', 1)]:
            with self.subTest(key=key), self.assertRaises(ValueError):
                hover_guard(dict(state, **{key:value}))
