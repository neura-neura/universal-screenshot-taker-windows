const fs=require('node:fs'),path=require('node:path'),vm=require('node:vm'),assert=require('node:assert/strict');
const root=path.join(__dirname,'../../src/UniversalCapture/BrowserExtension');
let listener,created=0,exists=false,captured=0,lastOptions;
const api={offscreen:{hasDocument:async()=>exists,createDocument:async options=>{assert.equal(options.reasons[0],'BLOBS');created++;exists=true;}},runtime:{getURL:file=>'chrome-extension://test/'+file,onMessage:{addListener:fn=>listener=fn},sendMessage:async message=>({status:'connected',tabId:message.tabId})},tabs:{get:async id=>({id,active:true}),captureVisibleTab:async()=>{captured++;return 'data:image/png;base64,AA==';}},scripting:{executeScript:async options=>{lastOptions=options;return [{frameId:0,result:{ok:true}}];}}};
vm.runInNewContext(fs.readFileSync(path.join(root,'background.js'),'utf8'),{chrome:api});
const request=(action,extra={},sender={url:api.runtime.getURL('panel.html')})=>new Promise(resolve=>listener({target:'background',action,...extra},sender,resolve));
(async()=>{
  const manifest=JSON.parse(fs.readFileSync(path.join(root,'manifest.json'),'utf8'));
  assert.equal(manifest.action.default_popup,'panel.html');assert.ok(!api.windows);
  await request('start',{tabId:42});await request('start',{tabId:42});assert.equal(created,1);
  // Requests still reach the controller after the popup caller has gone away.
  const controller={url:api.runtime.getURL('offscreen.html')};
  assert.ok((await request('capture',{windowId:7},controller)).value.startsWith('data:image'));assert.equal(captured,1);
  await request('invoke',{options:{target:{tabId:42,frameIds:[0]},args:[{action:'go',page:3}]}},controller);
  assert.equal(lastOptions.target.tabId,42);assert.equal(lastOptions.args[0].page,3);assert.equal(typeof lastOptions.func,'function');
  assert.equal((await request('capture',{windowId:7})).error,'Invalid controller');
  console.log('Background: 5 checks passed (normal popup, single hidden controller, capture after popup closes, DOM routing, caller restriction).');
})().catch(error=>{console.error(error);process.exitCode=1;});
