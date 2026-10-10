import { Routes } from '@angular/router';

import { ParametersFacade } from './state/parameters.facade';
import { RoleEditorFacade, RolesFacade } from './state/roles.facade';
import { UserEditorFacade, UsersFacade } from './state/users.facade';

/**
 * Organization (CP-36 Branches, CP-37 Roles & privileges, CP-38 Users, CP-57 Parameters; SA-06 Roles and SA-07 Admin
 * users under the platform tenant). Mounted by the integrator at `/organization` (client portal) and at
 * `/admin/organization` (Super Admin portal); every path here is relative.
 */
export const ORGANIZATION_ROUTES: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'users' },
  {
    path: 'branches',
    loadComponent: () => import('./pages/branches.page').then((m) => m.BranchesPage),
  },
  {
    path: 'roles',
    providers: [RolesFacade],
    loadComponent: () => import('./pages/roles.page').then((m) => m.RolesPage),
  },
  {
    path: 'roles/new',
    providers: [RoleEditorFacade],
    loadComponent: () => import('./pages/role-editor.page').then((m) => m.RoleEditorPage),
  },
  {
    path: 'roles/:roleId',
    providers: [RoleEditorFacade],
    loadComponent: () => import('./pages/role-editor.page').then((m) => m.RoleEditorPage),
  },
  {
    path: 'users',
    providers: [UsersFacade],
    loadComponent: () => import('./pages/users.page').then((m) => m.UsersPage),
  },
  {
    path: 'users/new',
    providers: [UserEditorFacade],
    loadComponent: () => import('./pages/user-editor.page').then((m) => m.UserEditorPage),
  },
  {
    path: 'users/:userId',
    providers: [UserEditorFacade],
    loadComponent: () => import('./pages/user-editor.page').then((m) => m.UserEditorPage),
  },
  {
    path: 'parameters',
    providers: [ParametersFacade],
    loadComponent: () => import('./pages/parameters.page').then((m) => m.ParametersPage),
  },
];
