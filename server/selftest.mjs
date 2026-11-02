#!/usr/bin/env node
/**
 * End-to-end check of the protocol's authentication half against the reference server.
 * Run with: npm test   (from ./server)
 */
import { createServer, loadConfig } from './src/server.mjs';
import { randomBytes, randomUUID } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { newNonce, computeSignature, canonicalRequest, sha256Hex } from './src/signature.mjs';

// Exercise the same generated-per-install credential shape used by the desktop local profile.
const config = loadConfig({
  port: 0,
  host: '127.0.0.1',
  keyId: `dte-local-${randomUUID().replaceAll('-', '')}`,
  secret: randomBytes(48).toString('base64'),
});
const { server, logs } = createServer(config);

let passed = 0;
let failed = 0;

function check(name, condition, detail = '') {
  if (condition) {
    passed += 1;
    console.log(`  PASS  ${name}`);
  } else {
    failed += 1;
    console.log(`  FAIL  ${name}${detail ? ' — ' + detail : ''}`);
  }
}

async function call(base, method, path, { body = '', token, secret = config.secret, timestamp, nonce, keyId = config.keyId } = {}) {
  const ts = timestamp ?? String(Math.floor(Date.now() / 1000));
  const n = nonce ?? newNonce();
  const signature = computeSignature({ secret, method, pathAndQuery: path, timestamp: ts, nonce: n, body });

  const headers = {
    'content-type': 'application/json',
    'x-dte-key-id': keyId,
    'x-dte-timestamp': ts,
    'x-dte-nonce': n,
    'x-dte-signature': signature,
    'x-dte-protocol': '1.0',
    'x-dte-client': 'selftest',
  };
  if (token) headers.authorization = `Bearer ${token}`;

  const response = await fetch(base + path, { method, headers, body: method === 'GET' ? undefined : body });
  const text = await response.text();
  let json = null;
  try {
    json = text ? JSON.parse(text) : null;
  } catch {
    json = { raw: text };
  }
  return { status: response.status, json, headers: response.headers };
}

// 0. documented known-answer vector
const vector = JSON.parse(readFileSync(new URL('./test-vector.json', import.meta.url), 'utf8'));
const vectorBody = Buffer.from(vector.body, 'utf8');
const vectorCanonical = canonicalRequest({
  method: vector.method,
  pathAndQuery: vector.pathAndQuery,
  timestamp: vector.timestamp,
  nonce: vector.nonce,
  body: vectorBody,
});
const vectorSignature = computeSignature({
  secret: vector.secret,
  method: vector.method,
  pathAndQuery: vector.pathAndQuery,
  timestamp: vector.timestamp,
  nonce: vector.nonce,
  body: vectorBody,
});
check('documented body digest matches', sha256Hex(vectorBody) === vector.bodySha256Hex);
check('documented canonical string matches', vectorCanonical === vector.canonical);
check('documented signature matches', vectorSignature === vector.signature);

const { port } = await new Promise((resolve) => {
  server.listen(0, '127.0.0.1', () => resolve(server.address()));
});
const base = `http://127.0.0.1:${port}`;

console.log('\nDriven Test Engine — protocol authentication self-test');
console.log(`  reference server on ${base}\n`);

// 1. health
const health = await call(base, 'GET', '/v1/health');
check('health returns 200', health.status === 200, `got ${health.status}`);
check('health reports protocol 1.0', health.json?.protocolVersion === '1.0');

// 2. unsigned / badly signed request is refused
const unsigned = await fetch(base + '/v1/health');
check('unsigned request is refused (401)', unsigned.status === 401, `got ${unsigned.status}`);

const badSig = await call(base, 'GET', '/v1/health', { secret: 'wrong-secret-wrong-secret-wrong!!' });
check('bad signature is refused (401)', badSig.status === 401);
check('bad signature error code', badSig.json?.error?.code === 'unauthorized');

// 3. stale timestamp
const stale = await call(base, 'GET', '/v1/health', { timestamp: String(Math.floor(Date.now() / 1000) - 900) });
check('stale timestamp is refused (clock_skew)', stale.status === 401 && stale.json?.error?.code === 'clock_skew');

// 4. replay protection
const replayNonce = newNonce();
const first = await call(base, 'GET', '/v1/health', { nonce: replayNonce });
const second = await call(base, 'GET', '/v1/health', { nonce: replayNonce, timestamp: firstTs(first) });
check('nonce can be used once', first.status === 200);
check('replayed nonce is refused', second.status === 401 && second.json?.error?.code === 'replay_detected');

function firstTs(response) {
  return String(Math.floor(Date.now() / 1000));
}

// 5. handshake
const handshake = await call(base, 'POST', '/v1/session/handshake', {
  body: JSON.stringify({ clientId: 'selftest', clientVersion: '1.0.0', protocol: '1.0', platform: 'windows' }),
});
check('handshake returns 200', handshake.status === 200, JSON.stringify(handshake.json));
check('handshake returns a session token', typeof handshake.json?.sessionToken === 'string' && handshake.json.sessionToken.length > 10);
check('handshake reports a developer account', handshake.json?.account?.isDeveloper === true);
check('handshake reports a private test session', handshake.json?.testSession?.isPrivateTestSession === true);
check('handshake reports allowed tests', Array.isArray(handshake.json?.allowedTests) && handshake.json.allowedTests.length === 4);
check('handshake expiresAt is ISO-8601', !Number.isNaN(Date.parse(handshake.json?.expiresAt ?? '')));

const token = handshake.json.sessionToken;

// 6. session-bound routes require the bearer token
const noToken = await call(base, 'GET', '/v1/logs');
check('session route without token is refused', noToken.status === 401);

// 7. run request is honestly refused (no fabricated results)
const run = await call(base, 'POST', '/v1/tests/run', {
  token,
  body: JSON.stringify({ testId: 'speed', parameters: { durationSeconds: 4 }, clientRunId: 'abc123' }),
});
check('run is refused with not_implemented', run.status === 501 && run.json?.error?.code === 'not_implemented');
check('run refusal carries a hint', typeof run.json?.error?.hint === 'string' && run.json.error.hint.length > 0);

const unknownTest = await call(base, 'POST', '/v1/tests/run', {
  token,
  body: JSON.stringify({ testId: 'godmode', clientRunId: 'abc124' }),
});
check('unknown test id is rejected (422 unknown_test)', unknownTest.status === 422 && unknownTest.json?.error?.code === 'unknown_test');

// 8. anti-cheat channel
const monitor = await call(base, 'POST', '/v1/anticheat/monitor', { token, body: JSON.stringify({ enabled: true }) });
check('monitoring can be armed', monitor.status === 200 && monitor.json?.monitoringEnabled === true);
const status = await call(base, 'GET', '/v1/anticheat/status', { token });
check('monitoring state is readable', status.json?.monitoringEnabled === true);
check('no signals are invented', Array.isArray(status.json?.signals) && status.json.signals.length === 0);

// 9. log cursor
const logsA = await call(base, 'GET', '/v1/logs?limit=2', { token });
check('logs are paginated', Array.isArray(logsA.json?.entries) && logsA.json.entries.length <= 2);
const cursor = logsA.json.nextCursor;
const logsB = await call(base, 'GET', `/v1/logs?limit=2&since=${cursor}`, { token });
check('log cursor advances', logsB.json?.nextCursor !== undefined && Number(logsB.json.nextCursor) >= Number(cursor));
check('server logged the session', logs.some((entry) => entry.message.includes('Session opened')));

// 10. refresh + close
const refresh = await call(base, 'POST', '/v1/session/refresh', { token, body: '{}' });
check('session refresh works', refresh.status === 200 && typeof refresh.json?.sessionToken === 'string');

const close = await call(base, 'POST', '/v1/session/close', { token, body: '{}' });
check('session close works', close.status === 200);

const afterClose = await call(base, 'GET', '/v1/logs', { token });
check('closed session token is refused', afterClose.status === 401);

server.close();

console.log(`\n${passed} passed, ${failed} failed\n`);
process.exit(failed === 0 ? 0 : 1);
