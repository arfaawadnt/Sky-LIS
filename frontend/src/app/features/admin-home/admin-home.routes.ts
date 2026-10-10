import { Routes } from '@angular/router';

export const ADMIN_HOME_ROUTES: Routes = [{ path: '', loadComponent: () => import('./pages/admin-home.page').then((m) => m.AdminHomePage) }];
