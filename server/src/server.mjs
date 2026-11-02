#!/usr/bin/env node
/**
 * Driven Test Engine — reference testing-service server (protocol v1).
 *
 * WHAT THIS IS
 *   A dependency-free implementation of the desktop client's authenticated transport:
 *   HMAC-SHA256 request signing, replay protection, session handshake, log streaming and the
 *   anti-cheat status channel. It exists so the authentication half of the link can be verified
 *   end-to-end before you connect it to your own game.
 *
 * WHAT THIS IS NOT
 *   It does not run tests. POST /v1/tests/run answers 501 `not_implemented` on purpose: a test
 *   result may only come from your own Roblox game server. See docs/ROBLOX-INTEGRATION.md and
 *   roblox/DrivenTestEngineBridge.server.lua for the wiring you need to add.
 */
import http from 'node:http';
import { fileURLToPath } from 'node:url';
import process from 'node:process';
import {
  MAX_SKEW_SECONDS,
  computeSignature,
  newSessionToken,
  safeEqual,
} from './signature.mjs';

const DEFAULTS = {
  port: 8791,
  host: '127.0.0.1',
  keyId: 'dte-dev-key',
  secret: 'dev-secret-change-me-0123456789abcdef',
  robloxUserId: '0000000000',
  displayName: 'Driven Developer',
  placeId: '0000000000',
  universeId: '0000000000',
  sessionId: 'private-test-session',
  serverInstanceId: 'reference-instance',
  isDeveloper: true,
  isPrivateSession: true,
  sessionTtlSeconds: 900,
  allowedTests: ['speed', 'jump', 'auto-catch', 'dime-accuracy'],
};

export function loadConfig(overrides = {}) {
  const env = process.env;
  const bool = (value, fallback) =>
    value === undefined ? fallback : String(value).toLowerCase() === 'true';

  return {
    port: Number(env.DTE_PORT ?? DEFAULTS.port),
    host: env.DTE_HOST ?? DEFAULTS.host,
    keyId: env.DTE_KEY_ID ?? DEFAULTS.keyId,
    secret: env.DTE_SECRET ?? DEFAULTS.secret,
    robloxUserId: env.DTE_ROBLOX_USER_ID ?? DEFAULTS.robloxUserId,
    displayName: env.DTE_DISPLAY_NAME ?? DEFAULTS.displayName,
    placeId: env.DTE_PLACE_ID ?? DEFAULTS.placeId,
    universeId: env.DTE_UNIVERSE_ID ?? DEFAULTS.universeId,
    sessionId: env.DTE_SESSION_ID ?? DEFAULTS.sessionId,
    serverInstanceId: env.DTE_SERVER_INSTANCE_ID ?? DEFAULTS.serverInstanceId,
    isDeveloper: bool(env.DTE_IS_DEVELOPER, DEFAULTS.isDeveloper),
    isPrivateSession: bool(env.DTE_IS_PRIVATE_SESSION, DEFAULTS.isPrivateSession),
    sessionTtlSeconds: Number(env.DTE_SESSION_TTL ?? DEFAULTS.sessionTtlSeconds),
    allowedTests: (env.DTE_ALLOWED_TESTS ?? DEFAULTS.allowedTests.join(','))
      .split(',')
      .map((t) => t.trim())
      .filter(Boolean),
    ...overrides,
  };
}

export function createServer(config = loadConfig()) {
  const sessions = new Map();
  const nonces = new Map();
  const logs = [];
  let logSequence = 0;
  const state = { monitoringEnabled: false, lastScanAt: null };

  const record = (level, category, message, detail) => {
    logs.push({
      id: String(logSequence++),
      timestamp: new Date().toISOString(),
      level,
      category,
      message,
      detail: detail ?? null,
    });
    if (logs.length > 5000) logs.shift();
  };

  const send = (res, status, payload) => {
    const body = payload === undefined ? '' : JSON.stringify(payload);
    res.writeHead(status, {
      'content-type': 'application/json; charset=utf-8',
      'content-length': Buffer.byteLength(body),
      'cache-control': 'no-store',
      'x-dte-protocol': '1.0',
    });
    res.end(body);
  };

  const fail = (res, status, code, message, hint) =>
    send(res, status, { error: { code, message, hint: hint ?? null } });

  const pruneNonces = (now) => {
    for (const [nonce, expiry] of nonces) {
      if (expiry <= now) nonces.delete(nonce);
    }
  };

  const verifySignature = (req, rawBody) => {
    const keyId = req.headers['x-dte-key-id'];
    const timestamp = req.headers['x-dte-timestamp'];
    const nonce = req.headers['x-dte-nonce'];
    const signature = req.headers['x-dte-signature'];

    if (!keyId || !timestamp || !nonce || !signature) {
      return { ok: false, status: 401, code: 'unauthorized', message: 'Missing signing headers.' };
    }

    if (keyId !== config.keyId) {
      return { ok: false, status: 401, code: 'unauthorized', message: 'Unknown key id.' };
    }

    const nowSeconds = Math.floor(Date.now() / 1000);
    const skew = Math.abs(nowSeconds - Number(timestamp));
    if (!Number.isFinite(skew) || skew > MAX_SKEW_SECONDS) {
      return { ok: false, status: 401, code: 'clock_skew', message: 'Timestamp outside the allowed window.' };
    }

    pruneNonces(nowSeconds);
    if (nonces.has(nonce)) {
      return { ok: false, status: 401, code: 'replay_detected', message: 'Nonce already used.' };
    }

    const expected = computeSignature({
      secret: config.secret,
      method: req.method,
      pathAndQuery: req.url,
      timestamp,
      nonce,
      body: rawBody,
    });

    if (!safeEqual(expected, signature)) {
      record('warning', 'session', 'Rejected request: bad signature', req.url);
      return { ok: false, status: 401, code: 'unauthorized', message: 'Signature mismatch.' };
    }

    nonces.set(nonce, nowSeconds + MAX_SKEW_SECONDS);
    return { ok: true };
  };

  const requireSession = (req) => {
    const header = req.headers.authorization ?? '';
    const token = header.startsWith('Bearer ') ? header.slice(7) : null;
    if (!token) return { ok: false, status: 401, code: 'unauthorized', message: 'Missing session token.' };
    const session = sessions.get(token);
    if (!session) return { ok: false, status: 401, code: 'unauthorized', message: 'Unknown or closed session.' };
    if (session.expiresAt <= Date.now()) {
      sessions.delete(token);
      return { ok: false, status: 401, code: 'unauthorized', message: 'Session token expired.' };
    }
    return { ok: true, session, token };
  };

  const sessionPayload = (token, session) => ({
    sessionToken: token,
    expiresAt: new Date(session.expiresAt).toISOString(),
    serverVersion: '1.0.0-reference',
    protocolVersion: '1.0',
    account: {
      robloxUserId: config.robloxUserId,
      displayName: config.displayName,
      isDeveloper: config.isDeveloper,
    },
    testSession: {
      sessionId: config.sessionId,
      placeId: config.placeId,
      universeId: config.universeId,
      serverInstanceId: config.serverInstanceId,
      isPrivateTestSession: config.isPrivateSession,
    },
    capabilities: ['tests:run', 'logs:read', 'anticheat:read', 'anticheat:write'],
    allowedTests: config.allowedTests,
  });

  const server = http.createServer(async (req, res) => {
    const chunks = [];
    for await (const chunk of req) chunks.push(chunk);
    const rawBody = Buffer.concat(chunks);
    const path = req.url ?? '/';

    if (req.method === 'GET' && path === '/') {
      return send(res, 200, {
        service: 'dte-reference-server',
        protocol: '1.0',
        note: 'Authentication reference only — test execution must be wired to your own game.',
      });
    }

    const signatureCheck = verifySignature(req, rawBody);
    if (!signatureCheck.ok) {
      return fail(res, signatureCheck.status, signatureCheck.code, signatureCheck.message);
    }

    if (req.method === 'GET' && path.startsWith('/v1/health')) {
      return send(res, 200, {
        status: 'ok',
        serverVersion: '1.0.0-reference',
        protocolVersion: '1.0',
        game: 'Driven Football (reference)',
        time: new Date().toISOString(),
      });
    }

    if (req.method === 'POST' && path.startsWith('/v1/session/handshake')) {
      if (!config.isDeveloper) {
        record('error', 'session', 'Handshake refused: account is not a developer');
        return fail(res, 403, 'developer_only', 'This account is not on the developer allow-list.');
      }
      if (!config.isPrivateSession) {
        record('error', 'session', 'Handshake refused: not a private test session');
        return fail(res, 403, 'private_session_required', 'Tests may only run inside a private test session.');
      }

      const token = newSessionToken();
      const session = {
        expiresAt: Date.now() + config.sessionTtlSeconds * 1000,
        robloxUserId: config.robloxUserId,
      };
      sessions.set(token, session);
      record('info', 'session', `Session opened for ${config.displayName} (${config.robloxUserId})`);
      return send(res, 200, sessionPayload(token, session));
    }

    if (req.method === 'POST' && path.startsWith('/v1/session/refresh')) {
      const auth = requireSession(req);
      if (!auth.ok) return fail(res, auth.status, auth.code, auth.message);
      auth.session.expiresAt = Date.now() + config.sessionTtlSeconds * 1000;
      record('info', 'session', 'Session token refreshed');
      return send(res, 200, sessionPayload(auth.token, auth.session));
    }

    if (req.method === 'POST' && path.startsWith('/v1/session/close')) {
      const auth = requireSession(req);
      if (!auth.ok) return fail(res, auth.status, auth.code, auth.message);
      sessions.delete(auth.token);
      record('info', 'session', 'Session closed by client');
      return send(res, 200, { ok: true });
    }

    if (req.method === 'POST' && path.startsWith('/v1/tests/run')) {
      const auth = requireSession(req);
      if (!auth.ok) return fail(res, auth.status, auth.code, auth.message);

      let payload = {};
      try {
        payload = rawBody.length ? JSON.parse(rawBody.toString('utf8')) : {};
      } catch {
        return fail(res, 400, 'validation_failed', 'Request body is not valid JSON.');
      }

      const testId = payload.testId;
      if (!config.allowedTests.includes(testId)) {
        return fail(res, 422, 'unknown_test', `Test '${testId}' is not enabled for this session.`);
      }

      record('warning', 'test', `Run request for '${testId}' reached the reference server`);
      return fail(
        res,
        501,
        'not_implemented',
        'The reference server does not execute tests.',
        'Wire POST /v1/tests/run to your Roblox game (docs/ROBLOX-INTEGRATION.md) so results come from your server.'
      );
    }

    if (req.method === 'GET' && path.startsWith('/v1/tests/runs/')) {
      const auth = requireSession(req);
      if (!auth.ok) return fail(res, auth.status, auth.code, auth.message);
      return fail(res, 404, 'unknown_test', 'The reference server has never produced a run.');
    }

    if (req.method === 'GET' && path.startsWith('/v1/logs')) {
      const auth = requireSession(req);
      if (!auth.ok) return fail(res, auth.status, auth.code, auth.message);

      const url = new URL(path, 'http://localhost');
      const since = Number(url.searchParams.get('since') ?? '0');
      const limit = Math.min(Number(url.searchParams.get('limit') ?? '100') || 100, 500);
      const start = Number.isFinite(since) && since >= 0 ? since : 0;
      const slice = logs.slice(start, start + limit);
      const nextCursor = String(start + slice.length);
      return send(res, 200, { entries: slice, nextCursor });
    }

    if (req.method === 'GET' && path.startsWith('/v1/anticheat/status')) {
      const auth = requireSession(req);
      if (!auth.ok) return fail(res, auth.status, auth.code, auth.message);
      return send(res, 200, {
        monitoringEnabled: state.monitoringEnabled,
        lastScanAt: state.lastScanAt,
        signals: [],
      });
    }

    if (req.method === 'POST' && path.startsWith('/v1/anticheat/monitor')) {
      const auth = requireSession(req);
      if (!auth.ok) return fail(res, auth.status, auth.code, auth.message);

      let payload = {};
      try {
        payload = rawBody.length ? JSON.parse(rawBody.toString('utf8')) : {};
      } catch {
        return fail(res, 400, 'validation_failed', 'Request body is not valid JSON.');
      }

      state.monitoringEnabled = Boolean(payload.enabled);
      state.lastScanAt = new Date().toISOString();
      record(
        'info',
        'anticheat',
        `Monitoring ${state.monitoringEnabled ? 'armed' : 'disarmed'} by the client`
      );
      return send(res, 200, {
        monitoringEnabled: state.monitoringEnabled,
        lastScanAt: state.lastScanAt,
        signals: [],
      });
    }

    return fail(res, 404, 'not_found', `No route for ${req.method} ${path}`);
  });

  return { server, config, state, sessions, logs };
}

const isMain = process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1];

if (isMain) {
  const config = loadConfig();
  const { server } = createServer(config);
  server.listen(config.port, config.host, () => {
    console.log('Driven Test Engine reference testing service');
    console.log(`  listening   http://${config.host}:${config.port}`);
    console.log(`  key id      ${config.keyId}`);
    console.log(`  account     ${config.displayName} (${config.robloxUserId})`);
    console.log(`  place       ${config.placeId} · private session: ${config.isPrivateSession}`);
    console.log(`  tests       ${config.allowedTests.join(', ') || '(none)'}`);
    console.log('');
    console.log('  Test execution is NOT implemented here: POST /v1/tests/run returns 501.');
    console.log('  Wire it to your own game server — see docs/ROBLOX-INTEGRATION.md.');
  });
}
