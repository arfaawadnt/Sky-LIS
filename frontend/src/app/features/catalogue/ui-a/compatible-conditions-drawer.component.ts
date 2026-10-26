import { ChangeDetectionStrategy, Component, computed, inject, input, linkedSignal, model, output, signal } from '@angular/core';

import type { CompatibleConditionView, CompatibleSampleTypeView, SampleConditionPairItem } from '../../../core/api/compatible-conditions.contracts';
import type { ProblemDetails } from '../../../core/api/problem-details';
import { I18nService } from '../../../core/i18n/i18n.service';
import { localizedPick } from '../../../shared/forms/localized-text';
import { problemMessage } from '../../../shared/forms/problem-text';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { DrawerComponent } from '../../../shared/ui/drawer/drawer.component';
import { type CompatibleRuleBreach, compatibleBreaches, targetChoices } from './compatible-rules';

/**
 * The compatible conditions of one sample type (LIS-CR-015 M4): per condition, "Its own sample" or another condition of the
 * type whose tube its tests join when that tube is in the same visit. The rules are checked before saving (the server
 * checks them again); emits the full map, the page saves it. F10 saves.
 */
@Component({
  selector: 'app-compatible-conditions-drawer',
  imports: [DrawerComponent, SaveShortcutDirective],
  template: `
    <app-drawer [(open)]="open" [title]="title()" size="lg" (closed)="onClosed()">
      <form appSaveShortcut (appSaveShortcut)="submit()" (submit)="$event.preventDefault(); submit()" novalidate>
        <p class="muted small">{{ i18n.t('catalogue-a.compatible.drawerHint') }}</p>
        @if (problem(); as current) {
          <p class="problem" role="alert">{{ problemText(current) }}</p>
        }
        @if (attempted() && breaches().length > 0) {
          <p class="problem" role="alert">{{ i18n.t('catalogue-a.compatible.fixRules') }}</p>
        }
        <table class="grid">
          <thead>
            <tr>
              <th scope="col">{{ i18n.t('catalogue-a.compatible.column.condition') }}</th>
              <th scope="col">{{ i18n.t('catalogue-a.compatible.column.mergesInto') }}</th>
            </tr>
          </thead>
          <tbody>
            @for (condition of conditions(); track condition.id) {
              <tr [attr.data-condition]="condition.code">
                <td>
                  <span class="mono">{{ condition.code }}</span> · {{ name(condition) }}
                  @if (!condition.isActive) {
                    <span class="muted small"> ({{ i18n.t('catalogue-a.compatible.inactive') }})</span>
                  }
                </td>
                <td>
                  <select class="inp sm" [attr.aria-label]="i18n.tf('catalogue-a.compatible.selectLabel', { code: condition.code })" [class.invalid]="breachOf(condition.id) !== null" (change)="choose(condition.id, $event)">
                    <option value="" [selected]="targetOf(condition.id) === null">{{ i18n.t('catalogue-a.compatible.ownSample') }}</option>
                    @for (choice of choicesOf(condition.id); track choice.id) {
                      <option [value]="choice.id" [selected]="targetOf(condition.id) === choice.id">{{ choice.code }} · {{ name(choice) }}</option>
                    }
                  </select>
                  @if (breachOf(condition.id); as breach) {
                    <span class="rule small" role="alert">{{ i18n.t('catalogue-a.compatible.rule.' + breach.code) }}</span>
                  }
                </td>
              </tr>
            } @empty {
              <tr><td colspan="2" class="muted">{{ i18n.t('catalogue-a.compatible.noConditions') }}</td></tr>
            }
          </tbody>
        </table>
      </form>
      <ng-container drawerFooter>
        <button type="button" class="btn" (click)="open.set(false)">{{ i18n.t('catalogue-a.cancel') }}</button>
        <button type="button" class="btn primary" [disabled]="saving() || conditions().length === 0" (click)="submit()">{{ saving() ? i18n.t('catalogue-a.saving') : i18n.t('catalogue-a.save') }}</button>
      </ng-container>
    </app-drawer>
  `,
  styles: `
    .grid { width: 100%; border-collapse: collapse; margin-block: var(--sp-3); }
    .grid th { text-align: start; font-size: var(--fs-sm); color: var(--ink-2); padding: 4px 6px; border-bottom: 1px solid var(--line); }
    .grid td { padding: 6px; vertical-align: middle; }
    .grid select { width: 100%; }
    .rule { display: block; color: var(--bad); margin-top: 2px; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CompatibleConditionsDrawerComponent {
  protected readonly i18n = inject(I18nService);

  readonly open = model(false);
  readonly sampleType = input<CompatibleSampleTypeView | null>(null);
  readonly saving = input(false);
  readonly problem = input<ProblemDetails | null>(null);
  readonly save = output<readonly SampleConditionPairItem[]>();
  readonly closed = output<void>();

  protected readonly conditions = computed<readonly CompatibleConditionView[]>(() => this.sampleType()?.conditions ?? []);

  /** The map being edited: every condition of the type with its target (null = its own sample); reset when another type opens. */
  protected readonly map = linkedSignal<ReadonlyMap<string, string | null>>(() => new Map(this.conditions().map((condition) => [condition.id, condition.mergesIntoConditionId])));

  protected readonly breaches = computed(() => compatibleBreaches(this.conditions(), this.map()));

  protected readonly attempted = signal(false);

  protected readonly title = computed(() => {
    const type = this.sampleType();
    const label = this.i18n.t('catalogue-a.compatible.drawerTitle');
    return type === null ? label : `${label} · ${type.code} · ${localizedPick({ en: type.nameEn, ar: type.nameAr }, this.i18n.lang())}`;
  });

  protected name(condition: CompatibleConditionView): string {
    return localizedPick({ en: condition.nameEn, ar: condition.nameAr }, this.i18n.lang());
  }

  protected targetOf(conditionId: string): string | null {
    return this.map().get(conditionId) ?? null;
  }

  protected choicesOf(conditionId: string): readonly CompatibleConditionView[] {
    return targetChoices(this.conditions(), conditionId, this.map());
  }

  protected breachOf(conditionId: string): CompatibleRuleBreach | null {
    return this.breaches().find((breach) => breach.conditionId === conditionId) ?? null;
  }

  protected choose(conditionId: string, event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.map.update((current) => new Map(current).set(conditionId, value === '' ? null : value));
  }

  protected submit(): void {
    this.attempted.set(true);
    if (this.saving() || this.conditions().length === 0 || this.breaches().length > 0) {
      return;
    }

    this.save.emit([...this.map()].map(([conditionId, mergesIntoConditionId]) => ({ conditionId, mergesIntoConditionId })));
  }

  protected problemText(problem: ProblemDetails): string {
    return problemMessage(this.i18n, 'catalogue-a.compatible', problem);
  }

  protected onClosed(): void {
    this.attempted.set(false);
    this.closed.emit();
  }
}
