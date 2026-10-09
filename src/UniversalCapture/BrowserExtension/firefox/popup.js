const api=typeof browser!=='undefined'?browser:chrome;
let tab;
async function send(action){const response=await api.runtime.sendMessage({target:'background',action,tabId:tab.id});if(response?.error)throw Error(response.error);return response?.value;}
function show(info){
  globalThis.__ustLanguage=info?.language||navigator.language;
  document.documentElement.lang=globalThis.__ustLanguage;
  document.querySelectorAll('[data-text]').forEach(node=>node.textContent=__ustText(node.dataset.text));
  document.getElementById('target').textContent=__ustText('Pestaña: {0}').replace('{0}',info?.title||tab.title||tab.url);
  document.getElementById('status').textContent=__ustText(info?.status||'Buscando la aplicación…');
}
async function start(){try{show(await send('start'));}catch(error){document.getElementById('status').textContent=error.message;}}
document.getElementById('connect').onclick=start;
document.getElementById('disconnect').onclick=async()=>{try{show(await send('disconnect'));}catch(error){document.getElementById('status').textContent=error.message;}};
(async()=>{[tab]=await api.tabs.query({active:true,currentWindow:true});await start();})();
setInterval(async()=>{if(tab)try{show(await send('status'));}catch{}},500);
