import { ChangeDetectionStrategy, Component, ElementRef, inject, input, output, viewChild } from '@angular/core';

import type { VisitListItem } from '../../../core/api/registration.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { dateTime, pick } from '../data/registration-format';

/**
 * The search strip of Accession details (CP-10b): one box — accession, patient number, mobile or name — run on Enter or
 * the button (never while typing: a desk types a name and expects to choose), and the compact list of matches when
 * several visits answer. The page owns the search (facade) and decides what opening a match means (a navigation).
 */
@Component({
  selector: 'app-reg-accession-search',
  imports: [BadgeComponent, IconComponent],
  template: `
    <form class="strip" role="search" (submit)="submit($event)">
      <label class="box">
        <span class="visually-hidden">{{ i18n.t('registration.accession.search') }}</span>
        <app-icon name="search" [size]="16" />
        <input #field type="search" [value]="text()" [placeholder]="i18n.t('registration.accession.searchPlaceholder')" autocomplete="off" enterkeyhint="search" (keydown.escape)="closeList.emit()" />
      </label>
      <button type="submit" class="btn primary" [disabled]="searching()">{{ searching() ? i18n.t('registration.accession.searching') : i18n.t('registration.accession.search') }}</button>
    </form>
    @if (listOpen()) {
      <div class="matches" role="group" [attr.aria-label]="i18n.t('registration.accession.matches')">
        <div class="matches-head">
          <span>{{ i18n.tf('registration.accession.pickVisit', { count: matches().length }) }}</span>
          <button type="button" class="btn sm ghost" (click)="closeList.emit()"><app-icon name="x" [size]="14" />{{ i18n.t('registration.close') }}</button>
        </div>
        @for (row of matches(); track row.id) {
          <button type="button" class="match" (click)="pick.emit(row)">
            <b class="mono">{{ row.accession }}</b>
            <span class="who">{{ patientName(row) }} <span class="small muted mono">{{ row.patientNumber ?? '' }}</span></span>
            <span class="small muted">{{ when(row.registeredAtUtc) }} · {{ name(row.branchNameEn, row.branchNameAr) }}</span>
            <app-badge [status]="row.status">{{ i18n.t('registration.status.' + row.status) }}</app-badge>
          </button>
        }
      </div>
    }
  `,
  styles: `
    :host { display: block; }
    .strip { display: flex; gap: var(--sp-2); align-items: center; }
    .box { flex: 1; display: flex; align-items: center; gap: var(--sp-2); padding-inline: var(--sp-3); background: var(--surface); border: 1px solid var(--line); border-radius: var(--r-pill); color: var(--muted); }
    .box:focus-within { border-color: var(--blue); box-shadow: var(--focus-ring); }
    .box input { flex: 1; min-width: 0; padding-block: var(--sp-2); border: 0; background: transparent; font: inherit; font-size: var(--fs-md); color: var(--ink); outline: none; }
    .matches { margin-block-start: var(--sp-2); background: var(--surface); border: 1px solid var(--line-soft); border-radius: var(--r); box-shadow: var(--sh); }
    .matches-head { display: flex; justify-content: space-between; align-items: center; padding: var(--sp-2) var(--sp-3); font-size: var(--fs-sm); color: var(--ink-2); border-block-end: 1px solid var(--line-soft); }
    .match { display: grid; grid-template-columns: 150px 1fr auto auto; gap: var(--sp-3); align-items: center; width: 100%; padding: var(--sp-2) var(--sp-3); border: 0; border-block-end: 1px solid var(--line-soft); background: transparent; text-align: start; font: inherit; cursor: pointer; }
    .match:last-child { border-block-end: 0; }
    .match:hover, .match:focus-visible { background: var(--row-hover); }
    @media (max-width: 720px) { .match { grid-template-columns: 1fr 1fr; } }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AccessionSearchComponent {
  protected readonly i18n = inject(I18nService);
  private readonly field = viewChild.required<ElementRef<HTMLInputElement>>('field');

  readonly text = input('');
  readonly searching = input(false);
  readonly matches = input<readonly VisitListItem[]>([]);
  readonly listOpen = input(false);
  readonly run = output<string>();
  readonly pick = output<VisitListItem>();
  readonly closeList = output<void>();

  protected when(utc: string | null): string {
    return dateTime(utc, this.i18n.lang());
  }

  protected name(en: string | null, ar: string | null): string {
    return pick(en, ar, this.i18n.lang());
  }

  focus(): void {
    this.field().nativeElement.focus();
  }

  protected submit(event: Event): void {
    event.preventDefault();
    this.run.emit(this.field().nativeElement.value);
  }

  protected patientName(row: VisitListItem): string {
    return row.patientMasked ? this.i18n.t('registration.vipMasked') : pick(row.patientName, row.patientNameAr, this.i18n.lang());
  }
}
