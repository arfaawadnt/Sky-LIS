import { InjectionToken, inject } from '@angular/core';

import { AuthStore } from '../../../core/auth/auth.store';

/**
 * LIS-CR-014 T2: where `<app-data-table [listKey]="…">` reads and saves the user's column choice. `hidden` is read inside
 * a computed (reactive); `save` resolves when stored and rejects when refused. The default is the signed-in member's
 * profile (`AuthProfile.listPreferences`, saved with `AuthStore.updateListPreferences`, optimistic), so the choice follows
 * the user to every device. Specs provide a fake. The table asks for it only when it has a `listKey`.
 */
export interface TablePreferences {
  hidden(listKey: string): readonly string[];
  save(listKey: string, hidden: readonly string[]): Promise<void>;
}

export const TABLE_PREFERENCES = new InjectionToken<TablePreferences>('TABLE_PREFERENCES', {
  providedIn: 'root',
  factory: (): TablePreferences => {
    const auth = inject(AuthStore);
    return {
      hidden: (listKey) => auth.profile()?.listPreferences?.[listKey]?.hidden ?? [],
      save: (listKey, hidden) => auth.updateListPreferences(listKey, hidden),
    };
  },
});
