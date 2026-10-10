// @vitest-environment node
import type { CredentialChallengeResponse } from '../api/auth.contracts';
import { base64Url, fromBase64Url, seal, sealFields } from './credential-envelope';

/**
 * Seals with the browser code and opens with the same WebCrypto primitives the server's opener uses
 * (RSA-OAEP-256 unwrap, A256GCM with the base64url header as additional data) to prove the envelope shape.
 */
describe('credential envelope (02 §3.4, D-03)', () => {
  let privateKey: CryptoKey;
  let challenge: CredentialChallengeResponse;

  beforeAll(async () => {
    const pair = await crypto.subtle.generateKey({ name: 'RSA-OAEP', modulusLength: 2048, publicExponent: new Uint8Array([1, 0, 1]), hash: 'SHA-256' }, true, ['encrypt', 'decrypt']);
    privateKey = pair.privateKey;
    const jwk = await crypto.subtle.exportKey('jwk', pair.publicKey);
    challenge = {
      kid: '2026-10-09T00:00Z#1',
      publicKey: { kty: 'RSA', n: jwk.n ?? '', e: jwk.e ?? '', alg: 'RSA-OAEP-256', use: 'enc', kid: '2026-10-09T00:00Z#1' },
      publicKeyPem: '',
      algorithm: 'RSA-OAEP-256',
      encryption: 'A256GCM',
      nonce: base64Url(crypto.getRandomValues(new Uint8Array(72))),
      expiresAtUtc: '2026-10-09T00:02:00Z',
    };
  });

  async function open(envelope: string): Promise<{ header: Record<string, string>; payload: Record<string, string> }> {
    const parts = envelope.split('.');
    expect(parts.length).toBe(5);
    const [headerText, wrapped, iv, ciphertext, tag] = parts as [string, string, string, string, string];
    const header = JSON.parse(new TextDecoder().decode(fromBase64Url(headerText))) as Record<string, string>;

    const rawCek = await crypto.subtle.decrypt({ name: 'RSA-OAEP' }, privateKey, fromBase64Url(wrapped));
    const cek = await crypto.subtle.importKey('raw', rawCek, { name: 'AES-GCM' }, false, ['decrypt']);
    const sealed = new Uint8Array([...fromBase64Url(ciphertext), ...fromBase64Url(tag)]);
    const plaintext = await crypto.subtle.decrypt({ name: 'AES-GCM', iv: fromBase64Url(iv), additionalData: new TextEncoder().encode(headerText), tagLength: 128 }, cek, sealed);
    return { header, payload: JSON.parse(new TextDecoder().decode(plaintext)) as Record<string, string> };
  }

  it('produces a compact JWE bound to the field, the endpoint and the nonce', async () => {
    const envelope = await seal(challenge, 'password', 'Correct-Horse-9', 'POST /api/v1/auth/sign-in');
    const { header, payload } = await open(envelope);

    expect(header).toEqual({ alg: 'RSA-OAEP-256', enc: 'A256GCM', kid: challenge.kid });
    expect(payload).toEqual({ v: 'Correct-Horse-9', f: 'password', aud: 'POST /api/v1/auth/sign-in', n: challenge.nonce });
    expect(envelope).not.toContain('Correct-Horse-9');
    expect(fromBase64Url(envelope.split('.')[2] ?? '').length).toBe(12);
    expect(fromBase64Url(envelope.split('.')[4] ?? '').length).toBe(16);
  });

  it('uses a fresh content key and IV for every envelope', async () => {
    const a = await seal(challenge, 'password', 'same', 'POST /x');
    const b = await seal(challenge, 'password', 'same', 'POST /x');
    expect(a).not.toBe(b);
    expect(a.split('.')[1]).not.toBe(b.split('.')[1]);
  });

  it('seals only the listed string fields of a body under one nonce', async () => {
    const sealed = await sealFields({ username: 'mona', password: 'pw', tenant: null }, ['password', 'tenant'], challenge, 'POST /api/v1/auth/sign-in');

    expect(sealed['username']).toBe('mona');
    expect(sealed['tenant']).toBeNull();
    const { payload } = await open(String(sealed['password']));
    expect(payload['v']).toBe('pw');
    expect(payload['n']).toBe(challenge.nonce);
  });

  it('base64url round-trips without padding', () => {
    const bytes = new Uint8Array([0, 1, 2, 250, 251, 252, 253, 254, 255]);
    const text = base64Url(bytes);
    expect(text).not.toMatch(/[+/=]/);
    expect(Array.from(fromBase64Url(text))).toEqual(Array.from(bytes));
  });
});
