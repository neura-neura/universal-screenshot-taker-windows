const {JSDOM}=require('jsdom'),fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const root=path.join(__dirname,'../../src/UniversalCapture/BrowserExtension');
const pageSource=fs.readFileSync(path.join(root,'page.js'),'utf8'),controllerSource=fs.readFileSync(path.join(root,'controller.js'),'utf8');
let checks=0;const check=(condition,message)=>{assert.ok(condition,message);checks++;console.log('PASS '+message);};
async function main(){
  const page=new JSDOM('<!doctype html><input value="1">',{runScripts:'dangerously',pretendToBeVisual:true,url:'https://reader.example/book'});
  const panel=new JSDOM('<div id="target"></div><input id="address"><input id="token"><button id="connect"></button><button id="disconnect"></button><div id="status"></div>',{runScripts:'dangerously',pretendToBeVisual:true,url:'https://extension.example/panel?tab=42'});
  const p=page.window,w=panel.window;let active=true,switchAfter=false,captureCalls=0,drawArgs,readerUrl='https://reader.example/book';
  try{
    p.CSS={escape:x=>x};p.Element.prototype.getBoundingClientRect=()=>({x:10,y:20,width:50,height:35});
    w.chrome={tabs:{get:async()=>({id:42,windowId:7,active,url:readerUrl}),captureVisibleTab:async windowId=>{
      check(windowId===7&&p.__ustPage.hidden,'captures only the bound browser window with highlights hidden');captureCalls++;if(switchAfter)active=false;return 'data:image/png;base64,AA==';
    }},scripting:{executeScript:async options=>{
      assert.equal(options.target.tabId,42);
      if(options.files){p.eval(pageSource);return [{frameId:0,result:null}];}
      return [{frameId:0,result:await p.eval('('+options.func.toString()+')('+JSON.stringify(options.args[0])+')')}];
    }}};
    w.fetch=async()=>({blob:async()=>new w.Blob(['test'])});w.createImageBitmap=async()=>({width:2048,height:1536,close(){}});
    w.HTMLCanvasElement.prototype.getContext=()=>({drawImage(...args){drawArgs=args;}});w.HTMLCanvasElement.prototype.toDataURL=()=> 'data:image/png;base64,AA==';
    w.AbortSignal=AbortSignal;w.AbortController=AbortController;w.__ustTest=true;w.eval(controllerSource);
    const signal=new AbortController().signal;
    setTimeout(()=>p.document.querySelector('input').dispatchEvent(new p.MouseEvent('pointerdown',{bubbles:true,button:0,clientX:20,clientY:25})),20);
    const selected=await w.handle({id:'pick',action:'pick-field',outline:true},signal,async()=>true);
    check(selected.found&&selected.session,'picker binds the selected element to the connection session');
    const restored=await w.handle({action:'restore',url:'https://reader.example/book',selector:'input',area:{x:10,y:20,width:200,height:100}},signal,async()=>true);
    check(restored.found&&restored.session&&restored.area.width===200,'reopening the app restores the bound field and area on the same reader');
    const wrongReader=await w.handle({action:'restore',url:'https://other.example/book'},signal,async()=>true);
    check(!wrongReader.found,'saved selection does not restore onto a different reader URL');
    const changed=await w.handle({action:'go',page:5},signal,async()=>true);check(changed.value==='5','controller routes page commands to the selected frame');
    readerUrl='https://elibro.net/es/ereader/ieu/128399';p.document.body.innerHTML='<input value="1">';w.eval('fieldFrame=null');
    const next=await w.handle({action:'go',page:9,url:'https://elibro.net/es/lc/ieu/login_usuario/?next=/es/ereader/ieu/128399/',selector:'input',area:{x:10,y:20,width:200,height:100}},signal,async()=>true);
    check(next.value==='9'&&next.session&&next.url===readerUrl&&p.__ustPage.area.width===200,'another reader restores the saved field and area and returns its new connection binding');
    readerUrl='https://elibro.net/es/lc/ieu/login_usuario/?next=/es/ereader/ieu/128399/';
    let loginBlocked=false;try{await w.handle({action:'go',page:10,url:readerUrl,selector:'input'},signal,async()=>true);}catch(e){loginBlocked=e.message.includes('lector');}
    check(loginBlocked&&p.document.querySelector('input').value==='9','login redirect URL is resolved only for the requested target, never treated as a reader field');
    readerUrl='https://reader.example/book';let otherBlocked=false;try{await w.handle({action:'go',page:20,url:'https://different.example/book',selector:'input'},signal,async()=>true);}catch(e){otherBlocked=e.message.includes('lector');}
    check(otherBlocked&&p.document.querySelector('input').value==='9','automatic field recovery cannot navigate a different connected site');
    p.document.body.innerHTML='<p>Reader without a page input</p>';w.eval('fieldFrame=null');let missingBlocked=false;
    try{await w.handle({action:'go',page:10,url:readerUrl,selector:'input'},signal,async()=>true);}catch(e){missingBlocked=e.message.includes('campo guardado');}
    check(missingBlocked,'a changed reader reports that its saved field cannot be recovered');
    p.document.body.innerHTML='<input value="1">';await w.handle({action:'go',page:5,url:readerUrl,selector:'input'},signal,async()=>true);
    const area={x:100,y:120,width:400,height:280};p.__ustPage.area=area;const result=await w.handle({id:'capture',action:'capture',area,stable:true,waitSeconds:.2},signal,async()=>true);
    check(captureCalls===1,'a stable page produces exactly one browser screenshot');
    check(result.image.startsWith('data:image/png;base64,')&&drawArgs[1]===200&&drawArgs[2]===240&&drawArgs[3]===800&&drawArgs[4]===560,'capture crops CSS coordinates using actual screenshot scale');
    check(!p.__ustPage.hidden,'successful capture restores highlights');
    const before=captureCalls;const readyStart=Date.now();await w.handle({id:'ready',action:'ready',waitSeconds:.2,stable:true},signal,async()=>true);
    check(captureCalls===before&&Date.now()-readyStart>=200,'stable countdown waits inside the page without repeated screenshots');
    w.fetch=async url=>({ok:true,json:async()=>({app:'UniversalCapture',token:'A'.repeat(48)})});
    const connection=await w.discover(signal);check(connection.address==='http://127.0.0.1:38473'&&connection.token.length===48,'automatic discovery needs no user-entered address or key');
    w.fetch=async()=>({blob:async()=>new w.Blob(['test'])});
    switchAfter=true;let rejected=false;try{await w.handle({action:'capture',area},signal,async()=>true);}catch(e){rejected=e.message.includes('pestaña');}
    check(rejected&&!p.__ustPage.hidden&&captureCalls===2,'tab switch discards the capture and restores highlights');
    await w.__ustControllerMessage({action:'disconnect'});
    check(p.__ustPage.suspended,'disconnect hides markers even when the reader tab is inactive');
    await p.__ustCommand({action:'outline',outline:true});
    w.fetch=async url=>{if(url.endsWith('/connection'))return {ok:true,json:async()=>({app:'UniversalCapture',token:'A'.repeat(48)})};throw Error('Application closed');};
    const reconnecting=w.connect();
    for(let i=0;i<100&&!p.__ustPage.suspended;i++)await new Promise(r=>setTimeout(r,10));
    check(p.__ustPage.suspended,'loss of the app connection hides markers before retrying discovery');
    await w.__ustControllerMessage({action:'disconnect'});await reconnecting;
    console.log(`Controller: ${checks} checks passed.`);
  }finally{page.window.close();panel.window.close();}
}
main().catch(e=>{console.error(e);process.exitCode=1;});
