// Test-only: derive compatibility fixtures from the existing HTML, never used by the native app.
const fs=require('node:fs'),vm=require('node:vm'),path=require('node:path');
const html=fs.readFileSync(path.join(__dirname,'../图片处理-双工具整合版.html'),'utf8');
const m=html.match(/<iframe id="tomato"[^>]*?(?:srcdoc|data-source)="([\s\S]*?)"><\/iframe>/);
const text=m[1].replace(/&quot;/g,'"').replace(/&lt;/g,'<').replace(/&gt;/g,'>').replace(/&amp;/g,'&');
const code=text.match(/function gilbert2d[\s\S]*?(?=<\/script>)/)[0];
const ctx={};vm.createContext(ctx);vm.runInContext(code,ctx);
const sizes=[[1,1],[1,17],[17,1],[2,3],[3,2],[3,3],[5,7],[7,5],[11,13],[13,11],[32,17],[17,32],[64,64]];
const fixtures=sizes.map(([width,height])=>({width,height,indices:ctx.gilbert2d(width,height).map(([x,y])=>y*width+x),offset:Math.round((Math.sqrt(5)-1)/2*width*height)}));
fs.writeFileSync(path.join(__dirname,'compatibility-fixtures.json'),JSON.stringify(fixtures));
console.log(`Generated ${fixtures.length} original-JavaScript curve fixtures.`);
