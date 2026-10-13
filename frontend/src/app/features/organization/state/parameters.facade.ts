import { Injectable, computed, inject, resource, signal } from '@angular/core';

import type { SettingValueItem, TenantSettingItem } from '../../../core/api/settings.contracts';
import { mutation } from '../../../core/state/mutation';
import { OrganizationApi } from '../data/organization.api';

/**
 * Parameters (CP-57) over the foundation settings API: the effective values, the unsaved edits by key, and one save
 * that sends only what changed (`PUT /api/v1/settings` validates every value: SET-001, SET-002) and adopts the
 * effective list the server returns. "Use default" removes the laboratory's own value (an edit of `null`, validation F-13),
 * so the key follows the platform value again, also a later one.
 */
@Injectable()
export class ParametersFacade {
  private readonly api = inject(OrganizationApi);

  readonly settings = resource({ loader: ({ abortSignal }) => this.api.settings(abortSignal) });

  /** Unsaved values by key, in the stored text form; `null` = remove the laboratory's own value (F-13). */
  readonly edits = signal<ReadonlyMap<string, string | null>>(new Map());

  readonly byKey = computed(() => new Map<string, TenantSettingItem>((this.settings.hasValue() ? this.settings.value().settings : []).map((item) => [item.key, item])));

  readonly dirty = computed(() => this.changes().length > 0);

  readonly save = mutation((changes: readonly SettingValueItem[]) => this.api.saveSettings(changes), {
    then: (response) => {
      this.settings.set(response);
      this.edits.set(new Map());
    },
  });

  /** The value shown for a key: the unsaved edit (a removal shows the inherited value), else the effective value. */
  valueOf(key: string): string {
    const item = this.byKey().get(key);
    const edit = this.edits().get(key);
    if (edit === null) {
      return item?.inheritedValue ?? item?.defaultValue ?? '';
    }

    return edit ?? item?.value ?? '';
  }

  /** True while the laboratory's own value of the key is to be removed with the next save. */
  isReset(key: string): boolean {
    return this.edits().get(key) === null;
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

  /**
   * "Use default": back to what the laboratory inherits, the platform value when the platform sets one, else the default
   * (UAT C-09). The laboratory's own row is removed with the next Save (validation F-13), not overwritten with the inherited
   * value, so a later platform change reaches the laboratory. A key without a value of its own only drops its edit.
   */
  resetToDefault(key: string): void {
    const item = this.byKey().get(key);
    if (!item) {
      return;
    }

    this.edits.update((current) => {
      const next = new Map(current);
      if (item.isDefault) {
        next.delete(key);
      } else {
        next.set(key, null);
      }

      return next;
    });
  }

  discard(): void {
    this.edits.set(new Map());
  }

  changes(): readonly SettingValueItem[] {
    return [...this.edits()].map(([key, value]) => ({ key, value }));
  }

  /** Re-reads the parameters when a page opens again: the facade outlives the page (route provider), so the previous visit's answer — even another sign-in's — must never stay on screen (UAT D-14). A load in flight is left alone. */
  refresh(): void {
    this.settings.reload();
  }
}
