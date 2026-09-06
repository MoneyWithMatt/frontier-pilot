"""Capture a confirmed stationary phase from the recorder; never modifies MSFS."""
import datetime, hashlib, json, math, pathlib, statistics, sys
session, phase, destination = pathlib.Path(sys.argv[1]), sys.argv[2], pathlib.Path(sys.argv[3])
if destination.exists(): raise SystemExit('Refusing to overwrite a survey point')
rows = [json.loads(s) for s in (session/'observations.jsonl').read_text(encoding='utf-8-sig').splitlines()]
samples = [r for r in rows if r['phase'] == phase and r['kind'] == 'sample']
def values(target, field):
    found = [r['value']['value'] for r in samples if r['value']['target'] == target and r['value']['field'] == field]
    if len(found) < 10 or not all(isinstance(v, (int,float)) and math.isfinite(v) for v in found):
        raise SystemExit('Insufficient finite samples: ' + target + '/' + field)
    return found
if not samples: raise SystemExit('No samples in phase')
duration = (datetime.datetime.fromisoformat(samples[-1]['utc']) - datetime.datetime.fromisoformat(samples[0]['utc'])).total_seconds()
if duration < 5: raise SystemExit('Need at least five seconds of stationary observations')
ids = {t: {r['value']['returnedObjectId'] for r in samples if r['value']['target'] == t} for t in ['aircraft','avatar','current']}
if len(ids['avatar']) != 1 or ids['current'] != ids['avatar'] or ids['avatar'] & ids['aircraft']:
    raise SystemExit('Avatar must be the controlled object and distinct from aircraft')
if any(v != 1 for v in values('avatar','SIM ON GROUND')): raise SystemExit('Avatar is not consistently on ground')
lat = values('avatar','PLANE LATITUDE'); lon = values('avatar','PLANE LONGITUDE')
alt = values('avatar','PLANE ALTITUDE'); heading = values('avatar','PLANE HEADING DEGREES TRUE')
if not all(-90 < v < 90 for v in lat) or not all(-180 <= v <= 180 for v in lon): raise SystemExit('Invalid coordinates')
if (max(lat)-min(lat))*111320 > .05 or (max(lon)-min(lon))*111320*math.cos(math.radians(lat[-1])) > .05 or max(alt)-min(alt) > .05:
    raise SystemExit('Position is not stable within 5 cm')
delta = [(h-heading[0]+180)%360-180 for h in heading]
if max(delta)-min(delta) > 1: raise SystemExit('Facing is not stable within one degree')
point = dict(schemaVersion=1, source='SimConnect USER_AVATAR', phase=phase,
    latitude=statistics.median(lat), longitude=statistics.median(lon),
    avatarObjectAltitudeMeters=statistics.median(alt), trueFacingDegrees=(heading[0]+statistics.median(delta))%360,
    altitudeReference='Avatar object altitude; not independently certified terrain elevation',
    suitableForAirportGeneration=False, durationSeconds=duration,
    groundAltitudeObservationMeters=statistics.median(values('avatar','GROUND ALTITUDE')),
    groundAltitudeCaveat='Observed avatar-local behaviour conflicts with aircraft-local documentation; do not assume portable behaviour.',
    evidenceSha256=hashlib.sha256((session/'observations.jsonl').read_bytes()).hexdigest())
with destination.open('x', encoding='utf-8') as f: json.dump(point,f,indent=2); f.write('\n')
print(json.dumps(point,indent=2))
