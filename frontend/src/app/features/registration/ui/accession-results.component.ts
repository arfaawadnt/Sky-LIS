import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';

import { RESULTS_LINKS, RESULTS_PERMISSIONS, type ResultCaseView } from '../../../core/api/results.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { pick } from '../data/registration-format';

/**
 * The *Results* card of Accession details (CP-10b): the status of every report unit the user may see (clinical group or
 * print-alone service), its result count, panic / delta / printed marks, the units hidden by the user's result rights,
 * and the link to result entry (`/results/by-accession?accession=`): *Enter results* for `results.create`, *Open results*
 * otherwise. The card is rendered only for `results.view` (the page gates it); a refused or empty read shows the empty text.
 */
@Component({
  selector: 'app-reg-accession-results',
  imports: [RouterLink, BadgeComponent, CanDirective, IconComponent, SectionCardComponent],
  template: `
    <app-section-card [title]="i18n.t('registration.accession.results.title')" [padded]="false">
      <a cardActions class="btn sm primary" *appCan="permissions.enter" [routerLink]="links.byAccession" [queryParams]="{ accession: accession() }"><app-icon name="flask" [size]="14" />{{ i18n.t('registration.accession.results.enter') }}</a>
      <a cardActions class="btn sm ghost" [routerLink]="links.byAccession" [queryParams]="{ accession: accession() }"><app-icon name="eye" [size]="14" />{{ i18n.t('registration.accession.results.open') }}</a>
      @if (loading()) {
        <p class="muted small pad" role="status">{{ i18n.t('registration.loading') }}</p>
      } @else if (units().length === 0) {
        <p class="muted small pad">{{ view() === null ? i18n.t('registration.accession.results.empty') : i18n.t('registration.accession.results.allHidden') }}</p>
      } @else {
        <ul class="units">
          @for (unit of units(); track unit.key) {
            <li>
              <span class="unit-name">{{ unitName(unit) }}</span>
              <span class="small muted">{{ i18n.tf('registration.accession.results.count', { count: unit.results.length }) }}</span>
              <span class="marks">
                @if (unit.hasPanic) { <app-badge tone="bad">{{ i18n.t('registration.accession.results.panic') }}</app-badge> }
                @if (unit.hasDelta) { <app-badge tone="warn">{{ i18n.t('registration.accession.results.delta') }}</app-badge> }
                @if (unit.isPrinted) { <app-badge tone="info">{{ i18n.tf('registration.accession.results.printed', { count: unit.printCount }) }}</app-badge> }
                <app-badge [status]="unit.status">{{ i18n.t('shared.status.' + unit.status) }}</app-badge>
              </span>
            </li>
          }
        </ul>
      }
      @if (hidden() > 0) {
        <p class="small muted pad hidden-note">{{ i18n.tf('registration.accession.results.hidden', { count: hidden() }) }}</p>
      }
    </app-section-card>
  `,
  styles: `
    .pad { padding: var(--sp-3) var(--sp-4); }
    .units { list-style: none; margin: 0; padding: 0; }
    .units li { display: flex; flex-wrap: wrap; align-items: center; gap: var(--sp-2) var(--sp-3); padding: var(--sp-2) var(--sp-4); border-block-end: 1px solid var(--line-soft); font-size: var(--fs-sm); }
    .units li:last-child { border-block-end: 0; }
    .unit-name { font-weight: 600; }
    .marks { display: inline-flex; gap: var(--sp-1); margin-inline-start: auto; }
    .hidden-note { border-block-start: 1px solid var(--line-soft); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AccessionResultsComponent {
  protected readonly i18n = inject(I18nService);

  readonly view = input<ResultCaseView | null>(null);
  readonly loading = input(false);
  readonly accession = input('');

  protected readonly permissions = RESULTS_PERMISSIONS;
  protected readonly links = RESULTS_LINKS;

  protected readonly units = computed(() => this.view()?.units ?? []);
  /** Units with nothing the user may see plus the results hidden inside the visible units. */
  protected readonly hidden = computed(() => (this.view()?.hiddenUnits ?? 0) + (this.view()?.hiddenResults ?? 0));

  protected unitName(unit: ResultCaseView['units'][number]): string {
    const group = pick(unit.groupNameEn, unit.groupNameAr, this.i18n.lang());
    const service = unit.printAloneServiceId === null ? '' : pick(unit.serviceNameEn, unit.serviceNameAr, this.i18n.lang());
    return service === '' ? group : `${service} · ${group}`;
  }
}
