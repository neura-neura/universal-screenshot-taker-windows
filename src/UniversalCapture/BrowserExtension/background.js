const api=typeof browser!=='undefined'?browser:chrome;
let creating;
async function ensureController(){
  if(!api.offscreen)return;
  if(await api.offscreen.hasDocument())return;
  if(!creating)creating=api.offscreen.createDocument({url:'offscreen.html',reasons:['BLOBS'],justification:'Decode screenshot blobs and crop them with a DOM canvas while the extension popup is closed.'}).finally(()=>{creating=null;});
  await creating;
}
async function route(message,sender){
  if(message.target!=='background')return;
  if(message.action==='start'){
    await ensureController();
    if(globalThis.__ustControllerMessage)return await __ustControllerMessage(message);
    return await api.runtime.sendMessage({target:'controller',action:'start',tabId:message.tabId});
  }
  if(message.action==='status'||message.action==='disconnect'){
    await ensureController();if(globalThis.__ustControllerMessage)return await __ustControllerMessage(message);return await api.runtime.sendMessage({target:'controller',action:message.action});
  }
  if(sender.url!==api.runtime.getURL('offscreen.html'))throw Error('Invalid controller');
  if(message.action==='get-tab')return await api.tabs.get(message.tabId);
  if(message.action==='capture')return await api.tabs.captureVisibleTab(message.windowId,{format:'png'});
  if(message.action==='inject')return await api.scripting.executeScript({target:message.options.target,files:['i18n.js','page.js']});
  if(message.action==='invoke')return await api.scripting.executeScript({target:message.options.target,func:command=>globalThis.__ustCommand(command),args:message.options.args});
  throw Error('Unknown extension request');
}
api.runtime.onMessage.addListener((message,sender,respond)=>{
  if(message.target!=='background')return;
  route(message,sender).then(value=>respond({value}),error=>respond({error:error.message}));return true;
});
