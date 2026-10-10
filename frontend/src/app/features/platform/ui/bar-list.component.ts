import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/** One bar of a `<app-bar-list>`: an already translated label and its value. */
export interface BarItem {
  readonly label: string;
  readonly value: number;
}

/**
 * `<app-bar-list [items]="…" [emptyText]="…" />`: the simple horizontal bar list of the dashboard (SA-03 plan
 * distribution, registrations by client). Bars are relative to the largest value; logical properties keep RTL right.
 */
@Component({
  selector: 'app-bar-list',
  template: `
    @if (items().length === 0) {
      <p class="muted small empty">{{ emptyText() }}</p>
    } @else {
      <ul class="bars" role="list">
        @for (item of items(); track item.label) {
          <li>
            <span class="label" [title]="item.label">{{ item.label }}</span>
            <span class="track" aria-hidden="true"><span class="fill" [style.inline-size.%]="share(item.value)"></span></span>
            <span class="value mono">{{ item.value }}</span>
          </li>
        }
      </ul>
    }
  `,
  styles: `
    .bars {
      list-style: none;
      margin: 0;
      padding: 0;
      display: grid;
      gap: var(--sp-2);
    }

    li {
      display: grid;
      grid-template-columns: minmax(90px, 160px) minmax(0, 1fr) auto;
      align-items: center;
      gap: var(--sp-3);
      font-size: var(--fs-sm);
    }

    .label {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      color: var(--ink-2);
    }

    .track {
      display: block;
      block-size: 10px;
      border-radius: var(--r-pill);
      background: var(--line-soft);
      overflow: hidden;
    }

    .fill {
      display: block;
      block-size: 100%;
      border-radius: var(--r-pill);
      background: var(--grad);
      min-inline-size: 2px;
    }

    .value {
      min-inline-size: 2.5ch;
      text-align: end;
    }

    .empty {
      margin: 0;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BarListComponent {
  readonly items = input.required<readonly BarItem[]>();
  readonly emptyText = input('');

  private readonly max = computed(() => Math.max(1, ...this.items().map((item) => item.value)));

  protected share(value: number): number {
    return Math.round((value / this.max()) * 100);
  }
}
