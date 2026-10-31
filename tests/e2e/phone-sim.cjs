// Independent re-implementation of the phone side of the protocol (Node crypto), used to verify
// that the .NET host, the relay and a different crypto stack agree. Usage: node phone-sim.cjs <relayWsUrl> <code>
'use strict';
const crypto = require('node:crypto');
const ALPHA = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';

function fromCode(code) {
  const clean = [...code.toUpperCase()].filter(c => ALPHA.includes(c)).join('');
  if (clean.length !== 26) throw new Error('bad code');
  let buf = 0, bits = 0; const out = [];
  for (const c of clean) { buf = (buf << 5) | ALPHA.indexOf(c); bits += 5; if (bits >= 8) { out.push((buf >> (bits - 8)) & 255); bits -= 8; } }
  return Buffer.from(out);
}
function derive(secret) {
  const hk = (info, len) => Buffer.from(crypto.hkdfSync('sha256', secret, Buffer.alloc(0), info, len));
  return { room: hk('usage/v1/room', 16).toString('hex'), key: hk('usage/v1/key', 32) };
}
const seal = (k, role, obj) => {
  const nonce = crypto.randomBytes(12);
  const c = crypto.createCipheriv('aes-256-gcm', k.key, nonce);
  c.setAAD(Buffer.from(k.room + ':' + role));
  const ct = Buffer.concat([c.update(JSON.stringify(obj)), c.final(), c.getAuthTag()]);
  return JSON.stringify({ v: 1, n: nonce.toString('base64'), c: ct.toString('base64') });
};
const open = (k, role, frame) => {
  const e = JSON.parse(frame); const all = Buffer.from(e.c, 'base64');
  const d = crypto.createDecipheriv('aes-256-gcm', k.key, Buffer.from(e.n, 'base64'));
  d.setAAD(Buffer.from(k.room + ':' + role)); d.setAuthTag(all.subarray(all.length - 16));
  return JSON.parse(Buffer.concat([d.update(all.subarray(0, all.length - 16)), d.final()]).toString());
};

const [relay, code] = process.argv.slice(2);
const keys = derive(fromCode(code));
const ws = new WebSocket(relay.replace(/\/$/, '') + '/r/' + keys.room + '?role=client');
const timer = setTimeout(() => { console.log('TIMEOUT'); process.exit(2); }, 60000);
let sawControl = false;
ws.onopen = () => { ws.send(seal(keys, 'c', { t: 'get', ts: Date.now() })); };
ws.onmessage = ev => {
  const text = String(ev.data);
  const o = JSON.parse(text);
  if (o.relay) { sawControl = true; console.log('relay:', o.relay); if (o.relay === 'host_online') ws.send(seal(keys, 'c', { t: 'get', ts: Date.now() })); return; }
  const m = open(keys, 'h', text);
  const d = m.d;
  console.log('snapshot', m.t, 'busy=' + m.busy, d ? d.providers.map(p => `${p.id}:${p.status}`).join(',') : 'none');
  if (d) { clearTimeout(timer); console.log('OK_E2E'); ws.close(); process.exit(0); }
};
ws.onerror = e => { console.log('ERR', e.message || e); process.exit(3); };
