import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';

import { type LabTrackerItem, TRACKER_LINKS } from '../../../core/api/tracker.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { StatusPillComponent } from './status-pill.component';
import { cardFlag, num, outsideLook, patientLine, payerLine, paymentLook, progressLabel, progressOf, timeLeftLook } from './tracker-format';

/**
 * A visit on the board (layout B): accession and time left, patient, one-line detail, payment and flag pills, and the
 * four-segment progress bar (registration, samples, results, payment) with its accessible name.
 */
@Component({
  selector: 'app-tracker-visit-card',
  imports: [RouterLink, StatusPillComponent],
  template: `
    <article class="card" [class.urgent]="item().urgent">
      <header class="top">
        <a class="acc mono" [routerLink]="accessionLink" [queryParams]="{ visitId: item().visitId }">{{ item().accession }}</a>
        <app-tracker-pill [look]="timeLeft()" absent="none" />
      </header>
      <p class="patient">{{ patient() }}</p>
      <p class="detail">{{ detail() }}</p>
      <div class="pills">
        <app-tracker-pill [look]="payment()" />
        @if (item().urgent) {
          <app-tracker-pill [look]="urgent()" />
        }
        @if (flag(); as flag) {
          <app-tracker-pill [look]="flag" />
        }
        @if (outside(); as ext) {
          <app-tracker-pill [look]="ext" />
        }
      </div>
      <div class="bar" role="img" [attr.aria-label]="progressText()">
        @for (segment of progress(); track segment.part) {
          <span class="seg" [class]="'seg ' + segment.state"></span>
        }
      </div>
    </article>
  `,
  styles: `
    .card {
      display: grid;
      gap: 6px;
      padding: var(--sp-3);
      background: var(--surface);
      border: 1px solid var(--line);
      border-inline-start: 3px solid var(--line);
      border-radius: var(--r);
      box-shadow: var(--sh);
    }

    .card.urgent {
      border-inline-start-color: var(--bad);
    }

    .top {
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: var(--sp-2);
    }

    .acc {
      font-weight: 700;
      color: var(--blue);
      text-decoration: none;
    }

    .acc:hover {
      text-decoration: underline;
    }

    p {
      margin: 0;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .patient {
      font-weight: 600;
      color: var(--ink);
    }

    .detail {
      font-size: var(--fs-sm);
      color: var(--muted);
    }

    .pills {
      display: flex;
      flex-wrap: wrap;
      gap: 4px;
    }

    .bar {
      display: grid;
      grid-template-columns: repeat(4, 1fr);
      gap: 3px;
      margin-block-start: 2px;
    }

    .seg {
      height: 5px;
      border-radius: var(--r-pill);
      background: var(--line-soft);
    }

    .seg.done {
      background: var(--ok);
    }

    .seg.partial {
      background: var(--warn);
    }

    .seg.bad {
      background: var(--bad);
    }

    .seg.unknown {
      background: repeating-linear-gradient(90deg, var(--line) 0 4px, transparent 4px 7px);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class VisitCardComponent {
  private readonly i18n = inject(I18nService);
  protected readonly accessionLink = TRACKER_LINKS.accession;

  readonly item = input.required<LabTrackerItem>();

  protected readonly timeLeft = computed(() => timeLeftLook(this.item().minutesLeft, this.item().delayState, this.i18n));
  protected readonly patient = computed(() => patientLine(this.item(), this.i18n));
  protected readonly payment = computed(() => paymentLook(this.item(), this.i18n));
  protected readonly flag = computed(() => cardFlag(this.item(), this.i18n));
  protected readonly outside = computed(() => outsideLook(this.item(), this.i18n));
  protected readonly urgent = computed(() => ({ tone: 'bad' as const, icon: 'zap' as const, text: this.i18n.t('tracker.urgent') }));
  protected readonly progress = computed(() => progressOf(this.item()));
  protected readonly progressText = computed(() => progressLabel(this.progress(), this.i18n));

  /** "3 services · Cash · Gold": the live services, the payer and the tier. */
  protected readonly detail = computed(() => {
    const item = this.item();
    const live = item.registration.lines - item.registration.cancelledLines;
    const lang = this.i18n.lang();
    const services = this.i18n.tc('tracker.services', live, { count: num(live, lang) });
    const payer = payerLine(item, lang);
    return payer === null ? services : `${services} · ${payer}`;
  });
}
