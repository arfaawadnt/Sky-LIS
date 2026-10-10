// @vitest-environment node
import { credentialFieldsFor } from './credential-fields';

describe('credential field map (D-03)', () => {
  it('matches exact paths, ignoring origin and query', () => {
    expect(credentialFieldsFor('post', 'http://127.0.0.1:4300/api/v1/auth/sign-in?x=1')).toEqual(['password']);
    expect(credentialFieldsFor('POST', '/api/v1/organization/users')).toEqual(['password']);
  });

  it('matches id-bearing paths through their pattern only', () => {
    const id = '01a12231-21c2-7f12-ae58-0481b29b8754';
    expect(credentialFieldsFor('POST', `/api/v1/organization/users/${id}/reset-password`)).toEqual(['temporaryPassword']);
    expect(credentialFieldsFor('POST', `/api/v1/organization/users/${id}/reset-password/extra`)).toEqual([]);
    expect(credentialFieldsFor('POST', '/api/v1/organization/users/not-an-id/reset-password')).toEqual([]);
  });

  it('seals nothing on other methods or paths', () => {
    expect(credentialFieldsFor('GET', '/api/v1/organization/users')).toEqual([]);
    expect(credentialFieldsFor('POST', '/api/v1/organization/users/import')).toEqual([]);
  });
});
