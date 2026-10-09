globalThis.__ustOffscreen=true;
globalThis.__ustApi=typeof browser!=='undefined'&&browser.tabs?browser:{
  tabs:{get:tabId=>request({action:'get-tab',tabId}),captureVisibleTab:windowId=>request({action:'capture',windowId})},
  scripting:{executeScript:options=>request({action:options.files?'inject':'invoke',options:{target:options.target,args:options.args}})}
};
async function request(message){
  const response=await chrome.runtime.sendMessage({target:'background',...message});
  if(response?.error)throw Error(response.error);return response?.value;
}
