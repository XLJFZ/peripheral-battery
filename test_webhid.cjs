// Test the actual inline WebHID protocol functions with simulated OS reports.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const html = fs.readFileSync(path.join(__dirname,'web/index.html'),'utf8');
const code = html.split('// WEBHID_PROTOCOL_START')[1].split('// WEBHID_PROTOCOL_END')[0];
const CATALOG = JSON.parse(fs.readFileSync(path.join(__dirname,'razer_devices.json'),'utf8'));
const c = vm.createContext({CATALOG, Uint8Array, DataView, setTimeout:fn=>{fn();}});
vm.runInContext(code,c);
function report(value=128, prefix=false, cmd=0x80) {
  const b = new Uint8Array(90); b[0]=2;b[1]=0x1f;b[5]=2;b[6]=7;b[7]=cmd;b[9]=value;
  for(let i=2;i<88;i++)b[88]^=b[i];
  if(prefix){const full=new Uint8Array(91);full.set(b,1);return new DataView(full.buffer);}
  return new DataView(b.buffer);
}
const e=CATALOG.find(e=>e.pid===0xb7);
function device(options={}){return {vendorId:0x1532,productId:0xb7,productName:'Razer DeathAdder V3 Pro',opened:true,collections:[{featureReports:[{reportId:0}]}],async sendFeatureReport(id,p){this.cmd=p[7]},async receiveFeatureReport(){return report(this.cmd===0x84?0:128,true,this.cmd)},...options};}
(async()=>{
  assert.equal(c.parseRazer(report(),e,0x80),128);
  assert.equal(c.parseRazer(report(255,true),e,0x80),255);
  assert.equal(c.parseRazer(report(0),e,0x80),0);
  const bad=report();bad.setUint8(9,32);assert.throws(()=>c.parseRazer(bad,e,0x80),/校验/);
  assert.throws(()=>c.parseRazer(report(128,false,0x84),e,0x80),/其他查询/);
  assert.throws(()=>c.reportPayload(new DataView(new ArrayBuffer(89)),0,90),/长度/);
  const raw=new Uint8Array(100);raw.set(new Uint8Array(report(255,true).buffer),4);
  assert.equal(c.parseRazer(new DataView(raw.buffer,4,91),e,0x80),255);
  for(const prefixed of [false,true]){
    const a7=new Uint8Array(prefixed?65:64);if(prefixed)a7[0]=0x11;a7[(prefixed?1:0)+12]=75^0xff;
    assert.equal(c.reportPayload(new DataView(a7.buffer),0x11,64)[12]^0xff,75);
  }
  for(const prefixed of [false,true]){
    const g=new Uint8Array(prefixed?8:7);if(prefixed)g[0]=6;g[(prefixed?1:0)+2]=62;
    assert.equal(c.reportPayload(new DataView(g.buffer),6,7)[2],62);
  }
  const ok=await c.readDevice(device());assert.equal(ok.percent,50);assert.equal(ok.charging,false);
  let writes=0;
  const blocked=device({collections:[],sendFeatureReport:async()=>{writes++}});
  const row=await c.readDevice(blocked);assert.equal(row.percent,null);assert.match(row.error,/浏览器未开放/);assert.equal(writes,0);
  const denied=await c.readDevice(device({sendFeatureReport:async()=>{throw Object.assign(new Error('denied'),{name:'NotAllowedError'})}}));
  assert.match(denied.error,/浏览器拒绝/);
  const nested=c.featureReports([{featureReports:[],children:[{featureReports:[{reportId:0}]}]}]);assert.equal(nested.length,1);
  assert.equal(c.visibleInterfaces([device(),device({productName:'',collections:[]})]).length,1);
  assert.equal(c.visibleInterfaces([device(),device()]).length,2);
  console.log('WebHID protocol tests passed: raw/prefixed reports, offsets, validation, blocked interfaces and errors');
})().catch(e=>{console.error(e);process.exitCode=1;});
