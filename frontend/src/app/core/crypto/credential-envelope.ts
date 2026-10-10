import type { CredentialChallengeResponse } from '../api/auth.contracts';

/**
 * The browser side of the credential envelope (02 §3.4, §3.5, D-03), with the platform's WebCrypto only:
 * a compact JWE, header {alg: RSA-OAEP-256, enc: A256GCM, kid}, a fresh AES-256 content key wrapped with the
 * challenge's RSA key, AES-GCM over {v, f, aud, n} with the base64url header as additional data. One challenge
 * serves one request; every credential field of that request carries the same nonce.
 */

const SUBTLE = (): SubtleCrypto => {
  const subtle = globalThis.crypto?.subtle;
  if (!subtle) {
    throw new Error('WebCrypto is not available; the credential envelope cannot be sealed.');
  }

  return subtle;
};

export function base64Url(bytes: ArrayBuffer | Uint8Array<ArrayBufferLike>): string {
  const view = bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes);
  let binary = '';
  for (const byte of view) {
    binary += String.fromCharCode(byte);
  }

  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

export function fromBase64Url(text: string): Uint8Array<ArrayBuffer> {
  const padded = text.replace(/-/g, '+').replace(/_/g, '/') + '='.repeat((4 - (text.length % 4)) % 4);
  const binary = atob(padded);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) {
    bytes[i] = binary.charCodeAt(i);
  }

  return bytes;
}

export interface EnvelopePayload {
  readonly v: string;
  readonly f: string;
  readonly aud: string;
  readonly n: string;
}

export async function seal(challenge: CredentialChallengeResponse, field: string, value: string, audience: string): Promise<string> {
  const subtle = SUBTLE();
  const encoder = new TextEncoder();

  const header = base64Url(encoder.encode(JSON.stringify({ alg: 'RSA-OAEP-256', enc: 'A256GCM', kid: challenge.kid })));

  const rsa = await subtle.importKey(
    'jwk',
    { kty: 'RSA', n: challenge.publicKey.n, e: challenge.publicKey.e, alg: 'RSA-OAEP-256', ext: true },
    { name: 'RSA-OAEP', hash: 'SHA-256' },
    false,
    ['encrypt'],
  );

  const cek = await subtle.generateKey({ name: 'AES-GCM', length: 256 }, true, ['encrypt']);
  const rawCek = await subtle.exportKey('raw', cek);
  const wrappedKey = await subtle.encrypt({ name: 'RSA-OAEP' }, rsa, rawCek);

  const iv = crypto.getRandomValues(new Uint8Array(12));
  const payload: EnvelopePayload = { v: value, f: field, aud: audience, n: challenge.nonce };
  const sealed = new Uint8Array(
    await subtle.encrypt({ name: 'AES-GCM', iv, additionalData: encoder.encode(header), tagLength: 128 }, cek, encoder.encode(JSON.stringify(payload))),
  );
  const ciphertext = sealed.slice(0, sealed.length - 16);
  const tag = sealed.slice(sealed.length - 16);

  return [header, base64Url(wrappedKey), base64Url(iv), base64Url(ciphertext), base64Url(tag)].join('.');
}

/** Seals the named string properties of a request body under one challenge; other properties are copied as they are. */
export async function sealFields(
  body: Readonly<Record<string, unknown>>,
  fields: readonly string[],
  challenge: CredentialChallengeResponse,
  audience: string,
): Promise<Record<string, unknown>> {
  const result: Record<string, unknown> = { ...body };
  for (const field of fields) {
    const value = body[field];
    if (typeof value === 'string') {
      result[field] = await seal(challenge, field, value, audience);
    }
  }

  return result;
}
