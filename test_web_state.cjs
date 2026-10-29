// Exercise the actual page's refresh/freshness/export behavior without hardware.
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const html=fs.readFileSync(path.join(__dirname,'web/index.html'),'utf8');
class Element {
  constructor(){this.children=[];this.dataset={};this.style={};this.attributes={};this.textContent='';}
  append(...items){this.children.push(...items)}
  replaceChildren(...items){this.children=items}
  setAttribute(key,value){this.attributes[key]=value}
  click(){}
}
let now=Date.now(),fail=false,lastBlob;
class Clock extends Date {static now(){return now}}
const elements=new Map();
const events={};
let payload={updated:now/1000,errors:[],readings:[{name:'Mouse',provider:'Razer HID',percent:90,raw_value:230,sampled_at:now/1000,charging:false}]};
const calls=[];
const context=vm.createContext({Date:Clock,URLSearchParams,AbortController,Blob,console,
  location:{search:'?token=LOCAL_SECRET',hostname:'127.0.0.1'},navigator:{},
  document:{getElementById(id){if(!elements.has(id))elements.set(id,new Element());return elements.get(id)},createElement(){return new Element()},addEventListener(name,fn){events[name]=fn}},
  fetch:async url=>{calls.push(url);if(fail)throw Error('offline');return {ok:true,json:async()=>payload}},
  setTimeout:()=>1,clearTimeout(){},setInterval:()=>2,clearInterval(){},
  URL:{createObjectURL(blob){lastBlob=blob;return 'blob:test'},revokeObjectURL(){}}
});
const code=html.split('<script>')[1].split('</script>')[0];
const settle=()=>new Promise(resolve=>setImmediate(resolve));
(async()=>{
  vm.runInContext(code,context);
  await settle();
  assert.equal(elements.get('count').textContent,1);
  assert.match(elements.get('updated').textContent,/读取于/);
  elements.get('export').onclick();
  const exported=await lastBlob.text();
  assert(!exported.includes('LOCAL_SECRET'));
  assert.equal(JSON.parse(exported).readings[0].raw_value,230);
  elements.get('refresh').onclick();await settle();
  assert(calls.at(-1).includes('&fresh=1'));
  now+=91000;
  vm.runInContext('updateAge()',context);
  assert.equal(elements.get('count').textContent,0);
  assert.equal(elements.get('export').disabled,true);
  assert.match(elements.get('message').textContent,/过期/);
  payload={updated:now/1000,errors:[],readings:[{name:'Keyboard',provider:'Test',percent:null,error:'No verified battery query is available'}]};
  vm.runInContext('refresh(true)',context);await settle();
  assert.equal(elements.get('unknown').textContent,1);
  assert.equal(elements.get('count').textContent,0);
  const card=elements.get('devices').children[0];
  assert.equal(card.children[3].attributes['aria-hidden'],'true');
  assert.match(card.children[4].textContent,/尚未适配/);
  fail=true;
  vm.runInContext('refresh(true)',context);await settle();
  assert.equal(elements.get('unknown').textContent,0);
  assert.match(elements.get('message').textContent,/读取失败/);
  assert.equal(elements.get('updated').textContent,'暂无有效读取时间');
  console.log('Web state tests passed: fresh scan, true timestamp, stale clearing, unknown state, failed scan and token-free export');
})().catch(error=>{console.error(error);process.exitCode=1});
