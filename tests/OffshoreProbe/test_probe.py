"""Offline packet fixtures only. No native DLL, simulator or personal files."""
import importlib.util
import math
import pathlib
import struct
import unittest

SOURCE = pathlib.Path(__file__).resolve().parents[2]/'scripts/offshore_probe.py'
spec = importlib.util.spec_from_file_location('offshore_probe', SOURCE)
probe = importlib.util.module_from_spec(spec)
spec.loader.exec_module(probe)


def airport(entry=0, total=1, lat=-23, lon=14, trailing=False):
    payload=struct.pack('<9s3sddd', b'FYWB', b'FY', lat, lon, 91)
    if trailing:
        payload+=bytes(36)
    return struct.pack('<7I',28+len(payload),0,18,700,1,entry,total)+payload


def aircraft(values, obj=0, definition=2, count=None):
    payload=struct.pack('<'+'d'*len(values),*values)
    return struct.pack('<10I',40+len(payload),0,8,101,obj,definition,0,0,1,count if count is not None else len(values))+payload


class ProbeTests(unittest.TestCase):
    def test_airport_placeholder_not_a_second_airport(self):
        p=probe.Airports(700)
        self.assertTrue(p.add(airport(trailing=True)))
        self.assertEqual(1,len(p.rows))
        self.assertEqual('FYWB',p.rows[0]['ident'])
        self.assertEqual(91,p.rows[0]['altitudeMeters'])

    def test_world_list_requires_all_packets(self):
        p=probe.Airports(700)
        self.assertFalse(p.add(airport(entry=1,total=2)))
        self.assertTrue(p.add(airport(entry=0,total=2)))

    def test_duplicate_and_changed_packet_count_refused(self):
        for raw in [airport(entry=0,total=2),airport(entry=1,total=3)]:
            p=probe.Airports(700)
            p.add(airport(entry=0,total=2))
            with self.assertRaises(ValueError):
                p.add(raw)

    def test_invalid_facility_coordinates_and_lengths_refused(self):
        for raw in [airport(lat=math.nan),airport(lat=91),airport(lon=181),airport()[:-1],bytes(20)]:
            with self.assertRaises(ValueError):
                probe.Airports(700).add(raw)

    def test_other_requests_are_not_adopted(self):
        self.assertIsNone(probe.decode_data(aircraft([1]),999,2,1))

    def test_data_preserves_negative_altitude_and_vertical_speed(self):
        self.assertEqual((-2,-5),probe.decode_data(aircraft([-2,-5]),101,2,2))

    def test_wrong_proxy_definition_count_and_nonfinite_refused(self):
        for raw in [aircraft([1],obj=17),aircraft([1],definition=3),aircraft([1],count=2),aircraft([math.inf]),aircraft([1])[:-1]]:
            with self.assertRaises(ValueError):
                probe.decode_data(raw,101,2,1)

    def test_title_decode_and_numeric_shape_are_separate(self):
        raw=struct.pack('<10I',296,0,8,101,0,1,0,0,1,1)+b'Fixture H125'+bytes(244)
        self.assertEqual('Fixture H125',probe.decode_data(raw,101,1,1,True))
        with self.assertRaises(ValueError):
            probe.decode_data(raw,101,1,1)

    def test_actual_object_from_explicit_proxy_response_must_remain_consistent(self):
        raw=aircraft([1],obj=540672)
        self.assertEqual((1,),probe.decode_data(raw,101,2,1,expected_object=540672))
        with self.assertRaises(ValueError):
            probe.decode_data(aircraft([1],obj=540673),101,2,1,expected_object=540672)

    def test_first_title_response_can_bind_real_object_instead_of_alias(self):
        raw=struct.pack('<10I',296,0,8,101,540672,1,0,0,1,1)+b'Fixture H125'+bytes(244)
        self.assertEqual('Fixture H125',probe.decode_data(raw,101,1,1,True,expected_object=None))

    def test_numeric_requests_cannot_establish_initial_aircraft_identity(self):
        session=probe.Session.__new__(probe.Session)
        session.aircraft_object=None
        with self.assertRaisesRegex(ValueError,'identity'):
            session.data(2,1)

    def test_distance_is_symmetric_and_zero_at_origin(self):
        a,b=(-23,14),(-23,15)
        self.assertEqual(0,probe.distance_nm(a,a))
        self.assertAlmostEqual(probe.distance_nm(a,b),probe.distance_nm(b,a))
        self.assertGreater(probe.distance_nm(a,b),50)

    def test_no_simulator_mutation_or_career_access_bindings(self):
        import ast
        tree=ast.parse(SOURCE.read_text(encoding='utf-8'))
        names=[n.args[0].value for n in ast.walk(tree) if isinstance(n,ast.Call) and isinstance(n.func,ast.Name) and n.func.id=='bind']
        self.assertEqual({'Open','Close','AddToDataDefinition','RequestDataOnSimObject','GetNextDispatch','RequestFacilitiesList','AddToFacilityDefinition','RequestFacilityData'},set(names))


if __name__=='__main__':
    unittest.main()
