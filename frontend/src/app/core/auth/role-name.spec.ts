import { roleNameFor } from './role-name';

describe('roleNameFor (validation v0.5.0 B-09)', () => {
  const profile = { roleName: 'Laboratory Administrator', roleNameAr: 'مدير المختبر' };

  it('names the role in the language of the page', () => {
    expect(roleNameFor(profile, 'en')).toBe('Laboratory Administrator');
    expect(roleNameFor(profile, 'ar')).toBe('مدير المختبر');
  });

  it('falls back to the English name when the server sent no Arabic one', () => {
    expect(roleNameFor({ roleName: 'Cashier' }, 'ar')).toBe('Cashier');
    expect(roleNameFor({ roleName: 'Cashier', roleNameAr: null }, 'ar')).toBe('Cashier');
    expect(roleNameFor({ roleName: 'Cashier', roleNameAr: '  ' }, 'ar')).toBe('Cashier');
  });

  it('is null without a profile or a role', () => {
    expect(roleNameFor(null, 'ar')).toBeNull();
    expect(roleNameFor(undefined, 'en')).toBeNull();
    expect(roleNameFor({ roleName: null, roleNameAr: null }, 'ar')).toBeNull();
  });
});
