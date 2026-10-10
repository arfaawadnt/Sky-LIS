import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

import { IconComponent, type IconName } from '../icon/icon.component';

export type StatTone = 'brand' | 'ok' | 'warn' | 'bad' | 'info';

/**
 * `<app-stat-card [label]="…" [value]="…" [delta]="+12" [hint]="…" tone="ok" icon="coins" />`: a KPI tile of the
 * dashboards (prototype `.kpi`). `delta` is a number (sign decides the arrow) or a preformatted string.
 */
@Component({
  selector: 'app-stat-card',
  imports: [IconComponent],
  template: `
    <div class="kpi" [class]="'kpi ' + tone()">
      <div class="text">
        <div class="lbl">{{ label() }}</div>
        <div class="val">{{ value() }}</div>
        <div class="foot">
          @if (deltaText(); as delta) {
            <span class="delta" [class.up]="deltaDirection() === 'up'" [class.down]="deltaDirection() === 'down'">
              @if (deltaDirection() === 'up') {
                <app-icon name="trending-up" [size]="13" />
              } @else if (deltaDirection() === 'down') {
                <app-icon name="trending-down" [size]="13" />
              }
              {{ delta }}
            </span>
          }
          @if (hint(); as hint) {
            <span class="hint">{{ hint }}</span>
          }
        </div>
      </div>
      @if (icon(); as icon) {
        <div class="kico"><app-icon [name]="icon" [size]="20" /></div>
      }
    </div>
  `,
  styles: `
    :host {
      display: block;
      min-width: 0;
    }

    .kpi {
      background: var(--surface);
      border: 1px solid var(--line);
      border-radius: var(--r-lg);
      box-shadow: var(--sh);
      padding: var(--sp-4);
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: var(--sp-3);
    }

    .text {
      min-width: 0;
    }

    .lbl {
      font-size: var(--fs-sm);
      color: var(--muted);
    }

    .val {
      font-size: var(--fs-xl);
      font-weight: 800;
      color: var(--navy);
      line-height: 1.1;
      margin: 4px 0;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .foot {
      font-size: var(--fs-xs);
      color: var(--muted);
      display: flex;
      gap: var(--sp-2);
      align-items: center;
      flex-wrap: wrap;
    }

    .delta {
      display: inline-flex;
      align-items: center;
      gap: 2px;
      font-weight: 600;
    }

    .delta.up {
      color: var(--ok);
    }

    .delta.down {
      color: var(--bad);
    }

    .kico {
      width: 40px;
      height: 40px;
      border-radius: 11px;
      display: grid;
      place-items: center;
      background: var(--grad-soft);
      color: var(--violet);
      flex: none;
    }

    .ok .kico {
      background: var(--ok-soft);
      color: var(--ok);
    }

    .warn .kico {
      background: var(--warn-soft);
      color: var(--warn);
    }

    .bad .kico {
      background: var(--bad-soft);
      color: var(--bad);
    }

    .info .kico {
      background: var(--info-soft);
      color: var(--info);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StatCardComponent {
  readonly label = input.required<string>();
  readonly value = input.required<string | number>();
  readonly delta = input<number | string | null>(null);
  readonly hint = input<string | null>(null);
  readonly tone = input<StatTone>('brand');
  readonly icon = input<IconName | null>(null);

  protected readonly deltaDirection = computed<'up' | 'down' | 'flat'>(() => {
    const delta = this.delta();
    if (typeof delta !== 'number') {
      return 'flat';
    }

    return delta > 0 ? 'up' : delta < 0 ? 'down' : 'flat';
  });

  protected readonly deltaText = computed(() => {
    const delta = this.delta();
    if (delta === null) {
      return null;
    }

    if (typeof delta === 'string') {
      return delta;
    }

    return delta > 0 ? `+${delta}` : String(delta);
  });
}
