import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

import { CODE128, encodeCode128 } from './code128';

/**
 * `<app-code128 [value]="sample.number" [height]="40" />`: a Code 128 barcode as a crisp SVG sized by CSS (the
 * print stylesheet makes it fill its box). Shows the text under the bars unless `showText` is false. An
 * unencodable value renders nothing and reports through `aria-label`.
 */
@Component({
  selector: 'app-code128',
  template: `
    @if (symbol(); as symbol) {
      <svg class="code128" [attr.viewBox]="viewBox()" [attr.height]="height()" preserveAspectRatio="xMidYMid meet" role="img" [attr.aria-label]="label() ?? value()" shape-rendering="crispEdges">
        @for (bar of symbol.bars; track $index) {
          <rect [attr.x]="bar.x + quiet" y="0" [attr.width]="bar.width" [attr.height]="barHeight()" fill="currentColor" />
        }
        @if (showText()) {
          <text [attr.x]="(symbol.modules + 2 * quiet) / 2" [attr.y]="barHeight() + 7" text-anchor="middle" font-family="Cascadia Mono, Consolas, monospace" font-size="7" fill="currentColor">{{ value() }}</text>
        }
      </svg>
    }
  `,
  styles: `
    :host {
      display: inline-block;
      line-height: 0;
      color: #111;
    }

    svg {
      display: block;
      max-width: 100%;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Code128Component {
  readonly value = input.required<string>();
  /** Bar height in SVG units (the width follows the number of modules). */
  readonly height = input(40);
  readonly showText = input(true);
  readonly label = input<string | null>(null);

  protected readonly quiet = CODE128.quietZone;

  protected readonly symbol = computed(() => {
    try {
      return encodeCode128(this.value());
    } catch {
      return null;
    }
  });

  protected readonly barHeight = computed(() => (this.showText() ? this.height() - 9 : this.height()));

  protected readonly viewBox = computed(() => {
    const symbol = this.symbol();
    const width = (symbol?.modules ?? 0) + 2 * this.quiet;
    return `0 0 ${width} ${this.height()}`;
  });
}
