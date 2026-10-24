import {readAccountQuota} from './quota.mjs';
try { process.stdout.write(JSON.stringify(await readAccountQuota())); }
catch { process.stderr.write('Quota read failed'); process.exitCode=1; }
