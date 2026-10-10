import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';

import { type LookupOption, RESULT_RIGHT_FLAGS, type ResultRight, type ResultRightFlag } from '../../../core/api/organization.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { localizedPick } from '../../../shared/forms/localized-text';

export interface BranchOption {
  readonly id: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
}

const ALL_BRANCHES = '';

function scopeKey(groupId: string, branchId: string | null): string {
  return `${groupId}|${branchId ?? ALL_BRANCHES}`;
}

/** Any of review, verify, edit or print implies see; clearing see clears everything (mirrors INV-ORG-008). */
export function applyResultFlag(right: ResultRight, flag: ResultRightFlag, on: boolean): ResultRight {
  if (flag === 'canSee' && !on) {
    return { ...right, canSee: false, canReview: false, canVerify: false, canEdit: false, canPrint: false };
  }

  const next = { ...right, [flag]: on };
  return { ...next, canSee: next.canSee || next.canReview || next.canVerify || next.canEdit || next.canPrint };
}

function grantsAnything(right: ResultRight): boolean {
  return right.canSee || right.canReview || right.canVerify || right.canEdit || right.canPrint;
}

/**
 * Result rights of a role (SRS CP-37; LDM manual §12.4 `S R V E P`): one row per clinical group, five boxes, for the
 * scope chosen above the grid — every branch, or one branch. Rows combine on the server: a right on "every branch"
 * applies in each branch. Emits the complete list (rows that grant nothing are dropped).
 */
@Component({
  selector: 'app-result-rights-grid',
  template: `
    <div class="scope">
      <label>
        <span>{{ i18n.t('organization.results.scope') }}</span>
        <select class="inp" [value]="scope()" (change)="onScope($event)">
          <option value="">{{ i18n.t('organization.results.allBranches') }}</option>
          @for (branch of branches(); track branch.id) {
            <option [value]="branch.id">{{ branch.code }} · {{ name(branch) }}</option>
          }
        </select>
      </label>
      <span class="muted small">{{ i18n.tf('organization.results.configured', { count: value().length }) }}</span>
    </div>

    @if (groups().length === 0) {
      <p class="info-strip">{{ i18n.t('organization.results.noGroups') }}</p>
    } @else {
      <div class="grid-scroll">
        <table class="grid" [attr.aria-label]="i18n.t('organization.results.title')">
          <thead>
            <tr>
              <th scope="col" class="group-col">{{ i18n.t('organization.results.group') }}</th>
              @for (flag of flags; track flag) {
                <th scope="col">{{ i18n.t('organization.results.' + flag) }}</th>
              }
            </tr>
          </thead>
          <tbody>
            @for (group of groups(); track group.id) {
              <tr>
                <th scope="row" class="group-col">
                  @if (group.code) {
                    <span class="mono">{{ group.code }}</span>
                  }
                  {{ name(group) }}
                </th>
                @for (flag of flags; track flag) {
                  <td>
                    <input
                      type="checkbox"
                      [checked]="rightOf(group.id)[flag]"
                      [disabled]="disabled()"
                      [attr.aria-label]="name(group) + ': ' + i18n.t('organization.results.' + flag)"
                      (change)="onFlag(group.id, flag, $event)" />
                  </td>
                }
              </tr>
            }
          </tbody>
        </table>
      </div>
    }
  `,
  styles: `
    :host { display: block; }
    .scope { display: flex; flex-wrap: wrap; align-items: center; gap: var(--sp-3); margin-block-end: var(--sp-3); }
    .scope label { display: inline-flex; align-items: center; gap: var(--sp-2); font-size: var(--fs-sm); color: var(--ink-2); }
    .scope select { width: auto; min-width: 220px; }
    .grid-scroll { overflow-x: auto; border: 1px solid var(--line); border-radius: var(--r); }
    .grid { width: 100%; border-collapse: collapse; font-size: var(--fs-sm); }
    .grid th, .grid td { padding: var(--sp-2) var(--sp-3); border-block-end: 1px solid var(--line-soft); text-align: center; }
    .grid thead th { background: var(--surface-2); color: var(--ink-2); font-weight: 600; }
    .grid .group-col { text-align: start; min-width: 220px; }
    .grid .mono { margin-inline-end: var(--sp-2); color: var(--muted); }
    .grid tbody tr:hover { background: var(--row-hover); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ResultRightsGridComponent {
  protected readonly i18n = inject(I18nService);

  readonly groups = input.required<readonly LookupOption[]>();
  readonly branches = input.required<readonly BranchOption[]>();
  readonly value = input.required<readonly ResultRight[]>();
  readonly disabled = input(false);
  readonly valueChange = output<readonly ResultRight[]>();

  protected readonly flags = RESULT_RIGHT_FLAGS;

  /** The branch whose rows the grid shows; '' = every branch. */
  protected readonly scope = signal<string>(ALL_BRANCHES);

  private readonly byScope = computed(() => new Map(this.value().map((right) => [scopeKey(right.clinicalGroupId, right.branchId), right])));

  protected name(option: { readonly nameEn: string; readonly nameAr: string }): string {
    return localizedPick({ en: option.nameEn, ar: option.nameAr }, this.i18n.lang());
  }

  protected rightOf(groupId: string): ResultRight {
    const branchId = this.scope() === ALL_BRANCHES ? null : this.scope();
    return this.byScope().get(scopeKey(groupId, branchId)) ?? { clinicalGroupId: groupId, branchId, canSee: false, canReview: false, canVerify: false, canEdit: false, canPrint: false };
  }

  protected onScope(event: Event): void {
    this.scope.set((event.target as HTMLSelectElement).value);
  }

  protected onFlag(groupId: string, flag: ResultRightFlag, event: Event): void {
    const updated = applyResultFlag(this.rightOf(groupId), flag, (event.target as HTMLInputElement).checked);
    const key = scopeKey(updated.clinicalGroupId, updated.branchId);
    const others = this.value().filter((right) => scopeKey(right.clinicalGroupId, right.branchId) !== key);
    this.valueChange.emit(grantsAnything(updated) ? [...others, updated] : others);
  }
}
