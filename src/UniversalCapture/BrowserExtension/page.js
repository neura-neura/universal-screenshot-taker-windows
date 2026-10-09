// DOM selection in an isolated extension world; no system pointer injection.
(() => {
  const t=text=>globalThis.__ustText?globalThis.__ustText(text):text;
  if(globalThis.__ustPage)return;
  document.querySelectorAll('[data-ust-overlay]').forEach(node=>node.remove());
  const s=globalThis.__ustPage={field:null,route:null,fingerprint:null,area:null,picking:null,result:null,shown:true,hidden:false,suspended:false};
  const host=document.createElement('div');host.style.cssText='all:initial!important;position:fixed!important;inset:0!important;pointer-events:none!important;z-index:2147483647!important';
  host.dataset.ustOverlay='';
  const shadow=host.attachShadow({mode:'closed'});
  shadow.innerHTML='<style>.box{position:fixed;box-sizing:border-box;border:2px dashed;border-radius:3px;pointer-events:none}.field{border-color:#12a75b;background:rgba(18,167,91,.15)}.area{border-color:#0078d4;background:rgba(0,120,212,.12)}.help{position:fixed;top:12px;left:12px;padding:10px 16px;border-radius:6px;background:#202020;color:white;font:14px Segoe UI,sans-serif}</style><div class="box area" hidden></div><div class="box field" hidden></div><div class="help" hidden></div>';
  const fieldBox=shadow.querySelector('.field'),areaBox=shadow.querySelector('.area'),help=shadow.querySelector('.help');
  const style=document.createElement('style');style.textContent='.handle{position:absolute;pointer-events:auto;touch-action:none;background:transparent;border:0;padding:0}.handle:focus-visible{outline:2px solid white;background:#0078d4}.corner{width:12px;height:12px;background:white;border:2px solid #0078d4;border-radius:3px}.countdown{position:absolute;right:8px;top:8px;background:#202020e6;color:white;border-radius:4px;padding:3px 6px;font:12px Segoe UI,sans-serif;pointer-events:none}';shadow.append(style);
  const countdown=document.createElement('span');countdown.className='countdown';countdown.hidden=true;countdown.setAttribute('role','timer');areaBox.append(countdown);
  let resizing=null;
  for(const edge of ['n','s','e','w','nw','ne','sw','se']){
    const h=document.createElement('button');h.className='handle'+(edge.length===2?' corner':'');h.dataset.edge=edge;h.setAttribute('aria-label',t('Redimensionar área ')+edge);h.title=t('Arrastra para ajustar; flechas para ajuste preciso');
    h.style.cssText=edge.length===2?`${edge.includes('n')?'top':'bottom'}:-6px;${edge.includes('w')?'left':'right'}:-6px;cursor:${edge}-resize`:
      edge==='n'||edge==='s'?`${edge==='n'?'top':'bottom'}:-5px;left:8px;right:8px;height:10px;cursor:ns-resize`:`${edge==='w'?'left':'right'}:-5px;top:8px;bottom:8px;width:10px;cursor:ew-resize`;
    h.addEventListener('pointerdown',e=>{if(s.picking||s.hidden||!s.shown||!s.area)return;e.preventDefault();e.stopPropagation();resizing={edge,original:{...s.area}};h.setPointerCapture?.(e.pointerId);});
    h.addEventListener('keydown',e=>{const moves={ArrowLeft:[-1,0],ArrowRight:[1,0],ArrowUp:[0,-1],ArrowDown:[0,1]};if(!moves[e.key]||!s.area)return;e.preventDefault();const step=e.shiftKey?10:1,[dx,dy]=moves[e.key],r=s.area;adjust(edge,{...r},(edge.includes('w')?r.x:r.x+r.width)+dx*step,(edge.includes('n')?r.y:r.y+r.height)+dy*step);});
    areaBox.append(h);
  }
  function adjust(edge,r,x,y){let left=r.x,top=r.y,right=r.x+r.width,bottom=r.y+r.height;
    if(edge.includes('w'))left=Math.max(0,Math.min(right-3,x));if(edge.includes('e'))right=Math.min(innerWidth,Math.max(left+3,x));
    if(edge.includes('n'))top=Math.max(0,Math.min(bottom-3,y));if(edge.includes('s'))bottom=Math.min(innerHeight,Math.max(top+3,y));
    s.area={x:left,y:top,width:right-left,height:bottom-top};draw();
  }
  addEventListener('pointermove',e=>{if(!resizing)return;e.preventDefault();adjust(resizing.edge,resizing.original,e.clientX,e.clientY);},true);
  addEventListener('pointerup',e=>{if(!resizing)return;e.preventDefault();resizing=null;},true);
  addEventListener('pointercancel',()=>{resizing=null;},true);
  addEventListener('keydown',e=>{if(e.key==='Escape'&&resizing){s.area=resizing.original;resizing=null;draw();}},true);
  document.documentElement.append(host);
  const rect=node=>{const r=node.getBoundingClientRect();return {x:r.x,y:r.y,width:r.width,height:r.height};};
  function visible(node){if(!node?.isConnected)return false;const r=rect(node);return r.width>1&&r.height>1&&r.x<innerWidth&&r.y<innerHeight&&r.x+r.width>0&&r.y+r.height>0&&getComputedStyle(node).visibility!=='hidden';}
  const editable=node=>node?.matches?.('input,textarea,[contenteditable]:not([contenteditable="false"])')&&(node.localName!=='input'||['text','number','tel','search','url','email'].includes(node.type))&&!node.disabled&&!node.readOnly&&visible(node);
  const stableClasses=node=>Array.from(node.classList).filter(c=>!/^ng-(valid|invalid|touched|untouched|dirty|pristine|pending)$/.test(c)).sort();
  const fingerprint=node=>[node.localName,node.getAttribute('type')||'',node.id,node.getAttribute('name')||'',node.getAttribute('aria-label')||'',stableClasses(node).join(' ')].join('|');
  function route(node){
    const parts=[];
    while(node?.nodeType===1){const root=node.getRootNode(),path=[];
      for(let n=node;n?.nodeType===1;n=n.parentElement){
        if(n.id&&root.querySelectorAll('#'+CSS.escape(n.id)).length===1){path.unshift('#'+CSS.escape(n.id));break;}
        const named=n.localName+stableClasses(n).map(c=>'.'+CSS.escape(c)).join('');
        if(root.querySelectorAll(named).length===1){path.unshift(named);break;}
        let index=1;for(let prev=n.previousElementSibling;prev;prev=prev.previousElementSibling)if(prev.localName===n.localName)index++;
        path.unshift(n.localName+':nth-of-type('+index+')');
      }
      parts.unshift(path.join(' > '));node=root.host;
    }return parts;
  }
  function resolve(){
    if(editable(s.field))return s.field;
    let root=document,node=null;for(const part of s.route||[]){node=root?.querySelector(part);root=node?.shadowRoot;}
    if(!editable(node)||fingerprint(node)!==s.fingerprint)throw Error(t('El campo seleccionado cambió o desapareció. Selecciónalo otra vez.'));return s.field=node;
  }
  function box(el,r,on){
    el.hidden=!(on&&r&&r.width>1&&r.height>1);if(el.hidden)return;
    const style=`left:${r.x}px;top:${r.y}px;width:${r.width}px;height:${r.height}px`;
    if(el.dataset.rect!==style){el.style.cssText=style;el.dataset.rect=style;}
  }
  function draw(){
    if(!host.isConnected)document.documentElement.append(host);
    host.style.setProperty('visibility',s.hidden||s.suspended?'hidden':'visible','important');
    box(fieldBox,s.hover?rect(s.hover):visible(s.field)?rect(s.field):null,s.shown||s.picking);
    box(areaBox,s.preview||s.area,s.shown||s.picking);
  }
  s.timer=setInterval(()=>{if(s.field||s.area||s.picking)draw();},200);addEventListener('resize',draw);addEventListener('scroll',draw,true);
  function stop(){
    const wasPicking=!!s.picking;
    if(s.listeners)for(const [type,handler] of s.listeners)removeEventListener(type,handler,true);
    s.listeners=null;s.picking=null;s.hover=null;s.preview=null;help.hidden=true;host.style.setProperty('pointer-events','none','important');if(wasPicking)document.documentElement.style.cursor=s.cursor||'';draw();
  }
  function finish(result){s.result=result;stop();if(result.found)addEventListener('click',consume,{capture:true,once:true});}
  const consume=e=>{e.preventDefault();e.stopImmediatePropagation();};
  function begin(command){
    stop();s.result=null;s.picking=command.id;s.cursor=document.documentElement.style.cursor;document.documentElement.style.cursor='crosshair';
    // Intercept the top-level viewport for area drags, including iframe surfaces.
    host.style.setProperty('pointer-events',command.action==='pick-area'?'auto':'none','important');
    help.hidden=false;help.textContent=command.action==='pick-field'?t('Pulsa el campo de página resaltado. Esc cancela.'):command.twoCorners?t('Pulsa las dos esquinas del área. Esc cancela.'):t('Arrastra el área dentro del lector. Esc cancela.');
    let anchor=null;const at=e=>({x:Math.max(0,Math.min(innerWidth,e.clientX)),y:Math.max(0,Math.min(innerHeight,e.clientY))});
    const region=(a,b)=>({x:Math.min(a.x,b.x),y:Math.min(a.y,b.y),width:Math.abs(a.x-b.x),height:Math.abs(a.y-b.y)});
    const completeArea=e=>{const r=region(anchor,at(e));if(r.width<3||r.height<3){anchor=null;s.preview=null;draw();return;}s.area=r;s.shown=command.outline;finish({found:true,rect:r});};
    const move=e=>{if(command.action==='pick-field')s.hover=e.composedPath().find(editable)||null;else if(anchor)s.preview=region(anchor,at(e));draw();};
    const down=e=>{
      if(e.button!==0)return;consume(e);
      if(command.action==='pick-field'){
        const node=e.composedPath().find(editable);if(!node)return;
        s.field=node;s.route=route(node);s.fingerprint=fingerprint(node);s.shown=command.outline;
        finish({found:true,rect:rect(node),label:node.getAttribute('aria-label')||node.getAttribute('name')||t('Campo de página'),selector:s.route.join(' >>> ')});
      }else if(!anchor)anchor=at(e);else if(command.twoCorners)completeArea(e);
    };
    const up=e=>{consume(e);if(command.action==='pick-area'&&!command.twoCorners&&anchor)completeArea(e);};
    const key=e=>{if(e.key==='Escape'){consume(e);finish({cancelled:true});}};
    s.listeners=[['pointermove',move],['pointerdown',down],['pointerup',up],['click',consume],['keydown',key]];
    for(const [type,handler] of s.listeners)addEventListener(type,handler,true);
    setTimeout(()=>{if(s.picking===command.id)finish({cancelled:true});},120000);return {ok:true};
  }
  function locate(command){
    let selector=command.selector;if(command.mode==='elibro')selector='.page-number';
    if(command.mode==='html'){
      const template=document.createElement('template');template.innerHTML=selector.trim();const node=template.content.firstElementChild;if(!node)throw Error(t('Fragmento HTML vacío.'));
      selector=node.id?'#'+CSS.escape(node.id):node.localName+Array.from(node.attributes).filter(a=>['name','aria-label','placeholder','type'].includes(a.name)).map(a=>'['+a.name+'='+JSON.stringify(a.value)+']').join('');
    }
    function find(root){
      const nodes=command.mode==='active'?[root.activeElement]:Array.from(root.querySelectorAll(selector));
      const found=nodes.filter(n=>command.action==='area'?visible(n):editable(n));
      for(const n of root.querySelectorAll('*'))if(n.shadowRoot)found.push(...find(n.shadowRoot));return found;
    }
    const matches=find(document);if(matches.length>1)throw Error(t('El selector coincide con varios elementos. Hazlo más preciso.'));
    const node=matches[0];if(!node)return {found:false};
    if(command.action==='area'){s.area=rect(node);draw();return {found:true,rect:s.area};}
    s.field=node;s.route=route(node);s.fingerprint=fingerprint(node);draw();return {found:true,rect:rect(node),selector:s.route.join(' >>> ')};
  }
  globalThis.__ustCommand=async command=>{
    globalThis.__ustLanguage=command.language||globalThis.__ustLanguage;
    for(const h of shadow.querySelectorAll('.handle')){h.setAttribute('aria-label',t('Redimensionar área ')+h.dataset.edge);h.title=t('Arrastra para ajustar; flechas para ajuste preciso');}
    try{
      if(command.action==='suspend'){s.suspended=true;s.readyCancelled=true;resizing=null;stop();return {ok:true};}
      if(['outline','restore','restore-area','locate','area','pick-field','pick-area'].includes(command.action))s.suspended=false;
      if(command.action.startsWith('pick-'))return begin(command);
      if(command.action==='poll-pick')return s.result||{pending:!!s.picking};
      if(command.action==='cancel-pick'){stop();return {ok:true};}
      if(command.action==='outline'){s.shown=command.outline;draw();return {ok:true,rect:s.area};}
      if(command.action==='restore'){
        if(!editable(s.field))locate({...command,selector:command.selector.replace(/\.ng-(valid|invalid|touched|untouched|dirty|pristine|pending)(?=[\s.>:#\[]|$)/g,''),action:'locate',mode:'css'});
        const node=resolve();draw();return {found:true,rect:rect(node)};
      }
      if(command.action==='restore-area'){
        s.area=s.area||command.area;
        if(s.area&&(s.area.x<0||s.area.y<0||s.area.width<3||s.area.height<3||s.area.x+s.area.width>innerWidth||s.area.y+s.area.height>innerHeight))s.area=null;
        draw();return {area:s.area};
      }
      if(command.action==='cancel-ready'){s.readyCancelled=true;return {ok:true};}
      if(command.action==='ready'){
        s.readyCancelled=false;const start=performance.now(),minimum=Math.max(200,Number(command.waitSeconds||.2)*1000);let lastChange=start;
        const observer=new MutationObserver(records=>{if(records.some(r=>r.target!==host&&!host.contains(r.target)))lastChange=performance.now();});
        if(command.stable)observer.observe(document.documentElement,{subtree:true,childList:true,attributes:true,characterData:true});
        countdown.hidden=false;
        try{while(true){if(s.readyCancelled)throw Error(t('Captura cancelada.'));const now=performance.now(),remaining=Math.max(0,minimum-(now-start));countdown.textContent=remaining>0?(remaining/1000).toFixed(1)+' s':'Preparando…';
          const loading=command.stable&&Array.from(document.images).some(img=>visible(img)&&!img.complete);
          if(remaining===0&&(!command.stable||(now-lastChange>=150&&!loading)))break;
          if(now-start>minimum+10000)throw Error(t('La página sigue cambiando. Aumenta la espera o desactiva Esperar imagen estable.'));
          await new Promise(r=>setTimeout(r,50));
        }return {ok:true,rect:s.area};}finally{observer.disconnect();countdown.hidden=true;}
      }
      if(command.action==='hide'){s.hidden=true;draw();await new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r)));return {ok:true};}
      if(command.action==='show'){s.hidden=false;draw();return {ok:true};}
      if(command.action==='viewport'){
        const r=s.area||command.area;if(!r||!Object.values(r).every(Number.isFinite)||r.x<0||r.y<0||r.width<3||r.height<3||r.x+r.width>innerWidth||r.y+r.height>innerHeight)throw Error(t('El área quedó fuera de la página visible. Selecciónala otra vez.'));
        s.area=r;return {rect:r,width:innerWidth,height:innerHeight};
      }
      if(command.action==='locate'||command.action==='area')return locate(command);
      if(command.action==='go'){
        const node=resolve();node.focus({preventScroll:true});
        if(node.isContentEditable)node.textContent=String(command.page);
        else Object.getOwnPropertyDescriptor(node.localName==='textarea'?HTMLTextAreaElement.prototype:HTMLInputElement.prototype,'value').set.call(node,String(command.page));
        node.dispatchEvent(new Event('input',{bubbles:true}));node.dispatchEvent(new Event('change',{bubbles:true}));
        for(const type of ['keydown','keypress','keyup'])node.dispatchEvent(new KeyboardEvent(type,{key:'Enter',code:'Enter',keyCode:13,which:13,bubbles:true,composed:true}));
        return {ok:true,value:node.isContentEditable?node.textContent:node.value};
      }
      throw Error(t('Comando no reconocido.'));
    }catch(e){return {error:e.message};}
  };
})();
