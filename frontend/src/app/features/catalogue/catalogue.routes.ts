import { Routes } from '@angular/router';

import { CATALOGUE_A_ROUTES } from './catalogue-a.routes';
import { CATALOGUE_B_ROUTES } from './catalogue-b.routes';

/** The catalogue feature (client Setup pages and the admin master-data pages SA-12/13/14) = both halves. */
export const CATALOGUE_ROUTES: Routes = [...CATALOGUE_A_ROUTES, ...CATALOGUE_B_ROUTES];
