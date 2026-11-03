import ctypes
import pathlib
import sys
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[2]/'scripts'))
from offshore_static_placement import InitialPosition, validate, REFERENCE, PAD, DECK_METERS, outside_release_envelope


class PlacementTests(unittest.TestCase):
    def test_sdk_layout(self):
        self.assertEqual(56, ctypes.sizeof(InitialPosition))
        self.assertEqual(48, InitialPosition.on_ground.offset)
        self.assertEqual(52, InitialPosition.airspeed.offset)

    def test_mutation_surface_is_separate_and_narrow(self):
        import ast
        source = pathlib.Path(__file__).resolve().parents[2]/'scripts'/'offshore_static_placement.py'
        tree = ast.parse(source.read_text(encoding='utf-8'))
        native = {n.attr for n in ast.walk(tree) if isinstance(n, ast.Attribute) and n.attr.startswith('SimConnect_')}
        self.assertEqual({'SimConnect_SetDataOnSimObject', 'SimConnect_MapClientEventToSimEvent',
                          'SimConnect_TransmitClientEvent'}, native)
        event_literals = {n.value for n in ast.walk(tree) if isinstance(n, ast.Constant) and
                          isinstance(n.value, bytes) and n.value.startswith(b'SLEW_')}
        self.assertEqual({b'SLEW_OFF', b'SLEW_ON'}, event_literals)

    def test_only_stationary_h125_slew_near_inspection(self):
        state = {'IS SLEW ACTIVE': 1, 'GROUND VELOCITY': 0,
                 'PLANE LATITUDE': REFERENCE[0], 'PLANE LONGITUDE': REFERENCE[1]}
        validate('H125 Passengers', state)
        for key, value in [('IS SLEW ACTIVE', 0), ('GROUND VELOCITY', 1), ('PLANE LATITUDE', -23)]:
            with self.subTest(key=key), self.assertRaises(ValueError):
                validate('H125 Passengers', dict(state, **{key: value}))
        with self.assertRaises(ValueError):
            validate('Caravan', state)

    def test_release_watchdog_rejects_drift_speed_tilt_and_wrong_elevation(self):
        state = {'PLANE LATITUDE': PAD[0], 'PLANE LONGITUDE': PAD[1],
                 'PLANE ALTITUDE': DECK_METERS/.3048+3.5, 'GROUND VELOCITY': 0,
                 'PLANE BANK DEGREES': 0, 'PLANE PITCH DEGREES': 0}
        self.assertFalse(outside_release_envelope(state))
        for key, value in [('PLANE LATITUDE', PAD[0]+.001), ('GROUND VELOCITY', 6),
                           ('PLANE BANK DEGREES', -11), ('PLANE PITCH DEGREES', 11),
                           ('PLANE ALTITUDE', 2), ('PLANE ALTITUDE', 1000)]:
            with self.subTest(key=key, value=value):
                self.assertTrue(outside_release_envelope(dict(state, **{key: value})))
