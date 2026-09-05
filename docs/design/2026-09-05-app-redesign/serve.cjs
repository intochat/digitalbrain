const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const root = __dirname;
const mime = {'.html':'text/html; charset=utf-8','.css':'text/css; charset=utf-8','.js':'text/javascript; charset=utf-8','.png':'image/png','.md':'text/plain; charset=utf-8','.json':'application/json; charset=utf-8'};
http.createServer((req,res)=>{
  let url;
  try { url = decodeURIComponent(new URL(req.url,'http://localhost').pathname); } catch {res.writeHead(400);res.end();return;}
  const filename=path.resolve(root,'.'+(url==='/'?'/index.html':url));
  if(!filename.startsWith(root+path.sep)){res.writeHead(403);res.end();return;}
  fs.readFile(filename,(error,body)=>{if(error){res.writeHead(404);res.end('Not found');return;}res.writeHead(200,{'Content-Type':mime[path.extname(filename)]||'application/octet-stream','Cache-Control':'no-store','X-Content-Type-Options':'nosniff'});res.end(body);});
}).listen(8743,'127.0.0.1',()=>console.log('DigitalBrain design collection: http://127.0.0.1:8743'));
