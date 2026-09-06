"""Reference generation only: pip install geographiclib==2.1; not used by C# tests."""
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / '.tools' / 'geodesic-reference'))
from geographiclib.geodesic import Geodesic
import geographiclib

cases = [
    (0, 0, 0, 1000), (0, 0, 90, 1000),
    (25.035986003223783, -77.46942558230664, 89.64241865029217, 1264.6561306048284),
    (25.035986003223783, -77.46942558230664, 269.64241865029217, 1264.6561306048284),
    (-33.9, 151.2, 217.3, 5000), (80, 15, 90, 5000),
    (0, 179.999, 90, 1000), (0, -179.999, 270, 1000),
    (89.999, 0, 0, 1000), (-89.999, 150, 180, 1000),
    (51.5, -0.1, 123.456, 0.01), (45, 90, 359.9, 10000),
]
results = []
for latitude, longitude, heading, distance in cases:
    result = Geodesic.WGS84.Direct(latitude, longitude, heading, distance)
    results.append(dict(latitude=latitude, longitude=longitude, heading=heading, distance=distance,
                        expectedLatitude=result['lat2'], expectedLongitude=(result['lon2'] + 180) % 360 - 180,
                        expectedBearing=result['azi2'] % 360))
print(json.dumps(dict(reference='GeographicLib', version=geographiclib.__version__, ellipsoid='WGS84', cases=results), indent=2))
