import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * `<app-section-card [title]="…" [subtitle]="…"><button cardActions>…</button> …body… </app-section-card>`: a
 * titled panel (prototype `.card` + `.card-head` + `.card-body`). `padded` false lets a table fill the card edge
 * to edge (the data table and toolbar draw their own rules).
 */
@Component({
  selector: 'app-section-card',
  template: `
    <section class="card">
      @if (title() !== null || subtitle() !== null) {
        <header class="card-head">
          <div class="titles">
            @if (title(); as title) {
              <h2 class="card-title">{{ title }}</h2>
            }
            @if (subtitle(); as subtitle) {
              <p class="card-sub">{{ subtitle }}</p>
            }
          </div>
          <div class="actions"><ng-content select="[cardActions]" /></div>
        </header>
      }
      <div class="card-body" [class.padded]="padded()"><ng-content /></div>
    </section>
  `,
  styles: `
    :host {
      display: block;
      min-width: 0;
    }

    .card {
      background: var(--surface);
      border: 1px solid var(--line);
      border-radius: var(--r-lg);
      box-shadow: var(--sh);
      overflow: hidden;
    }

    .card-head {
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: var(--sp-3);
      padding: var(--sp-3) var(--sp-4);
      border-bottom: 1px solid var(--line-soft);
    }

    .card-title {
      font-weight: 700;
      color: var(--navy);
      font-size: var(--fs);
      margin: 0;
    }

    .card-sub {
      font-size: var(--fs-sm);
      color: var(--muted);
      margin: 0;
    }

    .actions {
      display: flex;
      gap: var(--sp-2);
      flex-wrap: wrap;
    }

    .actions:empty {
      display: none;
    }

    .card-body.padded {
      padding: var(--sp-4);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SectionCardComponent {
  readonly title = input<string | null>(null);
  readonly subtitle = input<string | null>(null);
  readonly padded = input(true);
}
