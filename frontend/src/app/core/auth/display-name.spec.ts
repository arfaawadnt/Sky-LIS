import { displayNameFor } from './display-name';

describe('displayNameFor (validation v0.5.0 R-12)', () => {
  const profile = { displayName: 'UAT Reception Clerk', displayNameEn: 'UAT Reception Clerk', displayNameAr: 'موظف استقبال' };

  it('names the user in the language of the page', () => {
    expect(displayNameFor(profile, 'en')).toBe('UAT Reception Clerk');
    expect(displayNameFor(profile, 'ar')).toBe('موظف استقبال');
  });

  it('shows the English name in English although the preferred language made displayName Arabic', () => {
    expect(displayNameFor({ displayName: 'موظف استقبال', displayNameEn: 'UAT Reception Clerk', displayNameAr: 'موظف استقبال' }, 'en')).toBe('UAT Reception Clerk');
  });

  it('falls back to displayName when the server sent no name of the language', () => {
    expect(displayNameFor({ displayName: 'Platform Admin' }, 'ar')).toBe('Platform Admin');
    expect(displayNameFor({ displayName: 'Platform Admin', displayNameAr: null, displayNameEn: null }, 'en')).toBe('Platform Admin');
    expect(displayNameFor({ displayName: 'Platform Admin', displayNameAr: '  ' }, 'ar')).toBe('Platform Admin');
  });

  it('is empty without a profile', () => {
    expect(displayNameFor(null, 'ar')).toBe('');
    expect(displayNameFor(undefined, 'en')).toBe('');
  });
});
