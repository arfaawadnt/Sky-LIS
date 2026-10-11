import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { ToastService } from '../../shared/ui/toast/toast.service';
import { I18nService } from '../i18n/i18n.service';
import { PortalService } from '../portal/portal.service';

/** The same notice as the navigation registry guard (shell.pageNotAvailable), shown when a portal guard redirects. */
function notifyRefusal(): void {
  const i18n = inject(I18nService);
  const toast = inject(ToastService);
  void i18n.load('shell').then(() => toast.warn(i18n.t('shell.pageNotAvailable')));
}

/**
 * Portal guards (01 §6.5). Affordance only, like `authGuard`: the server scopes every query to the session's
 * tenant regardless (RLS, D-04). `platformGuard` keeps `/admin/**` for the platform tenant; `tenantGuard` keeps a
 * client page for laboratory tenants. Each sends the other portal to its own home instead of a dead end.
 */
export const platformGuard: CanActivateFn = () => {
  const portal = inject(PortalService);
  if (portal.portal() === 'admin') {
    return true;
  }

  notifyRefusal();
  return inject(Router).createUrlTree(['/']);
};

export const tenantGuard: CanActivateFn = () => {
  const portal = inject(PortalService);
  if (portal.portal() === 'client') {
    return true;
  }

  notifyRefusal();
  return inject(Router).createUrlTree(['/admin']);
};
