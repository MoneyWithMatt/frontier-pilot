"""Research preparation only. No imports from or writes to application/runtime state."""
import csv, json, math, hashlib
from pathlib import Path
ROOT=Path(__file__).resolve().parents[4]
HERE=ROOT/'docs/research/starter-destinations'
OUT=ROOT/'outputs/starter-destinations-20261029'
OUT.mkdir(parents=True,exist_ok=True)
SRC=HERE/'sources'
A={r['ident']:r for r in csv.DictReader((SRC/('airports.csv' if (SRC/'airports.csv').exists() else 'ourairports-na-plus-fact.csv')).open(encoding='utf-8-sig'))}
RW=list(csv.DictReader((SRC/('runways.csv' if (SRC/'runways.csv').exists() else 'ourairports-runways-na-plus-fact.csv')).open(encoding='utf-8-sig')))
SIM=json.loads((SRC/'simulator-lookup-public.json').read_text(encoding='utf-8-sig'))
S={r['Identifier']:r for r in SIM['Scan']['Airports']}
URL={
'kunene':'https://www.wch.gov.na/web/mhss/kunene-region',
'mhss':'https://mhss.gov.na/documents/146502/1709166/Status%2BReport.pdf/f9e14364-0307-a4ab-980f-1f57351077f2?download=true',
'kavango':'https://nrpa.gov.na/web/mhss/kavango-west',
'youth':'https://www.msyns.gov.na/contact',
'aminuis':'https://www.omahekerc.gov.na/aminuis-settlement1',
'leonard':'https://omahekerc.gov.na/leonardville',
'ehomba':'https://www.namibiankids.org.nz/take-action',
'nyae':'https://nacso.org.na/wp-content/uploads/2025/06/Brochure-Nyae-Nyae-1.pdf',
'nip':'https://www.nip.com.na/wp-content/uploads/2025/09/REQUEST-FOR-INFORMATION-Provision-of-drone-delivery-services-26-Sept-2025.pdf',
'okang':'https://the-eis.com/elibrary/sites/default/files/downloads/literature/3030_EASR_Fuel%20retail%20facility%20in%20Okangwati_Kunene%20Region.pdf',
'outapi':'https://www.wch.gov.na/documents/146502/1029704/MHSS%2BAnnual%2BReport%2B2023-2024.pdf/6ef77bbc-d081-6295-32b8-6a2b759201db?download=true',
'rossing':'https://www.rossing.com/',
'uis':'https://andradamining.com/assets/uis/',
'otjikoto':'https://b2gold.com/operations-projects/producing/otjikoto-mine-namibia/default.aspx',
'okorusu':'https://eia.meft.gov.na/screening/1653_draft_amended_emp_oku_july_2024.pdf',
'tsumeb':'https://en.sinomine.cn/fzjg/181.html',
'tsumeb-status':'https://thebrief.com.na/2026/08/sinomine-tsumeb-smelter-invests-over-4-billion-namibia-dollars-in-mmr-plant-and-community-projects-despite-challenges/',
'karibib':'https://eia.meft.gov.na/screening/5728_esa_report_ml_271_reduced.pdf',
'paladin':'https://www.paladinenergy.com/langer-heinrich-mine/',
'walvis':'https://www.namport.com.na/',
'luderitz':'https://www.namport.com.na/port-of-luderitz/welcome-to-the-port-of-luderitz/330/',
'rosh':'https://www.roshpinahzinc.com/',
'skorpion':'https://vedanta-zincinternational.com/what-we-do/our-operations/skorpion-zinc/',
'oranjemund':'https://oranjemund.co/about',
'marine':'https://eia.meft.gov.na/screening/4310_final_updated_dbmn_2024_eia_and_emp_report_for_atlantic_1_ml_47_june_2024.pdf',
'namcor':'https://www.namcor.com.na/contact-2/',
'ongava':'https://www.ongava.com/stay/getting-to-ongava',
'mokuti':'https://onguma.com/wp-content/uploads/2025/03/Trails-Camp-Fact-Sheet-2026-2.pdf',
'wolwedans':'https://www.wolwedans.com/location-and-access/',
'aandster':'https://the-eis.com/elibrary/sites/default/files/downloads/literature/269_EMP_Airstrip%20on%20Farm%20Aandster_Hardap%20Region.pdf',
'aandster-old':'https://www.wolwedans.com/wp-content/uploads/AandsterDirections_ToWolwedans.pdf',
'sonop':'https://www.zannierhotels.com/wp-content/uploads/2019/09/Zannier-Hotels-Sonop-Press-Kit-EN-FINAL.pdf',
'sonop-access':'https://www.swissafrican.com/en/namibia/camps/zannier-sonop/',
'palmwag':'https://www.afrozapping.com/wp-content/uploads/2022/06/palmwag-lodge.pdf',
'doro':'https://eia.meft.gov.na/screening/5558_dn_emp_2026.pdf',
'hartmann':'https://eia.meft.gov.na/screening/5561_scf_emp_2026.pdf',
'hoanib':'https://www.wildernessdestinations.com/africa/namibia/kunene-region/hoanib-skeleton-coast-camp',
'circuit':'https://images.wilderness.co.za/2023/Product/flying-circuit-namibia.pdf',
'geluk':'https://aopa.org.na/aerodrome/',
'okaukuejo':'https://www.nwr.com.na/resorts/okaukuejo-resort/',
'mount':'https://online.flipbuilder.com/aitt/bets/files/basic-html/page180.html',
'okonjima':'https://okonjima.com/wp-content/uploads/2022/05/Directions-Okonjima.pdf',
'okonjima-rwy':'https://okonjima.com/wp-content/uploads/2019/10/okonjima-MAIN-brochure-A5-may_19_NEW_final_for_web.pdf',
'halali':'https://www.nwr.com.na/resorts/halali-resort/',
'nwr':'https://www.nwr.com.na/icheephu/2024/07/NWR-Fact-Sheets-2024.pdf',
'damaraland':'https://www.wildernessdestinations.com/africa/namibia/damaraland/damaraland-camp',
'etendeka':'https://www.namibweb.com/etendeka.htm',
'helmer':'https://helmeringhausennamibia.com/airfield',
'new-circuit':'https://www.tourismupdate.com/article/natural-selection-namibia-to-launch-scheduled-flying-circuit',
'gem':'https://miningexpo.bi-dynamics.com/wp-content/uploads/2023/06/5.-Benguela-Gem-The-Game-Changer-of-Marine-Diamond-Recovery.pdf',
'kudu':'https://www.bwenergy.no/en/operations/namibia/kudu/',
'venus':'https://www.slrconsulting.com/public-documents/esia-venus-offshore-dev/'
}
URL.update({'twyfel':'https://the-eis.com/elibrary/sites/default/files/downloads/literature/1579_EMP_Twyfelfontein%20Country%20Lodge.pdf','twyfel-rwy':'https://www.afrozapping.com/wp-content/uploads/2025/04/twyfelfontein-country-lodge.pdf','mokuti-rwy':'https://stwetuproduction.blob.core.windows.net/azure-blob-resources-wetu-production/Resources/10525%2FMokuti-FactSheet-LowRes.pdf','omusati':'https://wch.gov.na/web/mhss/omusati-region','ohangwena':'https://nrpa.gov.na/web/mhss/ohangwena-region','nepara':'https://gateway.x-plane.com/airports/FYNP/show'})
URL.update({'solitaire':'https://www.solitairenamibia.com/','rosh-closed':'https://gateway.x-plane.com/airports/FYRP/show','rosh-field':'https://ourairports.com/airports/NA-0180/','caravan':'https://cessna.txtav.com/-/media/cessna/files/brochures/turboprop/caravan_brochure.ashx','vision':'https://cirrusaircraft.com/wp-content/uploads/2024/02/VisionJet_G3_ProductBrochure_2026_LOWRES.pdf','h125':'https://www.airbus.com/en/products-services/helicopters/civil-helicopters/h125/h125-technical-information','osm-aminuis':'https://www.openstreetmap.org/way/1080747309'})
URL.update({'dordabis':'https://eia.meft.gov.na/screening/7159_scoping_report_with_appendices_combined.pdf','omaheke':'https://wch.gov.na/web/mhss/omaheke-region'})
def nm(a,b):
    p,q=map(math.radians,[a[0],b[0]]); dp=q-p; dl=math.radians(b[1]-a[1])
    return 3440.065*2*math.asin(min(1,math.sqrt(math.sin(dp/2)**2+math.cos(p)*math.cos(q)*math.sin(dl/2)**2)))
BASE={'Eros':(-22.604920,17.079002),'Walvis Bay':(-22.97972,14.64532)}
AIR=[('c152','Cessna 152',90,8,'Missionary'),('c208','Cessna 208 Caravan',155,10,'Missionary / Industrial'),('baron-g58','Beechcraft Baron G58',175,10,'Industrial'),('bonanza','Beechcraft Bonanza',155,10,'Safari'),('pa28','PA-28',110,8,'Safari'),('savage-cub','Savage Cub',80,6,'Safari'),('vision-jet','Cirrus Vision Jet',220,15,'Offshore'),('h125','H125 — disabled',110,10,'Offshore future only')]
rows=[]; scores=[]
def add(code,facility,keys,terrain,notes='',lookup=None,coord=None,name=None,landing=None):
    a=A.get(code,{})
    lat,lon=coord if coord else (float(a['latitude_deg']),float(a['longitude_deg'])) if a else (None,None)
    rr=[r for r in RW if r['airport_ident']==code and r['closed']=='0']
    r=max(rr,key=lambda r:float(r['length_ft'] or 0),default={})
    sim=S.get(lookup or code); delta=nm((lat,lon),(sim['Latitude'],sim['Longitude'])) if sim and lat is not None else None
    state='Confirmed real-world destination; MSFS status unverified' if a or keys else 'Speculative candidate'
    if sim and delta is not None: state='Partially verified' if delta>5 else 'Confirmed in MSFS 2024'
    simnote='Not queried by this cache; absence cannot be inferred.'
    if sim and delta is not None: simnote=f"Identifier returned in 2026-10-27 cache; offset {delta:.1f} NM from real-world point. " + ('WRONG-LOCATION ASSOCIATION; hold.' if delta>5 else 'Database presence only; scenery/runway uninspected.')
    elif sim:
        simnote='Identifier returned but real-world coordinates missing; association unverified.'
        print('MISSING REAL COORDINATE',code)
    elif (lookup or code) in ['FYSF','FYBM','FYRP','FYTF','FYDN','FYMW']: simnote='Not returned by 2026-10-16 published scan or 2026-10-27 cached scan; no new query. Search by geography/alternate identifier before custom scenery.'
    rwtext=landing or (f"{float(r['length_ft'])*0.3048:.0f} x {float(r['width_ft'])*0.3048:.0f} m; {r['surface']}; {r['le_ident']}/{r['he_ident']}" if r and r.get('width_ft') else 'Runway dimensions, width, surface and orientation not established in this pass')
    item=dict(id=code,name=name or a.get('name',facility),identifier=code if not code.startswith(('candidate','custom','offshore')) else '',identifier_kind='OurAirports database key, not certified ICAO' if code.startswith('NA-') else 'Published identifier; reconcile with simulator' if a else 'Research label only; no invented ICAO',latitude=lat,longitude=lon,coordinate_basis='OurAirports snapshot 2026-10-29' if a and not coord else 'Explicit source; see notes' if coord else 'Unknown; omitted from map',region=a.get('iso_region','Namibia offshore / unresolved'),elevation_ft=float(a['elevation_ft']) if a.get('elevation_ft') else None,runway=rwtext,facility=facility,terrain=terrain,notes=notes,verification=state,sim_note=simnote,sim_lat=sim['Latitude'] if sim else None,sim_lon=sim['Longitude'] if sim else None,offset_nm=round(delta,2) if delta is not None else None,source_links=[URL[k] for k in keys],airport_source=f'https://ourairports.com/airports/{code}/' if a else '',distance_eros=round(nm(BASE['Eros'],(lat,lon)),1) if lat is not None else None,distance_walvis=round(nm(BASE['Walvis Bay'],(lat,lon)),1) if lat is not None else None)
    if lat is not None: item['map_link']=f'https://www.openstreetmap.org/?mlat={lat:.6f}&mlon={lon:.6f}#map=13/{lat:.6f}/{lon:.6f}'
    else: item['map_link']=''
    rows.append(item)
    item.update(length_m=round(float(r['length_ft'])*.3048,1) if r.get('length_ft') else None,width_m=round(float(r['width_ft'])*.3048,1) if r.get('width_ft') else None,surface=r.get('surface','Unknown'),orientation=(r.get('le_ident','')+'/'+r.get('he_ident','')).strip('/') or 'Unknown',true_heading=float(r['le_heading_degT']) if r.get('le_heading_degT') else None)
    return item
def rate(item,career,purpose,fleet,parts,experience,decision='Priority validation',gate=True):
    # Stage A is a purpose gate. Aircraft/verification remain separate hard holds.
    w=[.4,.15,.2,.25] if career in ['Missionary','Safari'] else [.4,.2,.2,.2]
    score=round(sum(x*y for x,y in zip(parts,w))*20,1) if gate else None
    if item['offset_nm'] is not None and item['offset_nm']>5: decision='Hold: identifier/location conflict'
    scores.append(dict(id=item['id'],name=item['name'],career=career,gate='Eligible purpose' if gate else 'Ineligible purpose',purpose=item['facility']+' — '+purpose,fleet=fleet,parts=parts if gate else [None]*4,experience=experience,score=score,flying=sum(experience),decision=decision,reason=item['notes']))

# Missionary: named communities and services, not inferred from surface or isolation.
specs=[
('FYOP','Opuwo hospital, laboratory and outreach network',['kunene','nip'],'Kunene hills and dry valleys','Referral/outreach hub; direct Eros sector exceeds two hours in C152.',5,1,2,5,[4,4,2,4,5]),
('FYSF','Sesfontein health centre and surrounding communities',['kunene','nip'],'Hoanib valley gateway and escarpment','High-value purpose; absent identifier in recorded scan; no scenery approval.',5,1,3,5,[5,4,3,5,5]),
('FYEF','Epupa Clinic and Kunene communities',['kunene'],'Kunene River, falls and rugged hills','Clinic association credible; ground transfer required; long from Eros.',5,1,1,4,[5,5,3,5,5]),
('NA-0160','Okangwati health centre and combined school',['okang','kunene'],'Zebra Mountains and Kunene hinterland','App FYKW resolves roughly 590 NM east of this settlement. EIA describes military strip. NIP clinic latitude conflicts with settlement; do not use it as airport coordinate.',5,1,1,4,[5,4,3,5,5]),
('NA-0169','Ehomba Combined School and feeder schools',['ehomba'],'Kunene foothills and river hinterland','Strong education purpose; identifier is a database key; no runway evidence or MSFS lookup.',5,1,1,4,[4,4,3,5,5]),
('FYRC','Ruacana community service network',['omusati'],'Kunene hydro landscape and border escarpment','Community association needs a named service recipient and verified ground transfer; reserve research candidate rather than generic medical demand.',0,4,1,4,[4,3,1,3,4]),
('FYTK','Tsumkwe / Nyae Nyae community and conservancy',['nyae'],'Kalahari sandveld, pans and woodland','Teacher, community liaison and supplies credible; medical evacuation equipment not established.',5,1,2,5,[4,3,3,5,5]),
('FYOE','Omega clinic and settlement',['kavango'],'Zambezi woodland and remote interior','Credible clinic endpoint but well beyond two-hour direct starter envelope; retain as extended research.',5,1,1,4,[4,3,3,5,5]),
('FYNP','Nepara clinic / Nkurenkuru health network',['kavango','nepara'],'Kavango woodland and river region','Long Eros leg. Field absent in current OurAirports snapshot; X-Plane has a historical FYNP datum, not real-world or MSFS acceptance. Closure lead needs investigation; no contemporary runway proof.',5,1,1,4,[4,3,3,4,4]),
('FYRU','Rundu referral hospital and regional services',['outapi','kavango'],'Kavango River and woodland','Referral hub rather than small village; extended C208 sector.',4,4,1,5,[4,3,1,2,4]),
('FYEN','Eenhana district hospital and community services',['ohangwena','youth'],'Northern floodplain and settlement mosaic','Named hospital and community personnel/supplies; no claim of poor-road access from runway alone.',4,4,1,4,[3,2,1,2,3]),
('NA-0171','Outapi hospital and community services',['outapi','youth'],'Seasonal northern floodplain','Hospital documented; field dimensions and simulator identity unknown; long Eros leg.',4,1,1,4,[3,2,3,3,3]),
('FYOH','Okahao district hospital and youth skills training',['omusati','youth'],'Northern seasonal pans and villages','Specific health/education purpose; runway condition/measurements unknown.',4,1,1,4,[3,2,3,3,3]),
('FYKJ','Kamanjab health centre and schools',['kunene'],'Kunene transition, hills and savanna','Useful intermediate sector; runway data required before either type approval.',5,1,4,5,[4,4,3,3,4]),
('FYOJ','Outjo hospital and clinic',['kunene'],'Bushveld and low hills','Regional outreach/referral hub; paved-road accessibility reduces remoteness.',4,1,4,5,[3,3,2,2,4]),
('FYOS','Oshakati intermediate hospital',['outapi'],'Northern floodplain and urban service hub','Referral destination, not isolated village; direct Eros leg exceeds target on slower type.',4,4,1,5,[3,2,1,1,4]),
('FYAM','Aminuis clinic, schools and rural settlement',['aminuis'],'Omaheke Kalahari rural plateau','High-value closer candidate: named services and partly gravel road connection. OSM clinic is about 1.3 NM from field; ground transfer inferred.',5,1,5,5,[3,3,3,4,5]),
('NA-0114','Leonardville clinic, primary and secondary schools',['leonard'],'Omaheke rural plateau','Strong shorter-route candidate with actual community services; no confirmed simulator identifier.',5,1,5,5,[3,3,3,3,5]),
('NA-0014','Dordabis clinic and community service area',['mhss','dordabis'],'Khomas rural hills and farmland','Closer Eros opportunity: named clinic and community training programme. Private Carpet field access and usable runway unknown; facility exists but no real aviation contract is asserted.',5,1,5,4,[4,4,3,3,5]),
('FYGB','Gobabis district hospital / Omaheke referral network',['omaheke'],'Omaheke plateau and rural service hub','Hospital/clinic staff and urgent supplies credible; established road access means this is a referral hub, not a road-isolated bush community.',4,1,5,5,[3,3,2,2,4]),
]
for code,fac,ks,ter,note,p,r,t,v,exp in specs:
    d=add(code,fac,ks,ter,note,lookup='FYKW' if code=='NA-0160' else None,coord=(-17.768,18.452) if code=='FYNP' else None)
    if code=='FYNP': d['verification']='Partially verified'; d['coordinate_basis']='Historical X-Plane datum; NOT independent real-world airfield verification'
    rate(d,'Missionary','Clinic/school supplies and community personnel; proposed game work, not a claim of an existing aviation contract.' if p else 'No specific community service recipient established; purpose gate not passed.','C152 / C208 conditional', [p,r,t,v],exp,'Research hold' if code in ['FYNP','FYRC'] else 'Extended network' if t==1 else 'Priority validation',gate=p>0)

# Existing Industrial pool retained and reviewed, including weak associations.
industrial=[
('FYAR','Arandis / Rössing uranium mine',['rossing'],'Namib gravel plains, escarpment and mine country','Retain existing accepted airport and prototype economy role; road transfer to mine. No radioactive cargo proposed.',5,4,5,5,[4,3,1,3,5]),
('FYUS','Uis tin mine / Andrada',['uis'],'Brandberg massif and arid mining town','Retain existing accepted field and resource-economy role; no runway measurements established.',5,1,5,5,[5,4,3,4,5]),
('FYBM','Otjikoto gold mine / Wolfshag underground operation',['otjikoto'],'Northern savanna and mining infrastructure','Open-pit phase ended 2025; underground/processing support remains plausible. Identifier absent in recorded MSFS response.',5,1,5,5,[3,3,3,3,5]),
('FYKR','Okorusu fluorspar mine',['okorusu'],'Otjiwarongo-region hills and mineral landscape','Mine/airstrip association credible; current production and civil air access unresolved. Do not model active ore output.',4,1,5,3,[4,3,3,4,4]),
('FYTM','Tsumeb smelter / recycling project',['tsumeb','tsumeb-status'],'Otavi hills and mining town','2026 reporting says copper processing is in care and maintenance. Only maintenance/project specialists, not invented recurring active copper production.',4,3,5,3,[3,3,2,2,4]),
('FYKA','Karibib / Navachab gold mining district',['karibib'],'Erongo hills, marble and gold-mining district','FYKA associated with air force base; civil permission and actual mine transfer need verification.',5,1,5,4,[4,4,3,3,4]),
('FYSM','Swakopmund uranium-service hub / Langer Heinrich road transfer',['paladin'],'Atlantic coast, dunes and desert transition','Industrial spares/specialists plausible through city; mine is about 80 km east, not adjacent to runway.',4,1,5,5,[5,3,2,2,5]),
('FYWB','Walvis Bay port, repair and logistics industries',['walvis'],'Atlantic port, lagoon and dunes','Retain industrial destination; Offshore base/return only. Not a dedicated mine strip.',5,5,5,5,[5,3,1,2,5]),
('FYRP','Rosh Pinah Zinc and southern mining town',['rosh','skorpion','rosh-field','rosh-closed'],'Orange River hinterland and rugged desert','Legacy FYRP now uses NA-0180 in OurAirports; X-Plane marks old code deprecated/typically closed. Current landing status unresolved. Rosh Pinah Zinc produces; nearby Skorpion is care and maintenance. Do not conflate them.',5,0,3,4,[5,4,3,4,5]),
('FYOG','Oranjemund diamond industry and marine logistics',['oranjemund','marine'],'Orange River mouth, dunes and Atlantic','Airborne crew chain documented. Main runway only; two closed gravel runways excluded.',5,3,3,5,[5,4,1,4,5]),
('FYLZ','Lüderitz fishing, mining exports and offshore support port',['luderitz'],'Rugged Atlantic coast, bays and desert','High-value dual-career hub. Airport to port needs road transfer; no airport deck assumed.',5,4,4,5,[5,4,2,4,5]),
('FYGF','Grootfontein regional logistics',['karibib'],'Northern savanna and logistics town','Existing pool member, but no specific industrial customer established in this pass; military access and purpose need research.',0,0,0,0,[3,2,1,2,3]),
]
for code,fac,ks,ter,note,p,r,t,v,exp in industrial:
    d=add(code,fac,ks,ter,note,coord=(-27.9643,16.753901) if code=='FYRP' else None)
    if code=='FYRP': d['verification']='Partially verified'; d['coordinate_basis']='OurAirports NA-0180 historical field point; legacy FYRP identity/closure unresolved';d['airport_source']=URL['rosh-field']
    rate(d,'Industrial','Urgent parts, tools, samples and specialists; aircraft-scale consignments only.','C208 / Baron conditional',[p,r,t,v],exp,'Research hold' if code in ['FYKR','FYTM','FYKA','FYGF','FYRP'] else 'Priority validation',gate=p>0)

safari=[
('FYNG','Ongava Private Game Reserve and lodges',['ongava'],'Etosha fringe and mopane woodland','Operator confirms 1,200 m strip; cached FYNG instead lies near Betesda in southern Namibia. Hold simulator association.',5,3,5,5,[4,3,2,3,4],'1200 m length (operator); width/surface/orientation unverified'),
('FYMO','Mokuti / Onguma tourism transfer',['mokuti','mokuti-rwy'],'Eastern Etosha woodland and wildlife country','Historical Mokuti factsheet: 2200 x 30 m, approx first 1500 m concrete from 08, remaining gravel, 08/26, daylight only; recency and usable declared distances need verification.',5,3,4,5,[4,3,2,3,4],'2200 x 30 m; historical mixed concrete/gravel; 08/26; confirm present usable length'),
('FYTF','Twyfelfontein Country Lodge and rock-art access',['twyfel','twyfel-rwy'],'Damaraland red-rock hills','Official EMP links lodge guest/flood access to strip 3 km north. Conflict: OurAirports approx 1996 m asphalt vs operator 2200 m gravel. Absent in recorded simulator list.',5,2,5,5,[5,4,2,4,5],'1996 m asphalt (OurAirports) vs 2200 m gravel (operator); width 25 m in database; orientation 10/28 in database'),
('NA-0030','Keerweder / Wolwedans transfer',['wolwedans'],'NamibRand dunes and mountain margins','Real operator association; 45-minute ground transfer. Database key is not an MSFS identifier.',5,1,5,4,[5,4,3,5,5],None),
('FYGK','Witwater East / Geluk-associated flying-circuit lead',['geluk','circuit'],'Sossusvlei desert plains','IDENTITY CONFLICT: AOPA calls FYGK Geluk; OurAirports calls it Farm Whitwater East. Separate NA-0067 Geluk exists about 8.5 NM away. Do not collapse the two records or assign lodge until reconciled.',3,1,5,3,[5,4,3,4,5],None),
('candidate-sonop','Zannier Sonop private strip',['sonop','sonop-access'],'Granite boulders, Tiras Mountains and Namib dunes','Lodge and private airstrip verified; exact runway coordinates, identifier and measurements unresolved. Lodge coordinates are deliberately not substituted.',5,1,3,4,[5,4,3,5,5],None),
('FYWD','Wolwedans lodges / NamibRand',['wolwedans'],'NamibRand dune sea and mountain backdrop','New database lead FYWD for previously identifierless row; must query exact geographic site, not silently approve.',5,1,5,5,[5,4,3,5,5],None),
('FYAD','Aandster / Wolwedans Boulders Camp',['wolwedans','aandster','aandster-old'],'Southern NamibRand open sand plains','Major conflict: OurAirports 900 m vs 2022 EMP 1450 m vs old operator 1500 m; orientations conflict too. Cached FYAD is far east. No runway approval until authoritative reconciliation.',5,1,5,4,[5,4,4,5,5],None),
('NA-0168','Palmwag Lodge and concession',['palmwag'],'Palm-lined oasis, basalt and desert mountains','Gondwana factsheet gives strip position; reconcile precise operator coordinates and database reference before activation.',5,1,4,5,[5,4,3,5,5],None),
('FYDN','Wilderness Doro Nawas charter hub',['doro'],'Aba-Huab valley and Damaraland hills','EMP prose 1550 m; embedded chart 1490 m with LDA 1320/1330 m; preserve conflict. Airport absent in recorded simulator list.',5,3,5,5,[5,4,3,4,5],'1490 / 1550 m conflict; LDA 1320/1330 m; gravel; 07/25; width unknown'),
('FYHV','Serra Cafema via Hartmann Valley',['hartmann','circuit'],'Kunene valley, dunes and remote mountains','1600 m EMP vs 1550 m OurAirports. Cached FYHV is in southeast Namibia; requires geographic reconciliation. Extended for current slow safari fleet.',5,2,2,5,[5,5,3,5,5],None),
('FYHO','Wilderness Hoanib Skeleton Coast Camp',['hoanib'],'Remote Hoanib river valley and desert-adapted wildlife','Fly-in facility verified by operator. Cached FYHO near Bitterwasser, not Hoanib; do not use that endpoint.',5,1,3,5,[5,5,3,5,5],None),
('FYMW','Möwe Bay Skeleton Coast excursion',['hoanib'],'Fog coast, seals, dunes and shipwreck landscape','Operator documents fly/drive coastal excursion. Not interchangeable with Shipwreck Lodge/Westies. No dimensions established; absent in recorded list.',4,1,3,4,[5,4,3,5,5],None),
('NA-0067','Geluk / Wolwedans and Kwessi Dunes transfer lead',['wolwedans','new-circuit','geluk'],'Dunes and Sossusvlei desert','OurAirports separate field from FYGK; facility transfer association applies to Geluk name but correct runway remains unresolved. Hold both until geography/identifier reconciled.',5,1,5,4,[5,4,3,4,5],None),
('FYOO','NWR Okaukuejo resort and park operations',['okaukuejo'],'Etosha pan margin and savanna','Real tourism/park service purpose. Confirm park air-access permission and exact runway; not a generic sightseeing landing.',5,1,5,5,[4,3,3,3,4],None),
('FYME','Mount Etjo Safari Lodge',['mount'],'Mount Etjo massif and savanna','Facility and field location corroborated; public runway details missing. Particularly useful short Eros sector.',5,1,5,5,[5,4,3,3,5],None),
('FYKN','Okonjima Nature Reserve / AfriCat',['okonjima','okonjima-rwy'],'Valley between mountain ranges','Operator 1300 m dirt/sand, 05/23; elevation 5430 ft vs OurAirports 5357 ft. Mountains/power line/fence require review. Identifier newly researched, not queried.',5,3,5,5,[5,5,3,3,5],'1300 m; compact dirt/sand; 05/23; width unknown'),
('FYHI','NWR Halali resort and park operations',['halali'],'Etosha woodland, dolomite hills and waterholes','Tourism purpose verified; air access/permission not established by resort existence.',4,1,4,4,[4,3,3,3,4],None),
('FYNA','NWR Namutoni resort and eastern Etosha',['nwr'],'Eastern Etosha pan and woodland','Resort purpose documented; runway details and current visitor air access unverified.',4,1,4,4,[4,3,3,3,4],None),
('FYDC','Wilderness Damaraland Camp / Torra Conservancy',['damaraland'],'Huab Valley, plains and rugged mountains','Strong lodge/conservancy purpose; strip record alone does not establish current landing permission.',5,1,5,5,[5,4,3,5,5],None),
('FYEA','Etendeka Mountain Camp',['etendeka'],'Basalt mountains and rugged Damaraland','Secondary operator-hosted description: 1000 m gravel, 3000 ft; current usable length/width/orientation need operator confirmation.',5,2,4,4,[5,5,3,5,5],'1000 m gravel (secondary description); width/orientation unverified'),
('FYHH','Helmeringhausen Hotel private field',['helmer'],'Southern desert farmland and distant Tiras hills','Operator 900 m gravel; unregistered/unlicensed/unmanned and prior approval for guests. Hospitality/diversity reserve, not core wildlife lodge.',4,2,4,3,[4,3,3,4,5],'900 m gravel; width/orientation unverified'),
('FYSO','Solitaire Roadhouse / Mountain Lodge / Desert Camp',['solitaire'],'Escarpment, gravel plains and desert-tourism crossroads','Operator confirms fly-in tourism, lodge shuttle and airfield. Useful closer desert-network stop; aviation fuel only by prior arrangement, not guaranteed.',5,3,5,5,[5,4,3,3,5],None),
('candidate-westies','Shipwreck Lodge via Westies airstrip',['new-circuit'],'Atlantic dunes and remote Skeleton Coast','2025 announcement of 2026 circuit is not evidence opening occurred. Confirm operator, position and simulator availability; do not relabel Möwe Bay.',4,1,2,4,[5,4,3,5,5],None),
]
for code,fac,ks,ter,note,p,r,t,v,exp,landing in safari:
    d=add(code,fac,ks,ter,note,landing=landing)
    rate(d,'Safari','Lodge/park staff, supplies and conservation equipment now; guest transport only through separately approved passenger capability.','Bonanza / PA-28 / Savage Cub conditional',[p,r,t,v],exp,'Research hold' if code in ['FYGK','NA-0067','candidate-westies','candidate-sonop'] else 'Priority validation')

# Offshore airport transfers: retain factual mission diversity without claiming every industry is petroleum.
for code,fac,ks,ter,note in [
('FYWE','Windhoek corporate offices / NAMCOR',['namcor'],'Khomas highlands and urban basin','Existing Offshore destination; airport supports corporate/specialist liaison by inference from real city office, not a claimed charter contract.'),
('FYWH','Windhoek international business gateway',['namcor'],'High plateau east of Windhoek','Executive/international connections and petroleum specialists plausible; separate from Eros local-office role.'),
('FYKT','Keetmanshoop regional airport',['luderitz'],'Southern plateau and desert','Existing Offshore member; no named petroleum/executive client or regular logistics chain established. Hold rather than invent demand.'),
('FACT','Cape Town marine logistics / technical support',['marine'],'Cape mountains, peninsula and ocean','Documented Cape Town–Oranjemund crew chain; extension from Walvis Bay is a proposed executive/specialist mission. Cross-border clearance, reserves and current aircraft implementation required.')]:
    add(code,fac,ks,ter,note)
by={r['id']:r for r in rows}
for key in ['FYGK','NA-0067','candidate-westies']:
    by[key]['verification']='Partially verified'
for code,p,r,t,v,exp,gate,decision in [
('FYLZ',5,4,5,5,[5,4,2,4,5],True,'Priority validation'),
('FYOG',5,3,4,5,[5,4,1,4,5],True,'Priority validation'),
('FYWE',5,4,5,5,[4,4,1,2,4],True,'Priority validation'),
('FYWH',5,5,5,5,[3,3,1,2,4],True,'Priority validation'),
('FYSM',3,1,3,3,[5,3,2,2,4],True,'Research hold'),
('FACT',5,5,3,4,[5,4,1,2,5],True,'Extended network'),
('FYAR',0,0,0,0,[4,3,1,3,4],False,'Reject Offshore association'),
('FYKT',0,0,0,0,[3,3,1,3,4],False,'Research hold')]:
    purpose='Marine-industry specialist/equipment or executive airport transfer; proposed adaptation of existing airport mission types.'
    if not gate: purpose='No evidenced Offshore-specific service requirement in this pass. Industrial membership at Arandis remains valid.'
    rate(by[code],'Offshore',purpose,'Vision Jet conditional; H125 disabled',[p,r,t,v],exp,decision,gate)

# Shared purpose must be independently evidenced, not inherited from geography.
rate(by['FYTK'],'Safari','Conservancy liaison/equipment and community-led tourism support distinct from community supplies.','Bonanza / PA-28 / Cub conditional',[4,1,3,4],[4,3,3,5,5],'Extended network')

for code,fac,ks,notes in [
('offshore-benguela-gem','Benguela Gem marine diamond vessel',['gem','marine'],'Real helicopter crew-change purpose; mobile vessel, no persistent landing coordinate or simulator deck evidence. H125 disabled and offshore single-engine suitability unqualified. Not a stationary platform.'),
('offshore-kudu','Kudu gas development area',['kudu'],'Operator lists production TBD and installation NA. A discovery/licence is not a landable platform; reject current landing destination. No field-centre coordinate substituted.'),
('offshore-venus','Venus proposed development',['venus'],'Proposed subsea/FPSO development, not a confirmed installed simulator destination. Distant offshore reach not approved for H125; no invented pad or coordinates.')]:
    d=add(code,fac,ks,'Offshore Atlantic',notes)
    d['verification']='Confirmed real-world destination; MSFS status unverified' if code=='offshore-benguela-gem' else 'Unsuitable or rejected'
    rate(d,'Offshore','Crew/specialist transfer is real only for the vessel; development licence areas provide no landing facility.', 'H125 unavailable; no landing approval',[5,0,0,4],[4,4,5,5,5] if code.endswith('gem') else [0,0,0,0,0],'Deferred: mobile deck qualification' if code.endswith('gem') else 'Reject current landing destination',gate=code.endswith('gem'))

custom=[]
for key,name,lat,lon,reason in [
('custom-etanga','Etanga Clinic service area',-17.8675792,13.025594,'Strong medical network need. First search for nearby existing strip; no runway absence established. Candidate landing survey needs terrain, drainage, land-use and ground-transfer review.'),
('custom-otjondeka','Otjondeka Clinic service area',-18.630306,14.234317,'Named remote clinic and sample route. Nearer outreach network than northern border sites, but still long from Eros; do not relocate the operating base without design approval.'),
('custom-oruvandjei','Oruvandjei Clinic service area',-18.9190037,14.1067336,'Named outreach clinic, potential community supply destination. Published point is clinic location only; slope/obstacles/land rights and alternative strips unresolved.')]:
    d=add(key,name,['nip','kunene'],'Kunene community hinterland',reason,coord=(lat,lon),name=name)
    d['verification']='Speculative candidate'
    custom.append(dict(id=key,name=name,latitude=lat,longitude=lon,purpose='Missionary medical/community supplies',assessment=reason,source=URL['nip'],status='Survey opportunity only; no airstrip designed or generated'))

# Master has one row per physical destination; original membership reconciliation stays explicit.
original=json.loads((ROOT/'src/Workflow/NamibiaDestinationResearch.json').read_text(encoding='utf-8-sig'))
alias={'Ehomba':'NA-0169','Outapi':'NA-0171','Okangwati':'NA-0160','Okorusu Mine':'FYKR','Okorusu':'FYKR','Keerweder':'NA-0030','Zannier Sonop':'candidate-sonop','Wolwedans':'FYWD','Palmwag':'NA-0168','Geluk':'NA-0067','Okonjima':'FYKN'}
for d in rows:
    d['original_memberships']='; '.join(x['career'] for x in original if (x['identifier'] and x['identifier']==d['id']) or alias.get(x['location'])==d['id'])
    if d['id']=='NA-0160': d['original_memberships']='Missionary (legacy FYKW)'
    d['careers']=[s['career'] for s in scores if s['id']==d['id'] and s['gate']=='Eligible purpose']
    d['recommendation']='; '.join(s['career']+': '+s['decision'] for s in scores if s['id']==d['id']) or ('Base / return only' if d['id']=='FYWB' else 'Custom research only')
    d['aircraft_suitability']='No aircraft approved by this research. ' + ('Known paved runway: candidate for fleet-specific AFM/weight/density-altitude assessment.' if 'ASP' in d['runway'] or 'Asphalt' in d['runway'] else 'Unknown/conflicting field data: hold performance clearance; assess each type separately.')
    if d['id'].startswith(('custom','offshore')): d['aircraft_suitability']='No approved landing operation; research/deferred only.'
    d['observed_date']='2026-10-29'

assert len({d['id'] for d in rows})==len(rows)
for s in scores:
    assert all(x is None or 0<=x<=5 for x in s['parts']) and all(0<=x<=5 for x in s['experience'])
    assert (s['score'] is None)==(s['gate']=='Ineligible purpose')
bundle=dict(as_of='2026-10-29',base_commit='d5ff51650789e45e46c8828893acaba071d957e7',offshore_context_commit='22b9e32dda56a747b7025c66761462943f34fd24',bases=BASE,aircraft=AIR,destinations=rows,scores=scores,custom=custom,sources=URL)
(HERE/'catalogue.json').write_text(json.dumps(bundle,indent=2,ensure_ascii=False)+'\n',encoding='utf-8')
for file,content in [('catalogue.csv',rows),('career-scores.csv',scores)]:
    with (OUT/file).open('w',newline='',encoding='utf-8-sig') as f:
        writer=csv.DictWriter(f,fieldnames=list(content[0]));writer.writeheader()
        writer.writerows({k:json.dumps(v,ensure_ascii=False) if isinstance(v,(list,dict)) else v for k,v in x.items()} for x in content)

# Keep only the country subset and Cape Town cross-border record, not the worldwide download.
for srcname,dstname,field in [('airports.csv','ourairports-na-plus-fact.csv','ident'),('runways.csv','ourairports-runways-na-plus-fact.csv','airport_ident')]:
    path=SRC/srcname
    if not path.exists(): continue
    allrows=list(csv.DictReader(path.open(encoding='utf-8-sig')))
    selected=[r for r in allrows if (r.get('iso_country')=='NA' or r[field]=='FACT')] if field=='ident' else [r for r in allrows if r[field] in A and (A[r[field]]['iso_country']=='NA' or r[field]=='FACT')]
    with (SRC/dstname).open('w',newline='',encoding='utf-8') as f:
        w=csv.DictWriter(f,fieldnames=list(allrows[0]));w.writeheader();w.writerows(selected)

features=[]
for name,(lat,lon) in BASE.items():
    features.append(dict(type='Feature',geometry=dict(type='Point',coordinates=[lon,lat]),properties=dict(kind='base',name=name,careers='Missionary / Safari / Industrial' if name=='Eros' else 'Offshore')))
for d in rows:
    if d['latitude'] is None: continue
    features.append(dict(type='Feature',geometry=dict(type='Point',coordinates=[d['longitude'],d['latitude']]),properties=dict(kind='custom' if d['id'].startswith('custom') else 'destination',id=d['id'],name=d['name'],status=d['verification'],careers=d['careers'],notes=d['notes'],offset_nm=d['offset_nm'])))
    for career in d['careers']:
        if d['id'].startswith(('custom','offshore')): continue
        base=BASE['Walvis Bay' if career=='Offshore' else 'Eros']
        features.append(dict(type='Feature',geometry=dict(type='LineString',coordinates=[[base[1],base[0]],[d['longitude'],d['latitude']]]),properties=dict(kind='proposed-relationship',career=career,destination=d['id'],note='Planning relationship only; not a navigation route or activated mission')))
    if d['offset_nm'] and d['offset_nm']>5:
        features.append(dict(type='Feature',geometry=dict(type='Point',coordinates=[d['sim_lon'],d['sim_lat']]),properties=dict(kind='conflicting-simulator-point',id=d['id'],name='Cached '+d['id']+' (wrong association)',status='Hold: location conflict')))
        features.append(dict(type='Feature',geometry=dict(type='LineString',coordinates=[[d['longitude'],d['latitude']],[d['sim_lon'],d['sim_lat']]]),properties=dict(kind='identity-conflict',destination=d['id'],offset_nm=d['offset_nm'])))
(OUT/'destinations.geojson').write_text(json.dumps(dict(type='FeatureCollection',features=features),indent=2)+'\n',encoding='utf-8')

# Offline schematic map; plain canvas, no CDN/tile service or execution of repository app.
points=[f for f in features if f['geometry']['type']=='Point']
html='''<!doctype html><html lang="en"><meta charset="utf-8"><title>Frontier Pilot destination research</title><style>body{margin:0;background:#f3efe6;color:#263237;font:15px Arial}header{padding:20px 28px;background:#263e43;color:white}main{display:grid;grid-template-columns:minmax(650px,2fr) minmax(330px,1fr);gap:20px;padding:20px}canvas{width:100%;height:780px;background:#fffefa;border:1px solid #c9c9bf}label{display:block;margin:12px 0}button,select{padding:8px}#details{line-height:1.5}small{color:#526167}li{margin:8px 0}</style><header><h2>Places worth flying to · Namibia starter research</h2>29 October 2026 · Review proposals only · Click a point for evidence and career scores</header><main><section><canvas id="map" width="1000" height="1100"></canvas><small>Offline longitude/latitude schematic. Grid is geographic; lines show base relationships, not flyable routes. No satellite or terrain imagery is implied. Cape Town extension appears south of Namibia.</small></section><aside><label>Career <select id="career"><option>All</option><option>Missionary</option><option>Safari</option><option>Industrial</option><option>Offshore</option></select></label><label><input type="checkbox" id="conflicts" checked> Show conflicting simulator locations</label><ul><li>★ Starting base</li><li style="color:#26705f">● Confirmed MSFS database association (scenery uninspected)</li><li style="color:#a47716">● Real-world / simulator unverified</li><li style="color:#bb4938">● Partial / conflicting identity</li><li style="color:#8062a0">◆ Proposed custom service area (clinic, not runway)</li></ul><div id="details">Select a destination. Five legacy code associations have large coordinate discrepancies. Unknown-location candidates and mobile vessels remain in the catalogue but are not plotted.</div></aside></main><script>const D=__DATA__, P=__POINTS__, S=__SCORES__, c=document.querySelector('canvas'),g=c.getContext('2d');const X=x=>60+(x-10.5)/15*880,Y=y=>40+(-16-y)/19*1010;let drawn=[];function draw(){g.clearRect(0,0,1000,1100);g.font='16px Arial';g.strokeStyle='#e0e3dd';g.fillStyle='#6d797a';for(let x=12;x<=24;x+=2){g.beginPath();g.moveTo(X(x),20);g.lineTo(X(x),1060);g.stroke();g.fillText(x+'°E',X(x),1090)}for(let y=-18;y>=-34;y-=2){g.beginPath();g.moveTo(40,Y(y));g.lineTo(960,Y(y));g.stroke();g.fillText(-y+'°S',4,Y(y))}let sel=document.querySelector('#career').value;for(const d of D){if(d.latitude===null||d.id.startsWith('custom')||d.id.startsWith('offshore'))continue;for(const k of d.careers){if(sel!=='All'&&sel!==k)continue;let b=k==='Offshore'?[-22.97972,14.64532]:[-22.60492,17.079002];g.strokeStyle=({Missionary:'#438d9a',Safari:'#be9944',Industrial:'#806a58',Offshore:'#6c66a4'})[k]+'55';g.beginPath();g.moveTo(X(b[1]),Y(b[0]));g.lineTo(X(d.longitude),Y(d.latitude));g.stroke()}}drawn=[];for(const p of P){let t=p.properties,[x,y]=p.geometry.coordinates;if(t.kind==='conflicting-simulator-point'&&!document.querySelector('#conflicts').checked)continue;if(sel!=='All'&&t.kind==='destination'&&!t.careers.includes(sel))continue;let color=t.kind==='custom'?'#8062a0':t.kind==='conflicting-simulator-point'||t.status==='Partially verified'?'#bb4938':t.status==='Confirmed in MSFS 2024'?'#26705f':'#a47716';g.fillStyle=t.kind==='base'?'#172f37':color;g.beginPath();g.arc(X(x),Y(y),t.kind==='base'?9:6,0,7);g.fill();if(t.kind==='base'){g.font='bold 19px Arial';g.fillText('★ '+t.name,X(x)+12,Y(y)-8)}drawn.push({p,x:X(x),y:Y(y)})}}c.onclick=e=>{let b=c.getBoundingClientRect(),x=(e.clientX-b.left)*1000/b.width,y=(e.clientY-b.top)*1100/b.height;let hit=drawn.map(z=>({...z,dist:Math.hypot(z.x-x,z.y-y)})).sort((a,b)=>a.dist-b.dist)[0];if(!hit||hit.dist>22)return;let t=hit.p.properties,d=D.find(z=>z.id===t.id);let el=document.querySelector('#details');el.replaceChildren();function para(v){let p=document.createElement('p');p.textContent=v;el.appendChild(p)}para(t.name);para(t.status||'Operating base');if(d){para(d.facility);para(d.runway);para(d.sim_note);para(d.notes);for(let s of S.filter(z=>z.id===d.id))para(s.career+': '+s.gate+'; suitability '+(s.score??'ineligible')+'/100; flying '+s.flying+'/25; '+s.decision);for(let u of [d.airport_source,...d.source_links,d.map_link].filter(Boolean)){let a=document.createElement('a');a.href=u;a.textContent=u;a.target='_blank';let p=document.createElement('p');p.appendChild(a);el.appendChild(p)}}};document.querySelectorAll('select,input').forEach(e=>e.onchange=draw);draw();</script></html>'''
html=html.replace('__DATA__',json.dumps(rows)).replace('__POINTS__',json.dumps(points)).replace('__SCORES__',json.dumps(scores))
(OUT/'destination-map.html').write_text(html,encoding='utf-8')
print(json.dumps({'destinations':len(rows),'scores':len(scores),'custom':len(custom),'conflicts':[{ 'id':r['id'],'offset_nm':r['offset_nm']} for r in rows if r['offset_nm'] and r['offset_nm']>5],'purpose_eligible':{c:sum(s['career']==c and s['gate']=='Eligible purpose' for s in scores) for c in ['Missionary','Safari','Industrial','Offshore']}},indent=2))
