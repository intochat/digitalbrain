const { chromium }=require('C:/Users/vhorb/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const fs=require('node:fs');
const path=require('node:path');
(async()=>{
 const browser=await chromium.launch({headless:true,executablePath:'C:/Users/vhorb/AppData/Local/ms-playwright/chromium-1217/chrome-win64/chrome.exe'});
 const errors=[];
 const results=[];
 fs.mkdirSync(path.join(__dirname,'previews'),{recursive:true});
 for(const name of ['lumen','aurora','tactile','atlas']){
  const page=await browser.newPage({viewport:{width:1440,height:960},deviceScaleFactor:1});
  page.on('pageerror',e=>errors.push({name,error:e.message}));
  await page.goto(`http://127.0.0.1:8743/${name}.html`);await page.waitForTimeout(600);
  await page.screenshot({path:path.join(__dirname,'previews',name+'.png'),fullPage:true});
  results.push({name,desktopOverflow:await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth)});
  if(name!=='aurora'){
   await page.locator('.neuron[data-node="gmail"]').click();
   await page.locator('#inspector').getByText('Inbox is ready',{exact:true}).waitFor();
   await page.keyboard.press('Escape');
   await page.locator('.edge-group[data-edge="mail-watch"]').focus();await page.keyboard.press('Enter');
   await page.getByRole('button',{name:'Unsubscribe',exact:true}).click();
   if(await page.locator('.edge-group[data-edge="mail-watch"]').count())throw Error(name+': unsubscribed edge still visible');
   await page.getByRole('button',{name:'Simulate a source event',exact:true}).click();
   let state=await page.evaluate(()=>digitalbrainPrototype.getState());
   if(state.edges.find(e=>e.id==='mail-watch').enabled)throw Error(name+': edge should be absent');
   if(!state.events[0].title.includes('skipped'))throw Error(name+': broadcast delivered after unsubscribe');
   await page.getByRole('button',{name:'Subscribe',exact:true}).click();
   await page.keyboard.press('Escape');
   await page.locator('[data-scenario="review"]').click();await page.waitForTimeout(150);
   await page.getByRole('button',{name:'Pause playback',exact:true}).click();
   const before=(await page.evaluate(()=>digitalbrainPrototype.getState())).run.index;
   await page.waitForTimeout(2800);
   if((await page.evaluate(()=>digitalbrainPrototype.getState())).run.index!==before)throw Error(name+': pause advanced');
   await page.getByRole('button',{name:'Resume playback',exact:true}).click();
   await page.getByRole('button',{name:'Reset playback',exact:true}).click();
   await page.locator('#message').fill('Review my local diff');await page.locator('#message').press('Enter');
   if((await page.evaluate(()=>digitalbrainPrototype.getState())).run.id!=='review')throw Error(name+': composer routing');
   await page.getByRole('button',{name:'Reset playback',exact:true}).click();
   results.at(-1).interactions='node/edge inspectors; unsubscribe; skipped broadcast; subscribe; pause/resume/reset; composer passed';
  }
  await page.setViewportSize({width:390,height:844});await page.waitForTimeout(3400);
  await page.screenshot({path:path.join(__dirname,'previews',name+'-mobile.png'),fullPage:true});
  results.at(-1).mobileOverflow=await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth);
  results.at(-1).mobileComposerVisible=await page.locator(name==='aurora'?'input,textarea':'#message').first().isVisible();
  await page.close();
 }
 await browser.close();
 fs.writeFileSync(path.join(__dirname,'verification.json'),JSON.stringify({results,errors},null,2));
 console.log(JSON.stringify({results,errors},null,2));
 if(errors.length||results.some(r=>r.desktopOverflow||r.mobileOverflow))process.exitCode=1;
})().catch(e=>{console.error(e);process.exitCode=1;});
