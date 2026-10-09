const {JSDOM}=require('jsdom');
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const source=fs.readFileSync(path.join(__dirname,'../../src/UniversalCapture/BrowserExtension/page.js'),'utf8');
let checks=0;
const check=(condition,message)=>{assert.ok(condition,message);console.log('PASS '+message);checks++;};
async function main(){
  const dom=new JSDOM('<!doctype html><main id="reader"><input class="page-number" value="1"><div id="page">Page 1</div></main>',{runScripts:'dangerously',pretendToBeVisual:true,url:'https://reader.example/book'}),w=dom.window;
  try{
    w.CSS={escape:x=>x};let overlay;
    const attach=w.Element.prototype.attachShadow;w.Element.prototype.attachShadow=function(options){const root=attach.call(this,options);if(options.mode==='closed')overlay=root;return root;};
    const positions=new WeakMap();w.Element.prototype.getBoundingClientRect=function(){return positions.get(this)||{x:10,y:10,width:100,height:30};};
    const field=w.document.querySelector('input');positions.set(field,{x:30,y:50,width:50,height:35});
    let inputs=0,changes=0,enters=0;
    const wire=node=>{node.addEventListener('input',()=>inputs++);node.addEventListener('change',()=>changes++);node.addEventListener('keydown',e=>{if(e.key==='Enter'){enters++;w.document.querySelector('#page').textContent='Page '+node.value;}});};wire(field);
    w.eval(source);w.eval(source);check(w.document.querySelectorAll('body + div').length===1&&overlay,'isolated overlay initializes once');
    const command=w.__ustCommand;
    const pointer=(node,type,x,y)=>node.dispatchEvent(new w.MouseEvent(type,{bubbles:true,composed:true,cancelable:true,button:0,clientX:x,clientY:y}));
    w.document.documentElement.style.cursor='text';
    await command({id:'field',action:'pick-field',outline:true});pointer(field,'pointermove',40,60);pointer(field,'pointerdown',40,60);pointer(field,'click',40,60);
    const selected=await command({action:'poll-pick'});check(selected.found&&selected.rect.width===50,'anonymous clicked input is selected as a DOM element');
    check(w.document.documentElement.style.cursor==='text','selection restores the page cursor');
    check(overlay.querySelector('style').textContent.includes('border:2px dashed')&&overlay.querySelector('style').textContent.includes('rgba(0,120,212,.12)'),'field and area use dashed borders and translucent fills');
    await command({action:'go',page:2});check(field.value==='2'&&w.document.querySelector('#page').textContent==='Page 2'&&inputs===1&&changes===1&&enters===1,'page replaces value and dispatches input/change/Enter internally');
    positions.set(field,{x:280,y:160,width:65,height:35});await command({action:'outline',outline:true});check(overlay.querySelector('.field').style.left==='280px','field highlight follows the component after layout changes');
    const replacement=field.cloneNode();field.replaceWith(replacement);positions.set(replacement,{x:280,y:160,width:65,height:35});wire(replacement);
    await command({action:'go',page:3});check(replacement.value==='3'&&w.document.querySelector('#page').textContent==='Page 3','component replacement resolves through its verified DOM route');
    await command({id:'area',action:'pick-area',outline:true,twoCorners:false});pointer(w.document.body,'pointerdown',500,400);pointer(w.document.body,'pointermove',100,120);pointer(w.document.body,'pointerup',100,120);pointer(w.document.body,'click',100,120);
    const area=await command({action:'poll-pick'});check(area.found&&area.rect.x===100&&area.rect.y===120&&area.rect.width===400&&area.rect.height===280,'reverse drag produces a viewport area independent of screen origin');
    await command({action:'hide'});check(overlay.host.style.visibility==='hidden','capture hides DOM markers');await command({action:'show'});check(overlay.host.style.visibility==='visible','capture restores DOM markers');
    const corner=overlay.querySelector('[data-edge="se"]');pointer(corner,'pointerdown',500,400);pointer(w.document.body,'pointermove',600,450);pointer(w.document.body,'pointerup',600,450);
    let resized=await command({action:'viewport',area:{x:0,y:0,width:3,height:3}});check(resized.rect.width===500&&resized.rect.height===330,'corner resizing updates the actual capture region and ignores stale app geometry');
    const left=overlay.querySelector('[data-edge="w"]');pointer(left,'pointerdown',100,120);pointer(w.document.body,'pointermove',150,200);pointer(w.document.body,'pointerup',150,200);resized=await command({action:'viewport'});check(resized.rect.x===150&&resized.rect.width===450,'edge resizing changes only its axis');
    const wait=command({action:'ready',waitSeconds:.25,stable:true});await new Promise(r=>setTimeout(r,70));check(!overlay.querySelector('.countdown').hidden&&overlay.querySelector('.countdown').textContent.endsWith(' s'),'small countdown is visible during the page wait');await wait;check(overlay.querySelector('.countdown').hidden,'countdown finishes before the single screenshot');
    w.__ustPage.area={x:99999,y:0,width:10,height:10};const invalid=await command({action:'viewport'});check(invalid.error,'invalid area is rejected before capturing another region');w.__ustPage.area=area.rect;
    await command({id:'cancel',action:'pick-area'});w.dispatchEvent(new w.KeyboardEvent('keydown',{key:'Escape',bubbles:true}));check((await command({action:'poll-pick'})).cancelled,'Escape cancels and preserves the previous selection');
    replacement.remove();check((await command({action:'go',page:4})).error,'missing component stops automation instead of guessing');
    const host=w.document.createElement('section');host.id='shadow';w.document.body.append(host);const root=host.attachShadow({mode:'open'}),inner=w.document.createElement('input');inner.id='number';root.append(inner);wire(inner);
    await command({id:'shadow',action:'pick-field',outline:true});pointer(inner,'pointerdown',20,20);pointer(inner,'click',20,20);await command({action:'go',page:8});check(inner.value==='8','click selection and navigation work inside an open shadow root');
    await command({id:'closing',action:'pick-area'});await command({action:'suspend'});
    check(overlay.host.style.visibility==='hidden'&&!w.__ustPage.picking&&w.document.documentElement.style.cursor==='text','closing hides both markers and releases selection listeners and cursor');
    await command({action:'show'});await new Promise(r=>setTimeout(r,230));
    check(overlay.host.style.visibility==='hidden','capture cleanup and redraw cannot revive a disconnected overlay');
    await command({action:'outline',outline:true});
    check(overlay.host.style.visibility==='visible'&&w.__ustPage.area.width===400,'reconnection restores the saved selection without losing its geometry');
    console.log(`DOM: ${checks} checks passed.`);
  }finally{w.close();}
}
main().catch(e=>{console.error(e);process.exitCode=1;});
