const $ = s => document.querySelector(s);
const $$ = s => [...document.querySelectorAll(s)];
const icons = () => lucide.createIcons();
const credentialTemplate = $('#credential-card').outerHTML;
function showCredential(){if(!$('#credential-card')){$('#chat-feed').insertAdjacentHTML('beforeend',credentialTemplate);icons()}$('#credential-card').scrollIntoView({behavior:'smooth',block:'center'});$('#demo-token')?.focus()}
const companies = [
 ['FA','Fieldwork Architects','fieldwork.example','Residential',98,'Shortlisted'],
 ['S','Studio North','studionorth.example','Commercial',96,'Shortlisted'],
 ['A&P','Arden & Partners','arden.example','Mixed-use',94,'Shortlisted'],
 ['FL','Form & Line','formandline.example','Residential',92,'To review'],
 ['OH','Open House Studio','openhouse.example','Interiors',91,'To review'],
 ['MA','Morrow Architects','morrow.example','Residential',89,'To review'],
 ['W','Westward Studio','westward.example','Commercial',88,'To review'],
 ['CB','Common Build','commonbuild.example','Mixed-use',86,'To review'],
 ['AA','Atelier Ash','atelierash.example','Interiors',85,'To review'],
 ['R','Room Practice','room.example','Residential',84,'To review'],
 ['SS','South Studio','southstudio.example','Commercial',82,'To review'],
 ['G','Ground Architecture','ground.example','Mixed-use',81,'To review']
];
let shortlisted = false, allRows = true, z = 5, connected = false, automated = false;
const windows = {leads:'open',notes:'open',automation:'closed'};
const variants = ['obsidian','pearl','midnight'];
const names = ['01 · Obsidian desktop','02 · Pearl atelier','03 · Midnight focus'];
const state = {variant:'obsidian',chatSide:'left',activeWindow:'notes'};
// Product applications compose existing Flutter neurons. This registry is a
// proposed shell concept, not a live backend app registry.
const appRegistry = [
 {id:'tables',name:'Tables',icon:'table-2',color:'mint',window:'leads',neurons:['ui.table']},
 {id:'notes',name:'Notes',icon:'file-text',color:'peach',window:'notes',neurons:['ui.text']},
 {id:'automations',name:'Automations',icon:'workflow',color:'violet',window:'automation',neurons:['ui.card','ui.text','ui.button']},
 {id:'files',name:'Files',icon:'folder-open',color:'sand',dialog:'files',neurons:['ui.tree']}
];
const windowMeta={leads:{title:'Architecture shortlist',icon:'table-2',color:'mint'},notes:{title:'Research notes',icon:'file-text',color:'peach'},automation:{title:'Weekly research',icon:'workflow',color:'violet'}};
let desktopRestore=[];
const preferredWindowModes = new Map();
function minimizeOthers(id){Object.keys(windows).filter(key=>key!==id&&windows[key]==='open').forEach(key=>hideWindow(key,'minimized'))}
function setWindowMode(id,maximized,remember=true){
 const win=$('#'+id);win.classList.toggle('maximized',maximized);
 if(remember)preferredWindowModes.set(id,maximized?'maximized':'floating');
 const button=win.querySelector('[data-maximize]');
 if(button){button.setAttribute('aria-label',(maximized?'Restore size of ':'Maximize ')+windowMeta[id].title);button.setAttribute('title',maximized?'Restore floating size':'Fill workspace');button.innerHTML='<i data-lucide="'+(maximized?'minimize-2':'maximize-2')+'"></i>'}
 if(maximized)minimizeOthers(id);icons();
}
function launchApplication(id){setWindowMode(id,preferredWindowModes.get(id)!=='floating',false);openWindow(id)}
function toggleAssistant(){document.body.classList.toggle('assistant-hidden');$('#toggle-assistant').setAttribute('aria-pressed',String(!document.body.classList.contains('assistant-hidden')))}
function renderTable(){
 let rows = companies.filter(c => !shortlisted || c[5] === 'Shortlisted');
 if(!allRows) rows = rows.slice(0,5);
 $('#company-rows').innerHTML = rows.map(c => `<tr><td><div class="company"><span class="company-logo">${c[0]}</span><div><b>${c[1]}</b><small>${c[2]}</small></div></div></td><td>${c[3]}</td><td><span class="match">${c[4]}%</span></td><td><span class="tag">${c[5]}</span></td></tr>`).join('');
 $('#row-count').textContent=rows.length;
 $('#show-all').innerHTML=`${allRows?'Show fewer':'View all companies'} <i data-lucide="arrow-right"></i>`;
 $('#filter span').textContent=shortlisted?'Shortlisted':'All matches'; icons();
}
let toastTimer;
function toast(message){$('#toast').textContent=message;$('#toast').classList.add('visible');clearTimeout(toastTimer);toastTimer=setTimeout(()=>$('#toast').classList.remove('visible'),3200)}
function focusWindow(id){const el=$('#'+id);el.style.zIndex=++z;state.activeWindow=id;updateDock()}
function openWindow(id){if($('#'+id).classList.contains('maximized'))minimizeOthers(id);else Object.keys(windows).filter(key=>key!==id&&windows[key]==='open'&&$('#'+key).classList.contains('maximized')).forEach(key=>hideWindow(key,'minimized'));$('#'+id).classList.remove('hidden');windows[id]='open';focusWindow(id);updateDock();}
function hideWindow(id,status){$('#'+id).classList.add('hidden');windows[id]=status;if(state.activeWindow===id){state.activeWindow=Object.keys(windows).filter(key=>windows[key]==='open').sort((a,b)=>(Number($('#'+b).style.zIndex)||3)-(Number($('#'+a).style.zIndex)||2))[0]||null}updateDock()}
function updateDock(){
 const live=Object.keys(windows).filter(id=>windows[id]!=='closed');
 $('#running-windows').innerHTML=live.map(id=>{const m=windowMeta[id],min=windows[id]==='minimized';return `<button class="task-button ${state.activeWindow===id&&!min?'active':''} ${min?'minimized':''}" data-task="${id}" aria-label="${min?'Restore':'Switch to'} ${m.title}" aria-pressed="${state.activeWindow===id&&!min}" title="${m.title}" data-tooltip="${m.title}${min?' · Minimized':''}"><span class="mini-icon ${m.color}"><i data-lucide="${m.icon}"></i></span><span>${m.title}</span>${min?'<span class="task-symbol">−</span>':''}</button>`}).join('');
 const visible=Object.values(windows).filter(v=>v==='open').length,minimized=Object.values(windows).filter(v=>v==='minimized').length;
 if($('#open-window-count'))$('#open-window-count').textContent=`${visible} window${visible===1?'':'s'}${minimized?' · '+minimized+' minimized':''}`;
 $$('.window').forEach(w=>w.classList.toggle('is-active',w.id===state.activeWindow&&windows[w.id]==='open'));
 $('.empty-desktop').style.visibility=visible?'hidden':'visible';icons();
}
function arrange(){$$('.window').forEach(el=>{el.removeAttribute('style');setWindowMode(el.id,false,false)});z=5;state.activeWindow=windows.notes==='open'?'notes':Object.keys(windows).find(id=>windows[id]==='open')||null;updateDock()}
function setVariant(v){state.variant=variants.includes(v)?v:'obsidian';document.body.dataset.variant=state.variant;const url=new URL(location);url.searchParams.set('variant',state.variant);history.replaceState(null,'',url);arrange();}
function flipChat(side){state.chatSide=side|| (state.chatSide==='left'?'right':'left');document.body.classList.toggle('chat-right',state.chatSide==='right');}
const icon = name => `<i data-lucide="${name}"></i>`;
const row = (title,desc,attr,ico='arrow-up-right') => `<button class="menu-row" ${attr}>${icon(ico)}<span><b>${title}</b><small>${desc}</small></span>${icon('chevron-right')}</button>`;
const modal=$('#modal');
function dialog(type){
 modal.className=type==='launcher'?'app-launcher':type==='activity'?'background-popup':['spaces','settings','profile','compute','search','directions','connections'].includes(type)?'top-popover'+(type==='spaces'?' workspace-popover':'')+(type==='search'?' search-popover':'')+(['profile','settings','compute','directions','connections'].includes(type)?' account-popover':''):'';
 $('.applications-button').setAttribute('aria-expanded',String(type==='launcher'));
 const content={
  settings:['Settings',`<div class="setting-row"><div><b>Dock position</b><small>Move all three islands together.</small></div><div class="segmented"><button data-dock-position="bottom" class="${document.body.classList.contains('dock-top')?'':'active'}">Bottom</button><button data-dock-position="top" class="${document.body.classList.contains('dock-top')?'active':''}">Top</button></div></div><div class="setting-row"><div><b>Assistant position</b><small>Keep the conversation on your preferred side.</small></div><div class="segmented"><button data-side="left" class="${state.chatSide==='left'?'active':''}">Left</button><button data-side="right" class="${state.chatSide==='right'?'active':''}">Right</button></div></div><div class="setting-row"><div><b>Reduce motion</b><small>Quiet transitions and still windows.</small></div><input id="motion" type="checkbox" aria-label="Reduce motion" style="width:18px" ${document.body.classList.contains('reduce-motion')?'checked':''}></div>${row('Appearance','Explore three directions for your workspace.','data-dialog="directions"','palette')}${row('Compute & usage','Balance, recent usage, and task estimates.','data-dialog="compute"','zap')}${row('Background activity','Running tasks and scheduled automations.','data-dialog="activity"','activity')}`],
  directions:['Appearance',`<p>Layout and color options for the operating-system shell.</p>${row('01 · Obsidian desktop — recommended','Dark green surfaces with floating controls and an icon dock.','data-variant="obsidian"','panels-top-left')}${row('02 · Pearl atelier','An airy, tiled studio. Work and notes sit alongside each other, with less overlap and a softer reading experience.','data-variant="pearl"','columns-2')}${row('03 · Midnight focus','An expansive primary canvas with a compact companion note. Best for going deep on one task.','data-variant="midnight"','app-window')}`],
  compute:['Compute',`<p>Your workspace runs on Compute.</p><div class="balance">2,480 <small>Compute available</small></div><div class="usage-row"><b>This month</b><span>520 Compute used</span></div><div class="usage-row"><b>Architecture research</b><span>12 Compute</span></div><div class="usage-row"><b>Shortlist generation</b><span>3 Compute</span></div><div class="usage-row"><b>Weekly research estimate</b><span>8–15 Compute / run</span></div><div class="demo-note">Illustrative balance and usage. Production should show an estimate before a task, a spending limit, and the actual metered cost afterward.</div><button class="primary full" id="topup-demo">Preview adding Compute ${icon('plus')}</button>`],
  profile:['Profile',`<div class="balance" style="font-size:32px">Viktor <small>Personal workspace</small></div>${row('Workspace preferences','Assistant position, appearance, and motion.','data-dialog="settings"','settings-2')}${row('Compute balance','2,480 Compute available in this concept.','data-dialog="compute"','zap')}${row('Background activity','Running tasks and schedules.','data-dialog="activity"','activity')}<p>Profile details are sample content for this design study.</p>`],
  spaces:['Your workspaces',`<p>Open directly into your last workspace. Switch context here whenever you need to.</p>${row('Personal workspace','Research, create, and explore.','data-space="Personal workspace"','orbit')}${row('Salesforce management','A separate space for your CRM work.','data-space="Salesforce management"','cloud')}${row('New workspace','Start with a clean desktop.','id="new-space"','plus')}`],
  files:['Files',`<p>Artifacts live here even after their windows close.</p>${row('Architecture shortlist','Interactive table · 12 sample companies','data-open="leads"','table-2')}${row('Research notes','Summary · created by IntoChat','data-open="notes"','file-text')}${row('Weekly research','Automation · '+(automated?'enabled in demo':'draft'),'data-open="automation"','workflow')}<p>Files remain available when you close their windows.</p>`],
  launcher:['Applications',`<label class="launcher-search">${icon('search')}<input id="app-search" aria-label="Search applications" placeholder="Search applications…" autocomplete="off"><kbd>↵</kbd></label><div class="launcher-section"><span>APPLICATIONS</span><span>4 available</span></div><div class="launcher-grid">${appRegistry.map(app=>`<button class="launcher-app" ${app.window?'data-launch="'+app.window+'"':'data-dialog="'+app.dialog+'"'} data-app-name="${app.name.toLowerCase()}"><span class="app-icon ${app.color}">${icon(app.icon)}</span><span>${app.name}</span></button>`).join('')}</div><p id="app-empty" class="muted hidden">No applications found.</p><div class="launcher-recents"><div class="launcher-section"><span>RECENT FILES</span></div>${row('Architecture shortlist','Tables · 12 companies','data-open="leads"','table-2')}${row('Research notes','Notes · today','data-open="notes"','file-text')}</div><div class="launcher-bottom"><span>Workspace · Prototype</span><button data-dialog="connections">${icon('plug')} Connections</button><button class="icon-button" data-dialog="settings" aria-label="Workspace settings">${icon('settings-2')}</button></div>`],
  connections:['Your tools, working together',`<p>Connect a service when a task needs it, right inside the conversation.</p>${row('Salesforce',connected?'Connected in this demo':'Not connected · try the inline connection card','id="crm-connect"','cloud')}<div class="demo-note">Prototype connection only. No accounts or external services are accessed.</div>`],
  activity:['Background activity',`<div class="activity-empty">${icon('circle-check')}<p>No tasks running</p><p>${automated?'1 scheduled demo automation':'Work in progress will appear here.'}</p></div><div class="activity-history"><div class="launcher-section">RECENT</div>${row('Architecture research completed','24 sources · 12 Compute · sample activity','data-open="leads"','check')}${automated?row('Weekly research scheduled','Monday, 09:00 · demo only','data-open="automation"','calendar-clock'):''}</div>`],
  attach:['Attach context',`${row('Architecture shortlist','Attach the company table to your next message.','id="attach-shortlist"','table-2')}${row('Research notes','Open the source notes in your desktop.','data-open="notes"','file-text')}`],
  agents:['Assistant capabilities',`<p>The assistant uses the capability that best fits your task.</p>${row('Auto','Choose the right tool for the request.','data-agent="Auto"','sparkles')}${row('Researcher','Explore sources and synthesize findings.','data-agent="Researcher"','search')}${row('Builder','Create useful artifacts and workflows.','data-agent="Builder"','workflow')}`],
  conversations:['Your conversations',`${row('Architecture research','Today · connected to your shortlist','id="resume-chat"','message-square')}${row('Start a new conversation','Your desktop artifacts stay available.','id="modal-new-chat"','square-pen')}`],
  sources:['Research, with a trail',`<p>This mockup uses fictional studios and example domains. In the real app, every finding should link back to a source.</p><div class="sources">24 studios reviewed<br>12 matched your criteria<br>Independent teams with 10–50 people<br>Residential, commercial, and mixed-use work</div>${row('Back to the shortlist','Review the findings in your table.','data-open="leads"','table-2')}`],
  neuron:['How this connects to your neurons',`<p>A neuron owns the content and actions. The desktop owns placement and focus.</p><div class="usage-row"><b>Shortlist</b><span>TableNeuron → ui.table</span></div><div class="usage-row"><b>Inline card</b><span>CardNeuron + UiChildRef</span></div><div class="usage-row"><b>Window lifecycle</b><span>WorkspaceNeuron → Open / Close</span></div><div class="usage-row"><b>Minimize & placement</b><span>WorkspacePresentation</span></div><div class="demo-note">The mockup is not connected to the backend. A dedicated credential channel and server-metered Compute service are proposed additions. See DESIGN.md beside this file.</div>`],
  search:['Find your next move',`<input class="search-input" id="search-query" aria-label="Search tools and files" placeholder="Search your tools and files…" autofocus><div id="search-results">${row('Architecture shortlist','Table · 12 companies','data-open="leads"','table-2')}${row('Research notes','Document · research summary','data-open="notes"','file-text')}${row('Weekly research','Automation · draft','data-open="automation"','workflow')}${row('Settings','Appearance and assistant position','data-dialog="settings"','settings-2')}</div>`]
 };
 const value=content[type]||content.launcher;$('#modal-title').textContent=value[0];$('#modal-content').innerHTML=value[1];if(!modal.open)modal.showModal();icons();if(type==='search')$('#search-query').focus();if(type==='launcher')$('#app-search').focus();
}
function addMessage(text,role='assistant'){const node=document.createElement('div');node.className=role==='user'?'user-message':'assistant-message';node.style.marginTop='16px';node.textContent=text;$('#chat-feed').append(node);$('#chat-feed').scrollTop=$('#chat-feed').scrollHeight;return node}
function sendMessage(text){if(!text.trim())return;addMessage(text,'user');$('#message').value='';const v=text.toLowerCase();if(/automat|weekly|schedule/.test(v)){launchApplication('automation');addMessage('I’ve opened a weekly research workflow. Review its schedule and estimated Compute before enabling the demo.')}else if(/note|summary|summari/.test(v)){openWindow('notes');addMessage('Your research summary is open on the desktop. This demo connects conversation to the Notes surface.')}else if(/connect|secret|token|salesforce/.test(v)){showCredential();toast('Try the inline connection card with a demo value.')}else if(/table|shortlist|compan|research/.test(v)){openWindow('leads');addMessage('I’ve brought your architecture shortlist into focus. You can filter matches, review all 12 companies, or export the sample data.')}else{addMessage('This is a design prototype with scripted interactions. Try “create a summary”, “show my shortlist”, or “automate this weekly” to see how chat opens the right tool.')}}
document.addEventListener('click',e=>{
 const b=e.target.closest('button');if(!b)return;
 if(b.closest('.window'))focusWindow(b.closest('.window').id);
 if(b.dataset.task){const id=b.dataset.task;if(windows[id]==='open'&&state.activeWindow===id)hideWindow(id,'minimized');else openWindow(id)}
 if(b.dataset.dialog)dialog(b.dataset.dialog);
 if(b.dataset.open){modal.close();openWindow(b.dataset.open)}
 if(b.dataset.launch){modal.close();launchApplication(b.dataset.launch)}
 if(b.dataset.minimize)hideWindow(b.dataset.minimize,'minimized');
 if(b.dataset.close)hideWindow(b.dataset.close,'closed');
 if(b.dataset.maximize){const w=$('#'+b.dataset.maximize);setWindowMode(w.id,!w.classList.contains('maximized'));focusWindow(w.id)}
 if(b.dataset.variant){setVariant(b.dataset.variant);modal.close()}
 if(b.dataset.dockPosition){document.body.classList.toggle('dock-top',b.dataset.dockPosition==='top');dialog('settings')}
 if(b.dataset.side){flipChat(b.dataset.side);dialog('settings')}
 if(b.dataset.prompt)sendMessage(b.dataset.prompt);
 if(b.dataset.space){$('#workspace-name').textContent=b.dataset.space;modal.close();toast('Workspace switch preview · sample artifacts remain visible')}
 if(b.dataset.agent){$('.agent-picker').innerHTML=icon('sparkles')+' '+b.dataset.agent+' '+icon('chevron-down');modal.close();icons()}
 const id=b.id;
 if(id==='toggle-assistant'||id==='hide-assistant')toggleAssistant();
 if(id==='show-desktop'){const visible=Object.keys(windows).filter(key=>windows[key]==='open');if(visible.length){desktopRestore=visible;visible.forEach(key=>hideWindow(key,'minimized'))}else{desktopRestore.filter(key=>windows[key]!=='closed').forEach(openWindow);desktopRestore=[]}}
 if(id==='close-modal')modal.close();
 if(id==='flip-chat')flipChat();
 if(id==='arrange'){arrange();toast('Windows arranged')}
 if(id==='desktop-tab'){arrange();openWindow('leads');openWindow('notes')}
 if(id==='previous-variant'||id==='next-variant')setVariant(variants[(variants.indexOf(state.variant)+(id==='next-variant'?1:2))%3]);
 if(id==='filter'){shortlisted=!shortlisted;renderTable()}
 if(id==='show-all'){allRows=!allRows;renderTable()}
 if(id==='open-sources')dialog('sources');
 if(id==='remove-context')$('.context-chip').classList.add('hidden');
 if(id==='attach-shortlist'){$('.context-chip').classList.remove('hidden');modal.close();$('#message').focus()}
 if(id==='reveal-token'){const input=$('#demo-token');input.type=input.type==='password'?'text':'password';b.setAttribute('aria-label',input.type==='password'?'Show demo token':'Hide demo token')}
  if(id==='crm-connect'){modal.close();showCredential()}
 if(id==='enable-automation'){automated=!automated;b.innerHTML=(automated?'Disable demo automation':'Enable demo automation')+icon(automated?'pause':'arrow-right');$('#background-label').textContent=automated?'1 scheduled':'Idle';toast(automated?'Demo enabled. No real tasks are scheduled.':'Demo automation paused.');icons()}
 if(id==='topup-demo'){$('#modal-content').innerHTML='<p>Preview: choose how much Compute to add.</p><div class="usage-row"><b>1,000 Compute</b><span>Pricing to be defined</span></div><div class="demo-note">No purchase or payment is available in this prototype. Set transparent unit pricing before implementing billing.</div>'}
 if(id==='resume-chat')modal.close();
 if(id==='new-chat'||id==='modal-new-chat'){$('#chat-feed').innerHTML='<div class="day-divider">New conversation</div><div class="assistant-message"><p>What would you like to work on?</p></div>';$('.conversation-name').innerHTML='New conversation '+icon('chevron-down');modal.close();icons();$('#message').focus()}
 if(id==='new-space'){$('#modal-content').innerHTML='<p>Give your next idea a home.</p><form id="space-form"><input class="search-input" id="space-name" aria-label="Workspace name" placeholder="Workspace name" required maxlength="50"><button class="primary full">Create demo workspace '+icon('plus')+'</button></form>';icons();$('#space-name').focus()}
 if(id==='export'){const csv='Company,Domain,Specialism,Match,Status\n'+companies.map(c=>c.slice(1).join(',')).join('\n');const a=document.createElement('a');const url=URL.createObjectURL(new Blob([csv],{type:'text/csv'}));a.href=url;a.download='architecture-shortlist-demo.csv';a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);toast('Sample shortlist exported')}
});
document.addEventListener('submit',e=>{e.preventDefault();if(e.target.id==='chat-form')sendMessage($('#message').value);if(e.target.id==='credential-form'){const input=$('#demo-token');if(!input.value.trim())return;input.value='';connected=true;$('#credential-card').innerHTML=`<div class="credential-title"><span class="app-icon mint">${icon('check')}</span><div><b>Salesforce connected</b><small>Demo connection · no account accessed</small></div></div><p>The assistant receives a connection status, not your token.</p>`;icons();toast('Demo input cleared. Nothing was transmitted or saved.')}if(e.target.id==='space-form'){$('#workspace-name').textContent=$('#space-name').value.trim()||'New workspace';Object.keys(windows).forEach(id=>hideWindow(id,'closed'));modal.close();toast('Fresh demo workspace ready')}});
document.addEventListener('input',e=>{if(e.target.id==='app-search'){$$('.launcher-app').forEach(app=>app.classList.toggle('hidden',!app.dataset.appName.includes(e.target.value.toLowerCase())));$('#app-empty').classList.toggle('hidden',$$('.launcher-app:not(.hidden)').length>0)}if(e.target.id==='search-query'){$$('#search-results .menu-row').forEach(r=>r.classList.toggle('hidden',!r.textContent.toLowerCase().includes(e.target.value.toLowerCase())));let empty=$('#search-empty');if(!empty){empty=document.createElement('p');empty.id='search-empty';empty.textContent='No matches. Try “notes”, “table”, or “settings”.';$('#search-results').append(empty)}empty.hidden=$$('#search-results .menu-row:not(.hidden)').length>0}});
document.addEventListener('change',e=>{if(e.target.id==='motion')document.body.classList.toggle('reduce-motion',e.target.checked)});
document.addEventListener('keydown',e=>{if(e.key==='Enter'&&e.target.id==='app-search'){e.preventDefault();$('.launcher-app:not(.hidden)')?.click()}if((e.ctrlKey||e.metaKey)&&e.key==='k'){e.preventDefault();dialog('search')}if(e.key==='Enter'&&!e.shiftKey&&e.target.id==='message'){e.preventDefault();sendMessage(e.target.value)}if(!e.target.closest('input,textarea,[contenteditable]')&&!modal.open&&['ArrowLeft','ArrowRight'].includes(e.key)){setVariant(variants[(variants.indexOf(state.variant)+(e.key==='ArrowRight'?1:2))%3])}});
modal.addEventListener('close',()=>$('.applications-button').setAttribute('aria-expanded','false'));
modal.addEventListener('click',e=>{if(e.target===modal){const r=modal.getBoundingClientRect();if(e.clientX<r.left||e.clientX>r.right||e.clientY<r.top||e.clientY>r.bottom)modal.close()}});
$$('.window').forEach(win=>{win.addEventListener('pointerdown',e=>{if(!e.target.closest('button'))focusWindow(win.id)});const bar=win.querySelector('.window-titlebar');bar.addEventListener('dblclick',e=>{if(!e.target.closest('button'))setWindowMode(win.id,!win.classList.contains('maximized'))});bar.addEventListener('pointerdown',e=>{if(e.target.closest('button')||win.classList.contains('maximized')||innerWidth<801)return;const bounds=win.getBoundingClientRect(),stage=$('#window-stage').getBoundingClientRect();const dx=e.clientX-bounds.left,dy=e.clientY-bounds.top;win.style.transform='none';win.style.width=bounds.width+'px';win.style.height=bounds.height+'px';win.style.right='auto';bar.setPointerCapture(e.pointerId);bar.style.cursor='grabbing';function move(event){win.style.left=Math.max(0,Math.min(stage.width-bounds.width,event.clientX-stage.left-dx))+'px';win.style.top=Math.max(0,Math.min(stage.height-45,event.clientY-stage.top-dy))+'px'}function stop(){bar.removeEventListener('pointermove',move);bar.removeEventListener('pointerup',stop);bar.removeEventListener('pointercancel',stop);bar.style.cursor='grab'}bar.addEventListener('pointermove',move);bar.addEventListener('pointerup',stop);bar.addEventListener('pointercancel',stop)})});
window.addEventListener('resize',arrange);
setVariant(new URLSearchParams(location.search).get('variant')||'obsidian');renderTable();updateDock();icons();
