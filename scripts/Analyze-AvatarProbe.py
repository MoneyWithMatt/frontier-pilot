"""Summarize read-only observations; does not connect to or change the simulator."""
import collections, hashlib, json, math, pathlib, sys
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1] / '.tools/geodesic-reference'))
from geographiclib.geodesic import Geodesic

session = pathlib.Path(sys.argv[1])
raw = session / 'observations.jsonl'
groups = collections.defaultdict(list)
exceptions = []
last = {}
for line in raw.open(encoding='utf-8-sig'):
    row = json.loads(line)
    if row['kind'] == 'exception': exceptions.append(row)
    if row['kind'] != 'sample': continue
    v = row['value']
    groups[row['phase'], v['target'], v['field']].append(v['value'])
    last[row['phase'], v['target'], v['field']] = v['value']

def snapshot(phase, target):
    return {f: last[phase, target, f] for f in ['PLANE LATITUDE', 'PLANE LONGITUDE', 'PLANE ALTITUDE', 'PLANE HEADING DEGREES TRUE', 'GROUND ALTITUDE', 'PLANE ALT ABOVE GROUND']}

walks = []
for before, after in [('outside-standing-confirmed', 'outside-after-walk-still-confirmed'), ('outside-view-turns-done', 'second-walk-done')]:
    for target in ['aircraft', 'avatar']:
        a, b = snapshot(before, target), snapshot(after, target)
        inv = Geodesic.WGS84.Inverse(a['PLANE LATITUDE'], a['PLANE LONGITUDE'], b['PLANE LATITUDE'], b['PLANE LONGITUDE'])
        bearing = inv['azi1'] % 360 if inv['s12'] > .01 else None
        error = ((b['PLANE HEADING DEGREES TRUE'] - bearing + 180) % 360 - 180) if bearing is not None else None
        walks.append(dict(beforePhase=before, afterPhase=after, target=target, before=a, after=b, distanceMeters=inv['s12'], displacementBearingTrueDegrees=bearing, finalHeadingMinusDisplacementDegrees=error))

summaries = []
for (phase, target, field), values in groups.items():
    if not all(isinstance(v, (int, float)) and math.isfinite(v) for v in values): continue
    entry = dict(phase=phase, target=target, field=field, count=len(values), minimum=min(values), maximum=max(values), first=values[0], last=values[-1])
    if field == 'PLANE HEADING DEGREES TRUE':
        offsets = [(v-values[0]+180)%360-180 for v in values]
        entry['circularOffsetMinimum'] = min(offsets)
        entry['circularOffsetMaximum'] = max(offsets)
    summaries.append(entry)
result = dict(rawSha256=hashlib.sha256(raw.read_bytes()).hexdigest(), exceptionCount=len(exceptions), exceptions=exceptions, walks=walks, summaries=summaries)
(session / 'analysis.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
print(json.dumps(dict(exceptionCount=len(exceptions), walks=walks), indent=2))
