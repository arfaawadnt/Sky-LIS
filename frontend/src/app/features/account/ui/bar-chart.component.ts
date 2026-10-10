import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/** One bar: its axis label, its value and the accessible description. */
export interface BarDatum {
  readonly label: string;
  readonly value: number;
  readonly description: string;
}

const WIDTH = 560;
const HEIGHT = 200;
const AXIS = 22;
const TOP = 18;

/**
 * A small inline-SVG bar chart (no chart library, CP-05 "7-day registrations"): bars scaled to the largest value,
 * the value above each bar and the label below, the last bar highlighted (today). With `rtl` the time axis runs
 * from right to left; screen readers get a list of the values instead of the drawing.
 */
@Component({
  selector: 'app-bar-chart',
  template: `
    <figure class="chart">
      <svg [attr.viewBox]="'0 0 ' + width + ' ' + height" role="img" [attr.aria-label]="label()" preserveAspectRatio="xMidYMid meet">
        <line class="base" [attr.x1]="0" [attr.x2]="width" [attr.y1]="height - axis" [attr.y2]="height - axis" />
        @for (bar of bars(); track $index; let last = $last) {
          <g>
            <rect [class.last]="last" [attr.x]="bar.x" [attr.y]="bar.y" [attr.width]="bar.width" [attr.height]="bar.height" rx="5" />
            <text class="value" [attr.x]="bar.x + bar.width / 2" [attr.y]="bar.y - 5">{{ bar.value }}</text>
            <text class="label" [attr.x]="bar.x + bar.width / 2" [attr.y]="height - 6">{{ bar.label }}</text>
          </g>
        }
      </svg>
      <figcaption class="visually-hidden">
        <ul>
          @for (bar of data(); track $index) {
            <li>{{ bar.description }}</li>
          }
        </ul>
      </figcaption>
    </figure>
  `,
  styles: `
    .chart {
      margin: 0;
    }

    svg {
      display: block;
      width: 100%;
      height: auto;
      max-height: 240px;
    }

    rect {
      fill: var(--blue-soft);
      stroke: var(--blue);
      stroke-width: 1;
    }

    rect.last {
      fill: var(--blue);
    }

    .base {
      stroke: var(--line);
    }

    text {
      font-size: 12px;
      text-anchor: middle;
      fill: var(--muted);
    }

    .value {
      fill: var(--ink);
      font-weight: 600;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BarChartComponent {
  readonly data = input.required<readonly BarDatum[]>();
  readonly label = input('');
  readonly rtl = input(false);

  protected readonly width = WIDTH;
  protected readonly height = HEIGHT;
  protected readonly axis = AXIS;

  protected readonly bars = computed(() => {
    const data = this.data();
    const max = Math.max(1, ...data.map((datum) => datum.value));
    const slot = WIDTH / Math.max(1, data.length);
    const barWidth = Math.min(56, slot * 0.6);
    const usable = HEIGHT - AXIS - TOP;
    const rtl = this.rtl();
    return data.map((datum, index) => {
      const position = rtl ? data.length - 1 - index : index;
      const height = Math.max(2, (datum.value / max) * usable);
      return { label: datum.label, value: datum.value, x: position * slot + (slot - barWidth) / 2, y: HEIGHT - AXIS - height, width: barWidth, height };
    });
  });
}
