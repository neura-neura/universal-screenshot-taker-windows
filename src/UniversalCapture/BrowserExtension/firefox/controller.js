const t=text=>globalThis.__ustText?globalThis.__ustText(text):text;
const api=globalThis.__ustApi||(typeof browser!=='undefined'?browser:chrome);
let tabId=Number(new URL(location.href).searchParams.get('tab'));let connectedTab=null;
const el=id=>document.getElementById(id),pause=ms=>new Promise(r=>setTimeout(r,ms));let session=crypto.randomUUID();
let controller,fieldFrame=null,lastCapture=0;
const dispatch=command=>globalThis.__ustCommand(command);
const invoke=(command,frames)=>api.scripting.executeScript({target:{tabId,...(frames?{frameIds:frames}:{allFrames:true})},func:dispatch,args:[{...command,language:globalThis.__ustLanguage}]});
const suspend=async targetTab=>{if(!Number.isInteger(targetTab))return;try{await api.scripting.executeScript({target:{tabId:targetTab,allFrames:true},func:dispatch,args:[{action:'suspend'}]});}catch{}};
if(!globalThis.__ustOffscreen)api.tabs.get(tabId).then(tab=>{el('target').textContent=`Pestaña: ${tab.title||tab.url}`;}).catch(e=>{el('status').textContent=e.message;});
el('disconnect').onclick=()=>{controller?.abort();suspend(tabId);el('status').textContent='Desconectado';};
function readerAddress(value,resolveRedirect=false){
  try{let url=new URL(value);if(resolveRedirect&&url.hostname==='elibro.net'&&url.pathname.includes('/login_usuario')&&url.searchParams.has('next')){const next=new URL(url.searchParams.get('next'),url);if(next.origin===url.origin&&next.pathname.startsWith('/es/ereader/'))url=next;}
    url.pathname=url.pathname.replace(/\/+$/,'')||'/';return url.href;
  }catch{return value;}
}
async function restoreSelection(command){
  const matches=(await invoke({...command,action:'restore'})).filter(x=>x.result?.found);
  if(matches.length!==1)return {found:false};fieldFrame=matches[0].frameId;
  const top=(await invoke({...command,action:'restore-area'},[0]))[0]?.result;
  return {...matches[0].result,area:top?.area||null,session};
}
async function handle(command,signal,pending){
  const tab=await api.tabs.get(tabId);if(!tab.active)throw Error(t('Activa la pestaña del lector antes de continuar.'));
  if(command.action==='restore'&&readerAddress(tab.url)!==readerAddress(command.url,true))return {found:false};
  await api.scripting.executeScript({target:{tabId,allFrames:true},files:['i18n.js','page.js']});
  if(command.action==='restore'){
    return restoreSelection(command);
  }
  if(command.action.startsWith('pick-')){
    const frames=command.action==='pick-area'?[0]:undefined;await invoke(command,frames);
    try{
      for(let i=0;i<480;i++){
        if(signal.aborted||!await pending(command.id))return {cancelled:true};
        const results=await invoke({action:'poll-pick'},frames),found=results.find(x=>x.result?.found);
        if(found){if(command.action==='pick-field')fieldFrame=found.frameId;return {...found.result,session,url:tab.url};}
        if(results.some(x=>x.result?.cancelled))return {cancelled:true};await pause(250);
      }return {cancelled:true};
    }finally{await invoke({action:'cancel-pick'},frames).catch(()=>{});}
  }
  if(command.action==='locate'||command.action==='area'){
    const matches=(await invoke(command,command.action==='area'?[0]:undefined)).filter(x=>x.result?.found);
    if(matches.length!==1)throw Error(t('No se encontró un elemento único. Selecciónalo directamente en la página.'));
    if(command.action==='locate')fieldFrame=matches[0].frameId;return {...matches[0].result,session};
  }
  if(command.action==='go'){
    if(command.url&&readerAddress(tab.url)!==readerAddress(command.url,true))throw Error(t('Abre el lector del enlace indicado en la pestaña conectada antes de ir a página.'));
    if(command.selector&&!(await restoreSelection(command)).found)throw Error(t('No se pudo recuperar el campo guardado en este lector. Selecciónalo otra vez.'));
    if(fieldFrame===null)throw Error(t('Selecciona el campo en la pestaña conectada primero.'));
    const result=(await invoke(command,[fieldFrame]))[0]?.result;if(!result||result.error)throw Error(result?.error||t('El frame del campo dejó de estar disponible.'));return {...result,session,url:tab.url};
  }
  if(command.action==='ready'){
    const watching=setInterval(async()=>{try{if(signal.aborted||!await pending(command.id))await invoke({action:'cancel-ready'},[0]);}catch{await invoke({action:'cancel-ready'},[0]).catch(()=>{});}},200);
    try{const result=(await invoke(command,[0]))[0]?.result;if(!result||result.error)throw Error(result?.error||t('No se pudo preparar la página.'));return result;}finally{clearInterval(watching);}
  }
  if(command.action==='outline'){
    const results=await invoke(command);return results.find(x=>x.frameId===0)?.result||{ok:true};
  }
  if(command.action==='capture'){
    await handle({...command,action:'ready'},signal,pending);
    const viewport=(await invoke({...command,action:'viewport'},[0]))[0]?.result;if(!viewport||viewport.error)throw Error(viewport?.error||t('No hay área de captura.'));
    const captureDelay=600-(Date.now()-lastCapture);if(captureDelay>0)await pause(captureDelay);
    await invoke({action:'hide'});
    try{
      const current=await api.tabs.get(tabId);if(!current.active||current.url!==tab.url)throw Error(t('La pestaña cambió durante la captura.'));
      const png=await api.tabs.captureVisibleTab(tab.windowId,{format:'png'});lastCapture=Date.now();
      const after=await api.tabs.get(tabId);if(!after.active||after.url!==tab.url)throw Error(t('La pestaña cambió durante la captura.'));
      const size=(await invoke({...command,action:'viewport'},[0]))[0]?.result;
      if(size?.width!==viewport.width||size?.height!==viewport.height)throw Error(t('El tamaño de la pestaña cambió durante la captura.'));
      const bitmap=await createImageBitmap(await (await fetch(png)).blob()),r=viewport.rect;
      const x=Math.round(r.x*bitmap.width/viewport.width),y=Math.round(r.y*bitmap.height/viewport.height),width=Math.round(r.width*bitmap.width/viewport.width),height=Math.round(r.height*bitmap.height/viewport.height);
      if(x+width>bitmap.width||y+height>bitmap.height)throw Error(t('El tamaño de la pestaña cambió. Selecciona otra vez el área.'));
      const canvas=document.createElement('canvas');canvas.width=width;canvas.height=height;
      canvas.getContext('2d').drawImage(bitmap,x,y,width,height,0,0,width,height);bitmap.close();return {image:canvas.toDataURL('image/png'),rect:r};
    }finally{await invoke({action:'show'}).catch(()=>{});}
  }
  const results=await invoke(command),failed=results.find(x=>x.result?.error);if(failed)throw Error(failed.result.error);return {ok:true};
}
async function discover(signal){
  for(let port=38473;port<=38477;port++){
    const address='http://127.0.0.1:'+port;
    try{const response=await fetch(address+'/connection',{headers:{'X-UST-Discovery':'1'},signal:AbortSignal.any([signal,AbortSignal.timeout(750)])});
      if(!response.ok)continue;const info=await response.json();globalThis.__ustLanguage=info.language||'en';if(info.app==='UniversalCapture'&&/^[a-f0-9]{48}$/i.test(info.token))return {address,token:info.token};
    }catch(e){if(signal.aborted)throw e;}
  }throw Error(t('Abre Universal Screenshot Taker. La conexión se realizará automáticamente.'));
}
async function connect(){
  controller?.abort();controller=new AbortController();const signal=controller.signal,targetTab=tabId;
  while(!signal.aborted){try{
  const {address,token}=await discover(signal);
  const headers={Authorization:`Bearer ${token}`,'Content-Type':'application/json','X-UST-Session':session};
  const pending=async id=>(await (await fetch(address+'/pending?id='+encodeURIComponent(id),{headers,signal})).json()).pending;
  try{
    while(!signal.aborted){
      const response=await fetch(address+'/command',{headers,signal:AbortSignal.any([signal,AbortSignal.timeout(5000)])});if(!response.ok)throw Error(t('Clave no válida o aplicación no disponible.'));
      el('status').textContent='Conectado. Puedes cerrar este popup y volver al lector.';const command=await response.json();
      if(command.id){globalThis.__ustLanguage=command.language||globalThis.__ustLanguage;let result;try{result=await handle(command,signal,pending);}catch(e){result={error:e.message};}
        await fetch(address+'/result',{method:'POST',headers,body:JSON.stringify({id:command.id,...result,session}),signal});}
    }
  }catch(e){await suspend(targetTab);if(!signal.aborted)el('status').textContent=e.message;}
  }catch(e){await suspend(targetTab);if(!signal.aborted)el('status').textContent=e.message;}
  if(!signal.aborted)await pause(1000);
  }
}
el('connect').onclick=connect;
globalThis.__ustControllerMessage=async message=>{
  if(message.action==='start'&&(tabId!==message.tabId||!controller||controller.signal.aborted)){
    controller?.abort();await suspend(tabId);if(tabId!==message.tabId)session=crypto.randomUUID();tabId=message.tabId;connectedTab=await api.tabs.get(tabId);fieldFrame=null;connect();
  }
  if(message.action==='disconnect'){controller?.abort();await suspend(tabId);el('status').textContent='Desconectado';}
  return {status:el('status').textContent,title:connectedTab?.title,language:globalThis.__ustLanguage};
};
if(globalThis.__ustOffscreen){
  chrome.runtime.onMessage.addListener((message,sender,respond)=>{
    if(message.target!=='controller')return;
    __ustControllerMessage(message).then(respond,error=>respond({status:error.message}));return true;
  });
}else if(!globalThis.__ustTest)connect();
