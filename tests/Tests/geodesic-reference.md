# Independent geodesic test references

`GeodesyTests.MatchesIndependentWgs84Reference` uses values independently generated with **GeographicLib Python 2.1**, `Geodesic.WGS84.Direct(latitude, longitude, azimuth, metres)`. GeographicLib is a reference-generation tool only; it is not a runtime or unit-test dependency. The C# implementation uses Vincenty's direct algorithm independently.

See `scripts/geodesic-reference.py` for reproducible inputs and `evidence/prototype-0.1/geodesic-reference.json` for the reference output. Longitude 180° is represented as -180° and bearings are normalized to [0,360).

Cases cover the equator, both hemispheres, sample runway ends, high latitudes, both dateline directions, passage over each pole, a 1 cm displacement and a 10 km displacement. Comparison tolerance is 1e-8 degrees (roughly 1.1 mm latitude). Additional tests traverse from each runway endpoint back to its centre and across its full length, including polar-adjacent and dateline-crossing strips. These property checks supplement, rather than replace, the independent reference values.

- [GeographicLib 2.1 API](https://geographiclib.sourceforge.io/html/python/code.html)
- [Vincenty's original direct/inverse paper (NOAA)](https://www.ngs.noaa.gov/PUBS_LIB/inverse.pdf)

Core scope: WGS84 horizontal geometry for local runway distances up to 10 km. Altitudes are explicit fixed data and never inferred from ellipsoid heights or terrain. Exact-pole origins are rejected because a runway heading at the pole is ambiguous; paths may cross a pole.
