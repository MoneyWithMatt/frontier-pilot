import ast
import pathlib
import sys
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[2] / 'scripts'))
from offshore_vertical_contact_test import guard, clear_above_deck, FREEZES, EVENTS
from offshore_probe import CORE
from offshore_static_placement import PAD, DECK_METERS


class VerticalContactTests(unittest.TestCase):
    def baseline(self, constrained=False):
        core = {name: 0. for name, _ in CORE}
        core.update({'PLANE LATITUDE': PAD[0], 'PLANE LONGITUDE': PAD[1],
                     'PLANE ALTITUDE': (DECK_METERS + .75) / .3048,
                     'GROUND ALTITUDE': DECK_METERS / .3048,
                     'SIM ON GROUND': 1, 'SIMULATION RATE': 1})
        detail = {name: 0. for name, _ in FREEZES}
        detail.update({'COLLECTIVE HANDLE POSITION': .1, 'ROTOR RPM PCT:1': 1.})
        for name, _ in FREEZES[:2]:
            detail[name] = int(constrained)
        contacts = {f'CONTACT POINT IS ON GROUND:{i}': 0 for i in range(20)}
        contacts['CONTACT POINT IS ON GROUND:8'] = 1
        return core, detail, contacts

    def test_ground_and_confirmed_constraints(self):
        guard(*self.baseline(), False, True)
        guard(*self.baseline(True), True, True)

    def test_freeze_unknown_or_wrong_altitude_state_refuses(self):
        for field, value in [(FREEZES[0][0], 0), (FREEZES[1][0], .5), (FREEZES[2][0], 1)]:
            core, detail, contacts = self.baseline(True)
            detail[field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                guard(core, detail, contacts, True)

    def test_motion_geometry_and_clock_guards(self):
        for field, value in [('PLANE LATITUDE', PAD[0] + .001), ('PLANE ALTITUDE', 2),
                             ('GROUND VELOCITY', 1.1), ('VERTICAL SPEED', 3.1),
                             ('PLANE BANK DEGREES', 6), ('SIMULATION RATE', 2), ('IS SLEW ACTIVE', 1)]:
            core, detail, contacts = self.baseline(True)
            core[field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                guard(core, detail, contacts, True)

    def test_contact_missing_or_invalid_and_rotor_state_refuses(self):
        for problem in ('missing', 'nan', 'rotor', 'control'):
            core, detail, contacts = self.baseline()
            if problem == 'missing': del contacts['CONTACT POINT IS ON GROUND:19']
            if problem == 'nan': core['GROUND VELOCITY'] = float('nan')
            if problem == 'rotor': detail['ROTOR RPM PCT:1'] = .5
            if problem == 'control': detail['COLLECTIVE HANDLE POSITION'] = .8
            with self.subTest(problem=problem), self.assertRaises(ValueError):
                guard(core, detail, contacts, False, True)

    def test_water_with_contact_flags_cannot_start(self):
        core, detail, contacts = self.baseline()
        core['PLANE ALTITUDE'] = 1
        core['GROUND ALTITUDE'] = 0
        with self.assertRaises(ValueError): guard(core, detail, contacts, False, True)

    def test_clear_contacts_need_height_and_on_ground_clear(self):
        core, _, contacts = self.baseline(True)
        self.assertFalse(clear_above_deck(core, contacts))
        core['SIM ON GROUND'] = 0
        core['PLANE ALTITUDE'] = (DECK_METERS + 2) / .3048
        contacts['CONTACT POINT IS ON GROUND:8'] = 0
        self.assertTrue(clear_above_deck(core, contacts))
        contacts['CONTACT POINT IS ON GROUND:19'] = 1
        self.assertFalse(clear_above_deck(core, contacts))

    def test_mutator_allowlist_excludes_position_altitude_fuel_payload_and_failures(self):
        self.assertEqual(('FREEZE_LATITUDE_LONGITUDE_TOGGLE', 'FREEZE_ATTITUDE_TOGGLE',
                          'AXIS_COLLECTIVE_SET', 'SLEW_ON'), EVENTS)
        text = (pathlib.Path(__file__).resolve().parents[2] / 'scripts/offshore_vertical_contact_test.py').read_text()
        ast.parse(text)
        self.assertNotIn('SimConnect_SetDataOnSimObject', text)
        self.assertNotIn("send('SLEW_OFF'", text)


if __name__ == '__main__':
    unittest.main()
