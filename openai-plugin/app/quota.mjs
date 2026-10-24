import { spawn, spawnSync } from 'node:child_process';
import { existsSync, readdirSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { createInterface } from 'node:readline';

function codexPath() {
  for (const p of [process.env.QUOTA_PANEL_CODEX_PATH, process.env.CODEX_CLI_PATH]) {
    if (p && existsSync(p)) return p;
  }
  const base = join(process.env.LOCALAPPDATA || '', 'OpenAI', 'Codex', 'bin');
  if (existsSync(base)) {
    const candidates = readdirSync(base).map(x => join(base, x, 'codex.exe')).filter(existsSync);
    candidates.sort((a,b) => statSync(b).mtimeMs - statSync(a).mtimeMs);
    if (candidates.length) return candidates[0];
  }
  const found = spawnSync('where.exe', ['codex.exe'], {encoding:'utf8', windowsHide:true});
  const p = found.stdout?.trim().split(/\r?\n/).find(x => x && existsSync(x));
  if (p) return p;
  throw new Error('未找到本机 Codex 程序。请先启动桌面版，或配置 QUOTA_PANEL_CODEX_PATH。');
}

// Query the supported app-server RPC. No token files, model turns, or private endpoints.
export function readAccountQuota() {
  return new Promise((resolve, reject) => {
    const child = spawn(codexPath(), ['app-server', '--stdio', '--disable', 'plugins', '--disable', 'apps'], {
      windowsHide:true, stdio:['pipe','pipe','pipe'],
      env: {...process.env, OTEL_SDK_DISABLED:'true'}
    });
    let finished = false;
    const lines = createInterface({input:child.stdout});
    const timer = setTimeout(() => finish(new Error('读取额度超时，请稍后刷新。')), 25000);
    function finish(error, result) {
      if (finished) return;
      finished = true; clearTimeout(timer); lines.close();
      if (child.pid && child.exitCode == null) {
        if (process.platform === 'win32') {
          // End only the app-server process tree created by this query.
          spawnSync('taskkill.exe', ['/PID', String(child.pid), '/T', '/F'], {windowsHide:true,stdio:'ignore',timeout:5000});
        } else child.kill();
      }
      child.stdin.end();
      error ? reject(error) : resolve(result);
    }
    function send(message) { if (!finished) child.stdin.write(JSON.stringify(message)+'\n'); }
    child.stderr.resume(); // Never log credentials or account details from a child process.
    child.stdin.on('error', () => finish(new Error('额度读取连接已关闭。')));
    child.on('error', () => finish(new Error('无法启动本机 Codex 额度接口。')));
    child.on('exit', () => { if (!finished) finish(new Error('额度读取进程提前退出。')); });
    lines.on('line', line => {
      let message; try { message=JSON.parse(line); } catch { return; }
      if (message.id === 1) {
        if (message.error) return finish(new Error('本机 Codex 初始化失败，请重新打开桌面版后重试。'));
        send({method:'initialized'});
        send({id:2, method:'account/rateLimits/read', params:{excludeResetCreditDetails:true}});
      } else if (message.id === 2) {
        if (message.error) return finish(new Error('暂时无法读取额度，请确认当前账号已在 Codex 中登录后重试。'));
        finish(null, normalizeQuota(message.result));
      } else if (message.method && message.id != null) {
        send({id:message.id, error:{code:-32601,message:'This read-only client does not handle server requests.'}});
      }
    });
    send({id:1, method:'initialize', params:{clientInfo:{name:'local_quota_text',title:'GPT/Codex Desktop Quota Display',version:'1.2.4'}}});
  });
}

const finite = x => typeof x === 'number' && Number.isFinite(x);
function windowSnapshot(value, key) {
  if (!value) return null;
  const used = finite(value.usedPercent) ? Math.max(0,Math.min(100,value.usedPercent)) : null;
  return {key, usedPercent:used, remainingPercent:used == null ? null : 100-used,
    durationMins:finite(value.windowDurationMins) && value.windowDurationMins > 0 ? value.windowDurationMins : null,
    resetsAt:finite(value.resetsAt) && value.resetsAt > 0 ? value.resetsAt : null};
}
export function normalizeQuota(raw) {
  if (!raw || typeof raw !== 'object') throw new Error('额度接口未返回有效数据。');
  const entries = raw.rateLimitsByLimitId && Object.keys(raw.rateLimitsByLimitId).length
    ? Object.entries(raw.rateLimitsByLimitId) : raw.rateLimits ? [[raw.rateLimits.limitId || 'codex',raw.rateLimits]] : [];
  return {
    fetchedAt:new Date().toISOString(), source:'本机 Codex 官方额度接口',
    ordinaryUsageAllowed: typeof raw.ordinaryUsageAllowed === 'boolean' ? raw.ordinaryUsageAllowed : null,
    buckets:entries.filter(([,s])=>s && typeof s==='object').map(([id,s]) => ({
      id, name:typeof s.limitName==='string' && s.limitName ? s.limitName : id==='codex' ? 'Codex / Work 共享额度' : id,
      plan:typeof s.planType==='string' ? s.planType : null,
      windows:[windowSnapshot(s.primary,'primary'),windowSnapshot(s.secondary,'secondary')].filter(Boolean),
      credits:s.credits ? {balance:typeof s.credits.balance==='string' ? s.credits.balance : null,
        unlimited:s.credits.unlimited === true, hasCredits:typeof s.credits.hasCredits==='boolean' ? s.credits.hasCredits : null} : null,
      individualLimit:s.individualLimit ? {
        remainingPercent:finite(s.individualLimit.remainingPercent) ? Math.max(0,Math.min(100,s.individualLimit.remainingPercent)) : null,
        resetsAt:finite(s.individualLimit.resetsAt) ? s.individualLimit.resetsAt : null
      } : null,
      blocked:s.spendControlReached === true || (typeof s.rateLimitReachedType==='string' && s.rateLimitReachedType.length>0)
    }))
  };
}

let cached=null, pending=null;
export async function getQuota({force=false}={}) {
  const age=cached ? Date.now()-Date.parse(cached.fetchedAt) : Infinity;
  if (cached && age<(force ? 10000 : 20000)) return cached;
  if (!pending) pending=readAccountQuota().then(result => (cached=result)).finally(()=>{pending=null;});
  return pending;
}
