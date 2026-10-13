import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';

import type { PermissionModule } from '../../../core/api/organization.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { SearchBoxComponent } from '../../../shared/ui/search-box/search-box.component';

/** Ordinal order, as the server stores keys. */
export function sortKeys(keys: Iterable<string>): string[] {
  return [...keys].sort((left, right) => (left < right ? -1 : left > right ? 1 : 0));
}

/** `module.action`, as the catalogue spells it. */
export function permissionKey(module: string, action: string): string {
  return `${module}.${action}`;
}

/**
 * The screen-rights matrix of the Roles and Users pages (SRS CP-37 "per screen Query/Insert/Update/Delete", LDM manual
 * §12.3): modules × catalogue actions as checkboxes, a per-module "all" box, grant/revoke all over the filtered
 * modules, and a filter by module name. With a `baseline` (the role's keys on a user's overrides tab) the cells that
 * differ from it are marked as modified. Emits the full sorted key list on every change; the page decides when to save.
 */
@Component({
  selector: 'app-privilege-matrix',
  imports: [SearchBoxComponent],
  template: `
    <div class="matrix-toolbar">
      <app-search-box [placeholder]="i18n.t('organization.matrix.filter')" [label]="i18n.t('organization.matrix.filter')" (searchChange)="filter.set($event)" />
      <span class="muted small count">{{ i18n.tf('organization.matrix.count', { selected: selectedInMatrix(), total: totalKeys() }) }}</span>
      @if (!disabled()) {
        <span class="bulk">
          <button type="button" class="btn sm" (click)="setVisible(true)">{{ i18n.t('organization.matrix.grantAll') }}</button>
          <button type="button" class="btn sm ghost" (click)="setVisible(false)">{{ i18n.t('organization.matrix.revokeAll') }}</button>
        </span>
      }
    </div>

    <div class="matrix-scroll">
      <table class="matrix" [attr.aria-label]="caption()">
        <thead>
          <tr>
            <th scope="col" class="module-col">{{ i18n.t('organization.matrix.module') }}</th>
            <th scope="col" class="all-col">{{ i18n.t('organization.matrix.all') }}</th>
            @for (action of actions(); track action) {
              <th scope="col">{{ actionLabel(action) }}</th>
            }
          </tr>
        </thead>
        <tbody>
          @for (module of visible(); track module.key) {
            <tr>
              <th scope="row" class="module-col">
                <span>{{ moduleLabel(module) }}</span>
                @if (module.platformOnly) {
                  <span class="tag">{{ i18n.t('organization.matrix.platform') }}</span>
                }
              </th>
              <td class="all-col">
                <input
                  type="checkbox"
                  [checked]="rowState(module) === 'all'"
                  [indeterminate]="rowState(module) === 'some'"
                  [disabled]="disabled()"
                  [attr.aria-label]="i18n.tf('organization.matrix.allOf', { module: moduleLabel(module) })"
                  (change)="onRowChange(module, $event)" />
              </td>
              @for (action of actions(); track action) {
                <td [class.modified]="isModified(module.key, action)">
                  @if (module.actions.includes(action)) {
                    <input
                      type="checkbox"
                      [checked]="selected().has(key(module.key, action))"
                      [disabled]="disabled()"
                      [attr.aria-label]="moduleLabel(module) + ': ' + actionLabel(action)"
                      (change)="toggle(module.key, action)" />
                  } @else {
                    <span class="na" aria-hidden="true">—</span>
                  }
                </td>
              }
            </tr>
          } @empty {
            <tr>
              <td class="empty muted" [attr.colspan]="actions().length + 2">{{ i18n.t('organization.matrix.noModule') }}</td>
            </tr>
          }
        </tbody>
      </table>
    </div>
  `,
  styles: `
    :host { display: block; }
    .matrix-toolbar { display: flex; flex-wrap: wrap; align-items: center; gap: var(--sp-3); margin-block-end: var(--sp-3); }
    .matrix-toolbar app-search-box { flex: 1 1 220px; max-width: 360px; }
    .bulk { display: inline-flex; gap: var(--sp-2); margin-inline-start: auto; }
    .matrix-scroll { overflow-x: auto; border: 1px solid var(--line); border-radius: var(--r); }
    .matrix { width: 100%; border-collapse: collapse; font-size: var(--fs-sm); }
    .matrix th, .matrix td { padding: var(--sp-2) var(--sp-3); border-block-end: 1px solid var(--line-soft); text-align: center; white-space: nowrap; }
    .matrix thead th { background: var(--surface-2); color: var(--ink-2); font-weight: 600; position: sticky; top: 0; }
    .matrix .module-col { text-align: start; white-space: normal; min-width: 200px; }
    .matrix tbody tr:hover { background: var(--row-hover); }
    .matrix td.modified { background: var(--warn-soft); }
    .na { color: var(--muted); }
    .tag { margin-inline-start: var(--sp-2); font-size: var(--fs-xs); color: var(--violet); }
    .empty { text-align: center; padding: var(--sp-4); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PrivilegeMatrixComponent {
  protected readonly i18n = inject(I18nService);

  readonly modules = input.required<readonly PermissionModule[]>();
  readonly actions = input.required<readonly string[]>();
  readonly selected = input.required<ReadonlySet<string>>();
  /** The keys the selection is compared with (the role's, on a user's overrides); null = no comparison. */
  readonly baseline = input<ReadonlySet<string> | null>(null);
  readonly disabled = input(false);
  readonly caption = input('');

  readonly selectedChange = output<readonly string[]>();

  protected readonly filter = signal('');

  protected readonly visible = computed(() => {
    const term = this.filter().trim().toLowerCase();
    return term === '' ? this.modules() : this.modules().filter((module) => `${module.key} ${module.displayName} ${this.moduleLabel(module)}`.toLowerCase().includes(term));
  });

  protected readonly totalKeys = computed(() => this.modules().reduce((sum, module) => sum + module.actions.length, 0));

  /**
   * Validation F-07: the held keys the matrix shows. A role may hold keys of modules this matrix does not list (platform-only
   * modules, modules of a later release); they are kept on save but never counted, so the counter cannot exceed the total.
   */
  protected readonly selectedInMatrix = computed(() =>
    this.modules().reduce((sum, module) => sum + module.actions.filter((action) => this.selected().has(permissionKey(module.key, action))).length, 0));

  protected key(module: string, action: string): string {
    return permissionKey(module, action);
  }

  protected moduleLabel(module: PermissionModule): string {
    const key = `organization.module.${module.key}`;
    return this.i18n.has(key) ? this.i18n.t(key) : module.displayName;
  }

  protected actionLabel(action: string): string {
    const key = `organization.action.${action}`;
    return this.i18n.has(key) ? this.i18n.t(key) : action;
  }

  protected rowState(module: PermissionModule): 'all' | 'some' | 'none' {
    const held = module.actions.filter((action) => this.selected().has(permissionKey(module.key, action))).length;
    return held === 0 ? 'none' : held === module.actions.length ? 'all' : 'some';
  }

  protected isModified(module: string, action: string): boolean {
    const baseline = this.baseline();
    if (baseline === null) {
      return false;
    }

    const key = permissionKey(module, action);
    return baseline.has(key) !== this.selected().has(key);
  }

  protected toggle(module: string, action: string): void {
    const next = new Set(this.selected());
    const key = permissionKey(module, action);
    if (next.has(key)) {
      next.delete(key);
    } else {
      next.add(key);
    }

    this.emit(next);
  }

  protected onRowChange(module: PermissionModule, event: Event): void {
    this.setRow(module, (event.target as HTMLInputElement).checked);
  }

  protected setRow(module: PermissionModule, on: boolean): void {
    const next = new Set(this.selected());
    for (const action of module.actions) {
      if (on) {
        next.add(permissionKey(module.key, action));
      } else {
        next.delete(permissionKey(module.key, action));
      }
    }

    this.emit(next);
  }

  /** Grant or revoke every action of the modules the filter shows. */
  protected setVisible(on: boolean): void {
    const next = new Set(this.selected());
    for (const module of this.visible()) {
      for (const action of module.actions) {
        if (on) {
          next.add(permissionKey(module.key, action));
        } else {
          next.delete(permissionKey(module.key, action));
        }
      }
    }

    this.emit(next);
  }

  private emit(keys: ReadonlySet<string>): void {
    this.selectedChange.emit(sortKeys(keys));
  }
}
