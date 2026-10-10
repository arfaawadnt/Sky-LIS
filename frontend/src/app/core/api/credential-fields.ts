/**
 * Which properties of which requests are `[CredentialField]` (D-03). The envelope interceptor reads this map; the
 * server's filter opens exactly these. Hand-maintained until the generator emits it from the OpenAPI document
 * (01 §6.4); the server-side gate CredentialFieldTests keeps the attribute complete on its side.
 */
interface CredentialEndpoint {
  readonly method: string;
  /** An exact path, or a pattern for paths that carry ids. */
  readonly path: string | RegExp;
  readonly fields: readonly string[];
}

const CREDENTIAL_ENDPOINTS: readonly CredentialEndpoint[] = [
  { method: 'POST', path: '/api/v1/auth/sign-in', fields: ['password'] },
  { method: 'POST', path: '/api/v1/auth/reauthenticate', fields: ['password'] },
  { method: 'POST', path: '/api/v1/auth/recovery/complete', fields: ['newPassword'] },
  // Platform administration (SA-04) and the subscription wizard (CP-02)
  { method: 'POST', path: '/api/v1/platform/clients', fields: ['adminPassword'] },
  { method: 'POST', path: /^\/api\/v1\/platform\/clients\/[0-9a-fA-F-]{36}\/reset-admin-password$/, fields: ['newPassword'] },
  { method: 'POST', path: '/api/v1/public/subscriptions/complete', fields: ['password'] },
  // Account (CP-65): both fields share the request's challenge
  { method: 'POST', path: '/api/v1/account/password', fields: ['currentPassword', 'newPassword'] },
  // Organization (CP-38 users)
  { method: 'POST', path: '/api/v1/organization/users', fields: ['password'] },
  { method: 'POST', path: /^\/api\/v1\/organization\/users\/[0-9a-fA-F-]{36}\/reset-password$/, fields: ['temporaryPassword'] },
];

/** The path of a request URL without origin or query, as the server sees it. */
export function pathOf(url: string): string {
  const withoutQuery = url.split('?')[0] ?? url;
  if (withoutQuery.startsWith('/')) {
    return withoutQuery;
  }

  try {
    return new URL(withoutQuery).pathname;
  } catch {
    return withoutQuery;
  }
}

export function credentialFieldsFor(method: string, url: string): readonly string[] {
  const path = pathOf(url);
  const upper = method.toUpperCase();
  return CREDENTIAL_ENDPOINTS.find((endpoint) => endpoint.method === upper && (typeof endpoint.path === 'string' ? endpoint.path === path : endpoint.path.test(path)))?.fields ?? [];
}
