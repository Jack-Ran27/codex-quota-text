import {spawn} from 'node:child_process';
import {readFile} from 'node:fs/promises';
import {existsSync} from 'node:fs';
import {join} from 'node:path';
import {fileURLToPath} from 'node:url';
import {createInterface} from 'node:readline';
const root=fileURLToPath(new URL('.',import.meta.url));
const annotation={readOnlyHint:false,destructiveHint:false,idempotentHint:true,openWorldHint:false};
const empty={type:'object',properties:{},additionalProperties:false};
const tools=[
 {name:'start_quota_text',title:'显示额度小字',description:'在本机安装或启动额度文字浮层。只有用户明确要求随登录自动运行时才传入 autostart:true；否则保留原有启动设置。',inputSchema:{type:'object',properties:{autostart:{type:'boolean'}},additionalProperties:false},annotations:annotation},
 {name:'pause_quota_text',title:'暂停额度小字',description:'暂时关闭本机文字浮层，不删除程序，不改变原有自动启动设置。',inputSchema:empty,annotations:annotation},
 {name:'quota_text_status',title:'查看小字状态',description:'只读查看小字组件的上次额度数据和运行状态；不读取聊天内容。',inputSchema:empty,annotations:{...annotation,readOnlyHint:true}}
];
const installed=join(process.env.LOCALAPPDATA||'', 'Programs','CodexQuotaText','QuotaText.exe');
async function execute(file,args){return new Promise((resolve,reject)=>{const cp=spawn(file,args,{windowsHide:true,stdio:'ignore',shell:false});const timer=setTimeout(()=>{cp.kill();reject(new Error('本地操作超时。'));},15000);cp.on('error',()=>{clearTimeout(timer);reject(new Error('无法运行本机组件。'));});cp.on('exit',code=>{clearTimeout(timer);code===0?resolve():reject(new Error('本机组件返回错误。'));});});}
async function status(){let s=null;try{s=JSON.parse(await readFile(join(process.env.LOCALAPPDATA||'','CodexQuotaText','state','status.json'),'utf8'));}catch{}let running=false;if(s?.processId){try{process.kill(s.processId,0);running=true;}catch{}}
return {running,displayed:running&&s?.visible===true,remaining:s?.percent??null,resetDate:s?.resetDate??null,resetTime:s?.resetTime??null,timezone:'Asia/Shanghai',fetchedAt:s?.updatedAt||null,stale:s?.stale??null,version:s?.version??null};}
async function dispatch(m){const p=m.params||{};switch(m.method){
case 'initialize':return {protocolVersion:p.protocolVersion||'2025-06-18',capabilities:{tools:{}},serverInfo:{name:'quota-text',version:'1.2.1'},instructions:'Windows-only local text overlay. Autostart is opt-in. Never purchase credits or change account settings.'};
case 'ping':return {};
case 'tools/list':return {tools};
case 'resources/list':return {resources:[]};
case 'tools/call':{if(!tools.some(t=>t.name===p.name))throw new Error('未知工具。');const a=p.arguments??{};if(!a||typeof a!=='object'||Array.isArray(a)||Object.keys(a).some(k=>p.name!=='start_quota_text'||k!=='autostart')||(a.autostart!==undefined&&typeof a.autostart!=='boolean'))throw new Error('参数无效。');
 if(p.name==='start_quota_text'){await execute(join(root,'Setup.exe'),a.autostart===true?[]:['--no-autostart']);for(let i=0;i<12;i++){if((await status()).running)break;await new Promise(r=>setTimeout(r,150));}}
 if(p.name==='pause_quota_text'&&existsSync(installed)){await execute(installed,['--stop']);for(let i=0;i<12;i++){if(!(await status()).running)break;await new Promise(r=>setTimeout(r,150));}}
 const data=await status();return {content:[{type:'text',text:JSON.stringify(data)}],structuredContent:data};}
default:throw new Error('不支持的方法。');}}
createInterface({input:process.stdin}).on('line',async line=>{let m;try{m=JSON.parse(line);}catch{process.stdout.write(JSON.stringify({jsonrpc:'2.0',id:null,error:{code:-32700,message:'Parse error'}})+'\n');return;}if(m.id==null)return;
 try{process.stdout.write(JSON.stringify({jsonrpc:'2.0',id:m.id,result:await dispatch(m)})+'\n');}catch(e){const response=m.method==='tools/call'?{result:{isError:true,content:[{type:'text',text:e.message}]}}:{error:{code:-32601,message:e.message}};process.stdout.write(JSON.stringify({jsonrpc:'2.0',id:m.id,...response})+'\n');}});
