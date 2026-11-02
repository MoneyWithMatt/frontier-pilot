import ast
import pathlib
import sys
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[2]/'scripts'))
from offshore_contact_scenarios import PAD, DECK_METERS, WATER, guard_start, outside_envelope


class ContactScenarioTests(unittest.TestCase):
    def pad(self):
        return {'PLANE LATITUDE':PAD[0], 'PLANE LONGITUDE':PAD[1],
                'PLANE ALTITUDE':(DECK_METERS+.75)/.3048, 'IS SLEW ACTIVE':0,
                'SIM ON GROUND':1, 'GROUND VELOCITY':0, 'PLANE BANK DEGREES':0,
                'PLANE PITCH DEGREES':0, 'SIMULATION RATE':1}

    def test_only_stationary_known_h125_pad_can_start_water_test(self):
        state = self.pad()
        guard_start('H125 Passengers',state,'nearby-water')
        for key,value in [('SIM ON GROUND',0),('GROUND VELOCITY',1),
                          ('PLANE ALTITUDE',3),('PLANE LONGITUDE',WATER[1]),
                          ('PLANE LATITUDE',float('nan')),('SIMULATION RATE',2),
                          ('GROUND VELOCITY',-1),('IS SLEW ACTIVE',2)]:
            with self.subTest(key=key), self.assertRaises(ValueError):
                guard_start('H125 Passengers',dict(state,**{key:value}),'nearby-water')
        with self.assertRaises(ValueError):
            guard_start('Caravan',state,'deck-drop')

    def test_water_recovery_requires_slew_and_only_targets_fixed_deck(self):
        state = dict(self.pad(), **{'PLANE LONGITUDE':WATER[1],
                                   'PLANE ALTITUDE':3.5,'IS SLEW ACTIVE':1})
        guard_start('H125 Passengers',state,'deck-drop')
        for scenario in ['nearby-water','arbitrary']:
            with self.assertRaises(ValueError):
                guard_start('H125 Passengers',state,scenario)

    def test_scenario_envelopes_remain_local_to_target_elevation(self):
        state = self.pad()
        self.assertFalse(outside_envelope(state,'deck-drop'))
        self.assertTrue(outside_envelope(state,'nearby-water'))
        sea = dict(state, **{'PLANE LONGITUDE':WATER[1],'PLANE ALTITUDE':3.5})
        self.assertFalse(outside_envelope(sea,'nearby-water'))
        self.assertTrue(outside_envelope(sea,'deck-drop'))
        for key,value in [('PLANE LATITUDE',0),('GROUND VELOCITY',6),
                          ('PLANE BANK DEGREES',11),('IS SLEW ACTIVE',1),
                          ('SIMULATION RATE',2),('PLANE ALTITUDE',1000)]:
            with self.subTest(key=key):
                self.assertTrue(outside_envelope(dict(sea,**{key:value}),'nearby-water'))

    def test_mutation_surface_has_only_position_and_slew(self):
        path = pathlib.Path(__file__).resolve().parents[2]/'scripts/offshore_contact_scenarios.py'
        tree = ast.parse(path.read_text())
        native = {n.attr for n in ast.walk(tree) if isinstance(n,ast.Attribute) and n.attr.startswith('SimConnect_')}
        self.assertEqual({'SimConnect_SetDataOnSimObject','SimConnect_MapClientEventToSimEvent',
                          'SimConnect_TransmitClientEvent'},native)
        events = {n.value for n in ast.walk(tree) if isinstance(n,ast.Constant) and isinstance(n.value,bytes)}
        self.assertEqual({b'TITLE',b'Initial Position',b'SLEW_ON',b'SLEW_OFF'},events)
