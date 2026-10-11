import { routes } from './app.routes';
import { tenantGuard } from './core/auth/portal.guards';
import { navRegistryGuard } from './shell/nav.guard';

describe('app routes (A-08, C-14, C-15)', () => {
  const shell = routes.find((route) => route.path === '' && route.children !== undefined);

  it('guards every signed-in page of both portals with the navigation registry', () => {
    expect(shell?.canActivateChild?.[0]).toBe(navRegistryGuard);
  });

  it('keeps the client account pages away from a platform session', () => {
    const account = shell?.children?.find((route) => route.path === 'account');
    expect(account?.canActivate).toContain(tenantGuard);
  });
});
