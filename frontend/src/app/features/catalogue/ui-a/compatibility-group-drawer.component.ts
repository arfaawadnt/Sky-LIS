import { ChangeDetectionStrategy, Component, computed, effect, inject, input, model, output, signal, untracked } from '@angular/core';

import type { SampleConditionView } from '../../../core/api/catalogue-a.contracts';
import type { ProblemDetails } from '../../../core/api/problem-details';
import { SAMPLE_COMPATIBILITY_LIMITS, type CreateSampleCompatibilityGroupRequest, type SampleCompatibilityGroupView } from '../../../core/api/sample-compatibility.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { localizedPick } from '../../../shared/forms/localized-text';
import { problemMessage } from '../../../shared/forms/problem-text';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { DrawerComponent } from '../../../shared/ui/drawer/drawer.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { type CatalogueOption, optionLabel } from '../state-a/catalogue-a-options';
import { type CompatibilityMemberDraft, checkCompatibilityMembers, moveMember } from './compatibility-rules';

interface MemberRow extends CompatibilityMemberDraft {
  readonly key: number;
}

/**
 * The editor of a compatibility group of samples (LIS-CR-015 M4): code (immutable once saved), names and the ordered
 * members — a sample type with one of its conditions or "No condition", moved up / down, removed, added. The rules are
 * checked before saving (the server checks them again). Emits the request; the page saves it. F10 saves.
 */
@Component({
  selector: 'app-compatibility-group-drawer',
  imports: [DrawerComponent, IconComponent, SaveShortcutDirective],
  template: `
    <app-drawer [(open)]="open" [title]="title()" size="lg" (closed)="onClosed()">
      <form appSaveShortcut (appSaveShortcut)="submit()" (submit)="$event.preventDefault(); submit()" novalidate>
        <p class="note small">{{ i18n.t('catalogue-a.compatible.note') }}</p>
        @if (problem(); as current) {
          <p class="problem" role="alert">{{ problemText(current) }}</p>
        }
        @if (attempted() && !valid()) {
          <p class="problem" role="alert">{{ i18n.t('catalogue-a.compatible.fixRules') }}</p>
        }
        <div class="fields">
          <label class="field">
            <span>{{ i18n.t('catalogue-a.compatible.field.code') }}</span>
            <input class="inp mono" name="code" [value]="code()" (input)="code.set(text($event).toUpperCase())" [disabled]="group() !== null" [maxLength]="limits.codeLength" autocomplete="off" spellcheck="false" [class.invalid]="attempted() && !codeValid()" />
          </label>
          <label class="field">
            <span>{{ i18n.t('catalogue-a.compatible.field.nameEn') }}</span>
            <input class="inp" name="nameEn" dir="ltr" [value]="nameEn()" (input)="nameEn.set(text($event))" maxlength="200" [class.invalid]="attempted() && nameEn().trim() === ''" />
          </label>
          <label class="field">
            <span>{{ i18n.t('catalogue-a.compatible.field.nameAr') }}</span>
            <input class="inp" name="nameAr" dir="rtl" lang="ar" [value]="nameAr()" (input)="nameAr.set(text($event))" maxlength="200" [class.invalid]="attempted() && nameAr().trim() === ''" />
          </label>
        </div>

        <table class="grid">
          <thead>
            <tr>
              <th scope="col" class="num">#</th>
              <th scope="col">{{ i18n.t('catalogue-a.compatible.column.sampleType') }}</th>
              <th scope="col">{{ i18n.t('catalogue-a.compatible.column.condition') }}</th>
              <th scope="col"><span class="visually-hidden">{{ i18n.t('catalogue-a.actions') }}</span></th>
            </tr>
          </thead>
          <tbody>
            @for (row of rows(); track row.key; let i = $index, first = $first, last = $last) {
              <tr [attr.data-member]="i + 1">
                <td class="num mono">{{ i + 1 }}</td>
                <td>
                  <select class="inp sm type" [attr.aria-label]="i18n.tf('catalogue-a.compatible.typeOf', { n: i + 1 })" [class.invalid]="attempted() && check().rows.get(i) === 'required'" (change)="chooseType(i, $event)">
                    <option value="" [selected]="row.sampleTypeId === null">{{ i18n.t('catalogue-a.compatible.chooseType') }}</option>
                    @for (option of typeOptions(row.sampleTypeId); track option.value) {
                      <option [value]="option.value" [selected]="option.value === row.sampleTypeId">{{ label(option) }}</option>
                    }
                  </select>
                </td>
                <td>
                  <select class="inp sm condition" [attr.aria-label]="i18n.tf('catalogue-a.compatible.conditionOf', { n: i + 1 })" [disabled]="row.sampleTypeId === null" [class.invalid]="attempted() && check().rows.get(i) === 'CAT-283'" (change)="chooseCondition(i, $event)">
                    <option value="" [selected]="row.sampleConditionId === null">{{ i18n.t('catalogue-a.compatible.noCondition') }}</option>
                    @for (condition of conditionOptions(row); track condition.id) {
                      <option [value]="condition.id" [selected]="condition.id === row.sampleConditionId">{{ condition.code }} · {{ conditionName(condition) }}</option>
                    }
                  </select>
                  @if (attempted() && check().rows.get(i); as rule) {
                    <span class="rule small" role="alert">{{ i18n.t('catalogue-a.compatible.rule.' + rule) }}</span>
                  }
                </td>
                <td class="actions">
                  <button type="button" class="row-btn up" [disabled]="first" (click)="move(i, -1)" [attr.aria-label]="i18n.t('catalogue-a.moveUp')" [title]="i18n.t('catalogue-a.moveUp')"><app-icon name="arrow-up" [size]="14" /></button>
                  <button type="button" class="row-btn down" [disabled]="last" (click)="move(i, 1)" [attr.aria-label]="i18n.t('catalogue-a.moveDown')" [title]="i18n.t('catalogue-a.moveDown')"><app-icon name="arrow-down" [size]="14" /></button>
                  <button type="button" class="row-btn danger remove" (click)="remove(i)" [attr.aria-label]="i18n.t('catalogue-a.remove')" [title]="i18n.t('catalogue-a.remove')"><app-icon name="x" [size]="14" /></button>
                </td>
              </tr>
            }
          </tbody>
        </table>
        @if (attempted() && check().list; as rule) {
          <p class="rule small" role="alert">{{ i18n.t('catalogue-a.compatible.rule.' + rule) }}</p>
        }
        <button type="button" class="btn sm soft add" [disabled]="rows().length >= limits.maxMembers" (click)="add()"><app-icon name="plus" [size]="14" />{{ i18n.t('catalogue-a.compatible.addMember') }}</button>
      </form>
      <ng-container drawerFooter>
        <button type="button" class="btn" (click)="open.set(false)">{{ i18n.t('catalogue-a.cancel') }}</button>
        <button type="button" class="btn primary" [disabled]="saving()" (click)="submit()">{{ saving() ? i18n.t('catalogue-a.saving') : i18n.t('catalogue-a.save') }}</button>
      </ng-container>
    </app-drawer>
  `,
  styles: `
    .note { padding: var(--sp-2) var(--sp-3); border-inline-start: 3px solid var(--line); background: var(--surface-2, transparent); margin-block-end: var(--sp-3); }
    .fields { display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: var(--sp-3); }
    .field { display: grid; gap: 4px; font-size: var(--fs-sm); color: var(--ink-2); }
    .grid { width: 100%; border-collapse: collapse; margin-block: var(--sp-3); }
    .grid th { text-align: start; font-size: var(--fs-sm); color: var(--ink-2); padding: 4px 6px; border-bottom: 1px solid var(--line); }
    .grid td { padding: 4px 6px; vertical-align: middle; }
    .grid select { width: 100%; }
    .num { width: 32px; }
    .actions { white-space: nowrap; text-align: end; }
    .rule { display: block; color: var(--bad); margin-top: 2px; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CompatibilityGroupDrawerComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly limits = SAMPLE_COMPATIBILITY_LIMITS;

  readonly open = model(false);
  /** The group edited; null = a new group. */
  readonly group = input<SampleCompatibilityGroupView | null>(null);
  readonly sampleTypes = input<readonly CatalogueOption[]>([]);
  readonly conditions = input<ReadonlyMap<string, readonly SampleConditionView[]>>(new Map());
  readonly saving = input(false);
  readonly problem = input<ProblemDetails | null>(null);
  readonly save = output<CreateSampleCompatibilityGroupRequest>();
  readonly closed = output<void>();
  /** Asks the page for the conditions of a sample type the editor shows. */
  readonly needConditions = output<string>();

  protected readonly code = signal('');
  protected readonly nameEn = signal('');
  protected readonly nameAr = signal('');
  protected readonly rows = signal<readonly MemberRow[]>([]);
  protected readonly attempted = signal(false);
  private nextKey = 0;

  protected readonly check = computed(() => checkCompatibilityMembers(this.rows()));
  protected readonly codeValid = computed(() => /^[A-Z0-9][A-Z0-9._-]*$/.test(this.code()) && this.code().length <= SAMPLE_COMPATIBILITY_LIMITS.codeLength);
  protected readonly valid = computed(() => this.codeValid() && this.nameEn().trim() !== '' && this.nameAr().trim() !== '' && this.check().list === null && this.check().rows.size === 0);

  protected readonly title = computed(() => {
    const group = this.group();
    return group === null
      ? this.i18n.t('catalogue-a.compatible.add')
      : `${this.i18n.t('catalogue-a.compatible.editTitle')} · ${group.code} · ${localizedPick({ en: group.nameEn, ar: group.nameAr }, this.i18n.lang())}`;
  });

  constructor() {
    // a new group, or another one, resets the editor
    effect(() => {
      const group = this.group();
      const open = this.open();
      untracked(() => {
        if (open) {
          this.reset(group);
        }
      });
    });
  }

  protected text(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected label(option: CatalogueOption): string {
    return optionLabel(option, this.i18n.lang());
  }

  protected conditionName(condition: SampleConditionView): string {
    return localizedPick({ en: condition.nameEn, ar: condition.nameAr }, this.i18n.lang());
  }

  /** Active sample types, plus the row's own (a kept member whose type was deactivated since). */
  protected typeOptions(current: string | null): readonly CatalogueOption[] {
    return this.sampleTypes().filter((option) => option.isActive || option.value === current);
  }

  /** The active conditions of the row's type, plus its own. */
  protected conditionOptions(row: MemberRow): readonly SampleConditionView[] {
    if (row.sampleTypeId === null) {
      return [];
    }

    return (this.conditions().get(row.sampleTypeId) ?? []).filter((condition) => condition.isActive || condition.id === row.sampleConditionId);
  }

  protected chooseType(index: number, event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    const sampleTypeId = value === '' ? null : value;
    this.patch(index, { sampleTypeId, sampleConditionId: null });
    if (sampleTypeId !== null) {
      this.needConditions.emit(sampleTypeId);
    }
  }

  protected chooseCondition(index: number, event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.patch(index, { sampleConditionId: value === '' ? null : value });
  }

  protected move(index: number, delta: -1 | 1): void {
    this.rows.update((rows) => moveMember(rows, index, delta));
  }

  protected remove(index: number): void {
    this.rows.update((rows) => rows.filter((_, position) => position !== index));
  }

  protected add(): void {
    this.rows.update((rows) => [...rows, { key: this.nextKey++, sampleTypeId: null, sampleConditionId: null }]);
  }

  protected submit(): void {
    this.attempted.set(true);
    if (this.saving() || !this.valid()) {
      return;
    }

    this.save.emit({
      code: this.code().trim(),
      nameEn: this.nameEn().trim(),
      nameAr: this.nameAr().trim(),
      members: this.rows().map((row) => ({ sampleTypeId: row.sampleTypeId ?? '', sampleConditionId: row.sampleConditionId })),
    });
  }

  protected problemText(problem: ProblemDetails): string {
    return problemMessage(this.i18n, 'catalogue-a.compatible', problem);
  }

  protected onClosed(): void {
    this.attempted.set(false);
    this.closed.emit();
  }

  private patch(index: number, change: Partial<CompatibilityMemberDraft>): void {
    this.rows.update((rows) => rows.map((row, position) => (position === index ? { ...row, ...change } : row)));
  }

  private reset(group: SampleCompatibilityGroupView | null): void {
    this.attempted.set(false);
    this.code.set(group?.code ?? '');
    this.nameEn.set(group?.nameEn ?? '');
    this.nameAr.set(group?.nameAr ?? '');
    const members = group?.members ?? [];
    this.rows.set(
      members.length > 0
        ? members.map((member) => ({ key: this.nextKey++, sampleTypeId: member.sampleTypeId, sampleConditionId: member.sampleConditionId }))
        : [
            { key: this.nextKey++, sampleTypeId: null, sampleConditionId: null },
            { key: this.nextKey++, sampleTypeId: null, sampleConditionId: null },
          ],
    );
    for (const typeId of new Set(members.map((member) => member.sampleTypeId))) {
      this.needConditions.emit(typeId);
    }
  }
}
