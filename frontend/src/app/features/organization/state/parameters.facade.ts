import { Injectable, computed, inject, resource, signal } from '@angular/core';

import type { SettingValueItem, TenantSettingItem } from '../../../core/api/settings.contracts';
import { mutation } from '../../../core/state/mutation';
import { OrganizationApi } from '../data/organization.api';

/**
 * Parameters (CP-57) over the foundation settings API: the effective values, the unsaved edits by key, and one save
 * that sends only what changed (`PUT /api/v1/settings` validates every value: SET-001, SET-002) and adopts the
 * effective list the server returns.
 */
@Injectable()
export class ParametersFacade {
  private readonly api = inject(OrganizationApi);

  readonly settings = resource({ loader: ({ abortSignal }) => this.api.settings(abortSignal) });

  /** Unsaved values by key, in the stored text form. */
  readonly edits = signal<ReadonlyMap<string, string>>(new Map());

  readonly byKey = computed(() => new Map<string, TenantSettingItem>((this.settings.hasValue() ? this.settings.value().settings : []).map((item) => [item.key, item])));

  readonly dirty = computed(() => this.changes().length > 0);

  readonly save = mutation((changes: readonly SettingValueItem[]) => this.api.saveSettings(changes), {
    then: (response) => {
      this.settings.set(response);
      this.edits.set(new Map());
    },
  });

  /** The value shown for a key: the unsaved edit, else the effective value. */
  valueOf(key: string): string {
    return this.edits().get(key) ?? this.byKey().get(key)?.value ?? '';
  }

  set(key: string, value: string): void {
    this.edits.update((current) => {
      const next = new Map(current);
      if (this.byKey().get(key)?.value === value) {
        next.delete(key);
      } else {
        next.set(key, value);
      }

      return next;
    });
  }

  /** Back to the platform default (saved with the next Save). */
  resetToDefault(key: string): void {
    const item = this.byKey().get(key);
    if (item) {
      this.set(key, item.defaultValue);
    }
  }

  discard(): void {
    this.edits.set(new Map());
  }

  changes(): readonly SettingValueItem[] {
    return [...this.edits()].map(([key, value]) => ({ key, value }));
  }
}
