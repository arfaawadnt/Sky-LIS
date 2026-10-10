import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { PortalService } from '../portal/portal.service';

/**
 * Portal guards (01 §6.5). Affordance only, like `authGuard`: the server scopes every query to the session's
 * tenant regardless (RLS, D-04). `platformGuard` keeps `/admin/**` for the platform tenant; `tenantGuard` keeps a
 * client page for laboratory tenants. Each sends the other portal to its own home instead of a dead end.
 */
export const platformGuard: CanActivateFn = () => {
  const portal = inject(PortalService);
  return portal.portal() === 'admin' ? true : inject(Router).createUrlTree(['/']);
};

export const tenantGuard: CanActivateFn = () => {
  const portal = inject(PortalService);
  return portal.portal() === 'client' ? true : inject(Router).createUrlTree(['/admin']);
};
