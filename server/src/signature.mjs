import { createHash, createHmac, randomBytes, timingSafeEqual } from 'node:crypto';

/** Same value the desktop client uses (DteProtocol.MaxClockSkew). */
export const MAX_SKEW_SECONDS = 300;

export function sha256Hex(buffer) {
  return createHash('sha256').update(buffer).digest('hex');
}

/**
 * Canonical string, '\n' separated:
 *   METHOD
 *   PATH_AND_QUERY
 *   TIMESTAMP
 *   NONCE
 *   SHA256_HEX_LOWERCASE(body)
 */
export function canonicalRequest({ method, pathAndQuery, timestamp, nonce, body }) {
  return [method.toUpperCase(), pathAndQuery, timestamp, nonce, sha256Hex(body)].join('\n');
}

export function computeSignature({ secret, method, pathAndQuery, timestamp, nonce, body }) {
  const canonical = canonicalRequest({ method, pathAndQuery, timestamp, nonce, body });
  return createHmac('sha256', secret).update(canonical, 'utf8').digest('hex');
}

export function safeEqual(a, b) {
  if (typeof a !== 'string' || typeof b !== 'string') return false;
  const left = Buffer.from(a, 'utf8');
  const right = Buffer.from(b, 'utf8');
  return left.length === right.length && timingSafeEqual(left, right);
}

export function newNonce() {
  return randomBytes(16).toString('hex');
}

export function newSessionToken() {
  return randomBytes(24).toString('base64url');
}
