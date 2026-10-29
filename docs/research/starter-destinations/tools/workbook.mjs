import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { Workbook, SpreadsheetFile } from '@oai/artifact-tool';
const here=path.dirname(fileURLToPath(import.meta.url));
const root=path.resolve(here,'../../../..');
const out=path.join(root,'outputs/starter-destinations-20261029');
const d=JSON.parse(await fs.readFile(path.join(here,'../catalogue.json'),'utf8'));
const wb=Workbook.create();
const names=['Career shortlists','Catalogue','Scoring','Aircraft and method','Custom opportunities'];
const sheets=Object.fromEntries(names.map(n=>[n,wb.worksheets.add(n)]));
const col=n=>{let r='';for(n++;n;n=Math.floor((n-1)/26))r=String.fromCharCode(65+(n-1)%26)+r;return r};
function base(s,last,rows,title){s.showGridLines=false;s.getRange(`A1:${last}${rows}`).format.font={name:'Arial',size:10,color:'#24383D'};s.getRange(`A1:${last}${rows}`).format.verticalAlignment='center';s.getRange(`A1:${last}${rows}`).format.rowHeight=30;s.getRange('A2').values=[[title]];s.getRange('A2').format.font={name:'Arial',size:15,bold:true};s.getRange(`A2:${last}2`).format.rowHeight=30;s.getRange(`A2:${last}2`).format.borders={bottom:{style:'thin',color:'#95AAA3'}};s.tabColor='#304E53'}
function table(s,headers,values,name,widths){const last=col(headers.length-1),end=5+values.length;s.getRange(`A5:${last}5`).values=[headers];if(values.length)s.getRange(`A6:${last}${end}`).values=values;s.tables.add(`A5:${last}${end}`,true,name);s.getRange(`A5:${last}5`).format={fill:'#304E53',font:{name:'Arial',size:10,bold:true,color:'#FFFFFF'},rowHeight:44,wrapText:true,horizontalAlignment:'center',verticalAlignment:'center'};widths.forEach((w,i)=>s.getRange(`${col(i)}5:${col(i)}${end}`).format.columnWidth=w);s.freezePanes.freezeRows(5);s.freezePanes.freezeColumns(2);return end}
function fitRows(s,values,widths,first=6){values.forEach((v,i)=>{const lines=Math.max(...v.map((x,j)=>String(x??'').split('\n').reduce((n,t)=>n+Math.max(1,Math.ceil(t.length/(widths[j]||30))),0)));s.getRange(`A${i+first}:${col(v.length-1)}${i+first}`).format.rowHeight=Math.max(42,lines*14+12)})}
const method=sheets['Aircraft and method'];base(method,'H',42,'Aircraft and scoring assumptions · research only');
method.getRange('A4:H4').values=[['Aircraft ID','Implemented type','Planning KTAS','Airborne allowance min','Career','Time model','Ground min','Approval boundary']];
method.getRange('A5:H12').values=d.aircraft.map((a,i)=>[a[0],a[1],a[2],a[3],a[4],i<6?'1.10 × direct NM / KTAS × 60 + allowance':i===6?'Under 60 NM: 170 kt + 10; else 220 kt + 15':'Disabled; hypothetical only',i===6?25:15,'Planning assumption; no payload/range/runway clearance']);
method.getRange('A15:E19').values=[['Career','Purpose','Runway','Access','Variety'],['Missionary',.4,.15,.2,.25],['Safari',.4,.15,.2,.25],['Industrial',.4,.2,.2,.2],['Offshore',.4,.2,.2,.2]];
method.getRange('B16:E19').setNumberFormat('0%');
const methodNotes=[
['Suitability /100','20 × SUMPRODUCT(four 0–5 criteria, career weights). Ineligible purpose leaves score blank. A high score does not override landing, identity or aircraft holds.'],
['Experience /25','SUM(terrain, approach, runway interest, remoteness, network diversity). Anticipated design judgement; no new flight or scenery inspection. Never averaged with suitability.'],
['Rubric 0–5','0 absent/incompatible; 1 major gaps; 2 weak/limited; 3 credible with material limits; 4 strong evidence/fit; 5 strongest purpose/network fit. Runway 1 means unknown, not a short-strip claim.'],
['Eligibility','Purpose gate is conceptual career eligibility. All performance decisions remain conditional; no aircraft is approved by this research. Unconsidered career pairs are ineligible for lack of evidenced purpose.'],
['Time and distance','Spherical great-circle distance, radius 3440.065 NM (planning approximation to WGS84). 10% routing factor; conservative cruise plus climb/circuit allowance. No wind/terrain/weather clearance.'],
['Ground operations','Times shown are airborne minutes, rounded up. Add 15 min piston/turboprop or 25 min jet for indicative ground/block time, then any lodge/clinic road transfer.'],
['Uncertainty','Allow ±20% airborne planning uncertainty, more with headwind/routing. Fuel/reserves, alternates, field length/width, slope, surface, obstacles, temperature and mass must be checked for exact simulator aircraft.'],
['Shortlist behavior','Live SORT/FILTER views include purpose-eligible Priority validation and Extended network only. Scoring contains every held/rejected pair. Changing a 0–5 input updates score and shortlist order.'],
['MSFS meaning','Confirmed = geographic association with cached facility (or retained manual evidence). Database presence does not confirm runway/scenery, spawning, parking or passenger/payload compatibility.'],
['Coordinate screen','Offset >5 NM is a conservative research conflict flag, not new runtime policy. Preserve immutable accepted route coordinates; later correction needs reviewed versioning.'],
['Evidence dates','Research 2026-10-29; local airport-only cache observed 2026-10-27, SDK 1.2.4 / simulator 1,9,21,0. Dataset downloaded 2026-10-29. No simulator query or launch in this task.'],
['Workbook scope','One destination master; career scores stored once on Scoring and linked into Catalogue. Static CSV/JSON/map exports are dated snapshots; regenerate to reflect changes.'],
['Runway units','Database feet converted ×0.3048 to metres; longest open runway selected where data exists. Detailed conflicting operator reports remain in landing notes; no guessed width or orientation.'],
['Aircraft evidence','Manufacturer maximum performance is context only, not exact MSFS/add-on performance. Dispatch kg budgets are gameplay fixtures and never performance limits.'],
];method.getRange('A22:B35').values=methodNotes;method.getRange('B22:H35').merge(true);method.getRange('B22:H35').format.wrapText=true;method.getRange('A22:H35').format.rowHeight=56;
method.getRange('A4:H4').format={fill:'#304E53',font:{bold:true,color:'#FFFFFF'},wrapText:true,rowHeight:42};method.getRange('A5:H12').format.wrapText=true;method.getRange('A5:H12').format.rowHeight=72;method.getRange('A15:E15').format={fill:'#304E53',font:{bold:true,color:'#FFFFFF'}};
[22,32,18,19,23,43,18,46].forEach((w,i)=>method.getRange(`${col(i)}1:${col(i)}42`).format.columnWidth=w);
method.getRange('C5:D12').format.fill='#F3E7BD';method.getRange('B16:E19').format.fill='#F3E7BD';

const sc=sheets.Scoring;const sh=['Career','Destination key','Destination','Purpose gate','Recommendation','Purpose 0–5','Runway 0–5','Access 0–5','Variety 0–5','Suitability /100','Terrain 0–5','Approach 0–5','Runway interest 0–5','Remoteness 0–5','Diversity 0–5','Experience /25','Aircraft assessment','Base distance NM','Airborne min (reference type)','Operational purpose / inference','Research notes','Shortlist inclusion','Sort tie key','Career view','Key view','Name view','Suitability view','Experience view','Decision view','Minutes view','Purpose view'];
base(sc,'AE',d.scores.length+5,'Career assessments · editable 0–5 inputs, separate hard holds');
const idx=new Map(d.destinations.map((x,i)=>[x.id,i+6]));
const sr=new Map();const vals=d.scores.map((s,i)=>{sr.set(s.career+'|'+s.id,i+6);return [s.career,s.id,s.name,s.gate,s.decision,...s.parts,null,...s.experience,null,s.fleet,null,null,s.purpose,s.reason,null,null,null,null,null,null,null,null,null,null]});
table(sc,sh,vals,'CareerAssessments',[17,20,36,23,36,14,14,14,14,17,14,14,14,14,14,18,42,17,21,60,65,18,17,17,20,36,17,18,36,17,60]);
for(let i=0;i<d.scores.length;i++){const s=d.scores[i],r=i+6,m={Missionary:16,Safari:17,Industrial:18,Offshore:19}[s.career],cr=idx.get(s.id),tcol={Missionary:'V',Safari:'X',Industrial:'V',Offshore:'AA'}[s.career],dist=s.career==='Offshore'?'T':'S';
sc.getRange(`J${r}`).formulas=[[`=IF(D${r}="Eligible purpose",20*SUMPRODUCT(F${r}:I${r},'Aircraft and method'!$B$${m}:$E$${m}),"")`]];
sc.getRange(`P${r}`).formulas=[[`=SUM(K${r}:O${r})`]];
sc.getRange(`R${r}`).formulas=[[`=IF(Catalogue!${dist}${cr}="","",Catalogue!${dist}${cr})`]];
sc.getRange(`S${r}`).formulas=[[`=IF(Catalogue!${tcol}${cr}="","",Catalogue!${tcol}${cr})`]];
sc.getRange(`V${r}`).formulas=[[`=IF(AND(D${r}="Eligible purpose",OR(E${r}="Priority validation",E${r}="Extended network")),1,0)`]];
sc.getRange(`W${r}`).formulas=[[`=IF(V${r}=1,J${r}+P${r}/1000+(1000-ROW())/1000000,-1)`]];
sc.getRange(`X${r}:AE${r}`).formulas=[[`=A${r}`,`=B${r}`,`=C${r}`,`=J${r}`,`=P${r}`,`=E${r}`,`=IF(S${r}="","",S${r})`,`=T${r}`]];
}
sc.getRange(`F6:I${d.scores.length+5}`).dataValidation={rule:{type:'whole',operator:'between',formula1:0,formula2:5}};
sc.getRange(`K6:O${d.scores.length+5}`).dataValidation={rule:{type:'whole',operator:'between',formula1:0,formula2:5}};
sc.getRange(`F6:I${d.scores.length+5}`).format.fill='#F3E7BD';sc.getRange(`K6:O${d.scores.length+5}`).format.fill='#F3E7BD';sc.getRange(`J6:J${d.scores.length+5}`).setNumberFormat('0.0');sc.getRange(`A6:AE${d.scores.length+5}`).format.rowHeight=116;for(const c of ['C','D','E','Q','T','U'])sc.getRange(`${c}6:${c}${d.scores.length+5}`).format.wrapText=true;
fitRows(sc,vals,[17,20,36,23,36,14,14,14,14,17,14,14,14,14,14,18,42,17,21,60,65,18,17,17,20,36,17,18,36,17,60]);

const cat=sheets.Catalogue;const heads=['Destination key','Destination name','Associated eligible careers','Region','Verification status','Recommendation / holds','Latitude WGS84','Longitude WGS84','Elevation ft','Database runway length m','Database runway width m','Database surface','Database runway orientation','Landing evidence / conflicts','Nearby facility / community','Operational purpose','Aircraft suitability','Simulator evidence','Eros direct NM','Walvis Bay direct NM','C152 airborne min','C208 airborne min','Baron airborne min','Bonanza airborne min','PA-28 airborne min','Cub airborne min','Vision Jet airborne min','H125 hypothetical min','Missionary gate','Missionary /100','Missionary experience /25','Safari gate','Safari /100','Safari experience /25','Industrial gate','Industrial /100','Industrial experience /25','Offshore gate','Offshore /100','Offshore experience /25','Existing memberships','Sim offset NM','Purpose / operator sources','Airport source','Coordinate basis','Research notes','OSM review link'];
heads.push('Terrain / environment','Identifier classification','Research date');
base(cat,'AX',d.destinations.length+5,'Master destinations · one row per place · research proposals');
const cv=d.destinations.map(x=>[x.id,x.name,x.careers.join(' / '),x.region,x.verification,x.recommendation,x.latitude,x.longitude,x.elevation_ft,x.length_m,x.width_m,x.surface,x.orientation,x.runway,x.facility,null,x.aircraft_suitability,x.sim_note,x.distance_eros,x.distance_walvis,...Array(8).fill(null),...Array(12).fill(null),x.original_memberships,x.offset_nm,x.source_links.join('\n'),x.airport_source,x.coordinate_basis,x.notes,x.map_link]);
cv.forEach((v,i)=>v.push(d.destinations[i].terrain,d.destinations[i].identifier_kind,d.destinations[i].observed_date));
table(cat,heads,cv,'DestinationCatalogue',[22,37,25,19,39,55,18,18,15,18,18,23,20,62,53,64,58,65,18,20,18,18,18,18,18,18,20,21,26,18,22,26,18,22,26,18,22,26,18,22,35,18,68,55,49,73,68,52,52,18]);
for(let i=0;i<d.destinations.length;i++){const x=d.destinations[i],r=i+6;
cat.getRange(`P${r}`).values=[[d.scores.filter(s=>s.id===x.id).map(s=>s.career+': '+s.purpose).join('\n')||'Base / custom service-area research only']];
for(let a=0;a<8;a++){const mr=a+5,c=col(20+a),dist=a>=6?'T':'S';let expr=a===6?`ROUNDUP(${dist}${r}*1.10/IF(${dist}${r}<60,170,'Aircraft and method'!C${mr})*60+IF(${dist}${r}<60,10,'Aircraft and method'!D${mr}),0)`:`ROUNDUP(${dist}${r}*1.10/'Aircraft and method'!C${mr}*60+'Aircraft and method'!D${mr},0)`;cat.getRange(`${c}${r}`).formulas=[[`=IF(${dist}${r}="","",${expr})`]];}
for(let a=0;a<4;a++){const career=['Missionary','Safari','Industrial','Offshore'][a],srow=sr.get(career+'|'+x.id),c=28+a*3;
cat.getRange(`${col(c)}${r}`).values=[[srow?d.scores[srow-6].gate:'Ineligible: no evidenced purpose']];if(srow)cat.getRange(`${col(c+1)}${r}:${col(c+2)}${r}`).formulas=[[`=IF(Scoring!J${srow}="","",Scoring!J${srow})`,`=Scoring!P${srow}`]];
}}
cat.getRange(`G6:H${d.destinations.length+5}`).setNumberFormat('0.000000');cat.getRange(`I6:K${d.destinations.length+5}`).setNumberFormat('0.0');cat.getRange(`S6:T${d.destinations.length+5}`).setNumberFormat('0.0');cat.getRange(`A6:AU${d.destinations.length+5}`).format.rowHeight=140;
cat.getRange(`AV6:AW${d.destinations.length+5}`).format.wrapText=true;
for(const c of ['B','C','E','F','N','O','P','Q','R','AC','AF','AI','AL','AO','AQ','AR','AS','AT','AU'])cat.getRange(`${c}6:${c}${d.destinations.length+5}`).format.wrapText=true;
fitRows(cat,cv,[22,37,25,19,39,55,18,18,15,18,18,23,20,62,53,64,58,65,18,20,18,18,18,18,18,18,20,21,26,18,22,26,18,22,26,18,22,26,18,22,35,18,68,55,49,73,68]);
for(const c of ['AD','AG','AJ','AM'])cat.getRange(`${c}6:${c}${d.destinations.length+5}`).conditionalFormats.add('dataBar',{color:'#7DAA96'});
cat.getRange(`E6:E${d.destinations.length+5}`).conditionalFormats.add('containsText',{text:'Partially',format:{fill:'#F8DCD1',font:{color:'#893A28'}}});

const sum=sheets['Career shortlists'];base(sum,'H',113,'Ranked validation shortlists · no flight approval');sum.getRange('A3:H3').merge();sum.getRange('A3').values=[['Purpose first; suitability /100 and anticipated flying /25 stay separate. Held/rejected pairs remain on Scoring.']];sum.getRange('A3:H3').format.wrapText=true;sum.getRange('A3:H3').format.rowHeight=35;
const shortlistHeaders=['Career','Key','Destination','Suitability /100','Flying /25','Recommendation','Airborne min','Operational reason'];
const sectionRows=[5,32,59,86];const end=d.scores.length+5;
for(let i=0;i<4;i++){const c=['Missionary','Safari','Industrial','Offshore'][i],r=sectionRows[i];sum.getRange(`A${r}:H${r}`).values=[shortlistHeaders];sum.getRange(`A${r}:H${r}`).format={fill:'#304E53',font:{bold:true,color:'#FFFFFF'},rowHeight:42,wrapText:true};sum.getRange(`A${r+1}`).formulas=[[`=SORT(FILTER(Scoring!X6:AE${end},(Scoring!A6:A${end}="${c}")*(Scoring!V6:V${end}=1)),4,-1)`]];sum.getRange(`A${r+1}:H${r+25}`).format.rowHeight=72;sum.getRange(`C${r+1}:C${r+25}`).format.wrapText=true;sum.getRange(`F${r+1}:F${r+25}`).format.wrapText=true;sum.getRange(`H${r+1}:H${r+25}`).format.wrapText=true;}
[18,20,38,20,17,30,20,74].forEach((w,i)=>sum.getRange(`${col(i)}1:${col(i)}113`).format.columnWidth=w);
const custom=sheets['Custom opportunities'];base(custom,'G',12,'Phase-two service areas · clinic points are not runway sites');
table(custom,['Research key','Service area','Latitude','Longitude','Purpose','Assessment / prerequisites','Source'],d.custom.map(x=>[x.id,x.name,x.latitude,x.longitude,x.purpose,x.assessment,x.source]),'CustomResearch',[26,34,17,17,40,84,82]);custom.getRange('A6:G8').format.rowHeight=125;custom.getRange('E6:G8').format.wrapText=true;custom.getRange('C6:D8').setNumberFormat('0.000000');
wb.recalculate();
console.log((await wb.inspect({kind:'table',range:'Career shortlists!A5:H10',include:'values,formulas',tableMaxRows:6,tableMaxCols:8,maxChars:2500})).ndjson);
const errors=await wb.inspect({kind:'match',searchTerm:'#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A|#NUM!|#NULL!|#SPILL!|#CALC!',options:{useRegex:true,maxResults:50},summary:'formula error scan'});console.log(errors.ndjson);
// Meaningful independent checks and one reversible input sensitivity before final export.
const before=sc.getRange('J6').values[0][0];const old=sc.getRange('F6').values[0][0];sc.getRange('F6').values=[[0]];wb.recalculate();const after=sc.getRange('J6').values[0][0];if(Math.abs((before-after)-old*8)>1e-7)throw Error('Weighted score sensitivity failed');sc.getRange('F6').values=[[old]];wb.recalculate();
// Verify the actual ranked result reacts, not merely its source score.
const lead=sc.getRange(`F${sr.get('Missionary|FYAM')}`).values[0][0];sc.getRange(`F${sr.get('Missionary|FYAM')}`).values=[[0]];wb.recalculate();if(sum.getRange('B6').values[0][0]==='FYAM')throw Error('Shortlist did not reorder');sc.getRange(`F${sr.get('Missionary|FYAM')}`).values=[[lead]];wb.recalculate();
const checks=[];for(let i=0;i<d.scores.length;i++){const r=i+6,s=d.scores[i],actual=sc.getRange(`J${r}`).values[0][0],experience=sc.getRange(`P${r}`).values[0][0];if(s.score!==null&&Math.abs(actual-s.score)>1e-7)throw Error('Score reconciliation '+s.id);if(experience!==s.flying)throw Error('Experience reconciliation '+s.id);checks.push({id:s.id,career:s.career,score:actual,experience});}
for(const [name,range] of [['Career shortlists','A5:H12'],['Catalogue','A5:F9'],['Scoring','A5:J9'],['Aircraft and method','A4:H8'],['Custom opportunities','A5:G8']]){const png=await wb.render({sheetName:name,range,scale:1.25,format:'png'});await fs.writeFile(path.join(here,'preview-'+name.replaceAll(' ','-')+'.png'),new Uint8Array(await png.arrayBuffer()));}
await fs.writeFile(path.join(here,'workbook-validation.json'),JSON.stringify({formulaErrorScan:errors.ndjson,sensitivity:{before,after,restored:sc.getRange('J6').values[0][0]},scores:checks},null,2));
const file=await SpreadsheetFile.exportXlsx(wb);await file.save(path.join(out,'starter-destination-catalogue.xlsx'));console.log(`Exported workbook; all ${d.scores.length} weighted/experience checks and score/ranking sensitivity passed.`);
