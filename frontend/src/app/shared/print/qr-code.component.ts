import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import qrcode from 'qrcode-generator';

export type QrErrorCorrection = 'L' | 'M' | 'Q' | 'H';

// The library's default byte conversion truncates to one byte per character; encode as UTF-8 so Arabic text and
// URLs with non-ASCII characters survive the round trip.
qrcode.stringToBytes = (text: string): number[] => [...new TextEncoder().encode(text)];

/**
 * `<app-qr-code [value]="reportUrl" [size]="96" />`: a QR code as an SVG path (`qrcode-generator`, MIT). Used on
 * laboratory reports (verification link) and receipts. `label` is the accessible name; defaults to the value.
 */
@Component({
  selector: 'app-qr-code',
  template: `
    @if (path(); as path) {
      <svg [attr.viewBox]="viewBox()" [attr.width]="size()" [attr.height]="size()" role="img" [attr.aria-label]="label() ?? value()" shape-rendering="crispEdges">
        <path [attr.d]="path" fill="currentColor" />
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
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class QrCodeComponent {
  readonly value = input.required<string>();
  /** Rendered size in CSS pixels (square). */
  readonly size = input(96);
  readonly level = input<QrErrorCorrection>('M');
  /** Quiet zone in modules. */
  readonly margin = input(2);
  readonly label = input<string | null>(null);

  private readonly matrix = computed(() => {
    const value = this.value();
    if (value.length === 0) {
      return null;
    }

    try {
      const qr = qrcode(0, this.level());
      qr.addData(value, 'Byte');
      qr.make();
      return qr;
    } catch {
      return null;
    }
  });

  protected readonly modules = computed(() => this.matrix()?.getModuleCount() ?? 0);

  protected readonly viewBox = computed(() => {
    const total = this.modules() + 2 * this.margin();
    return `0 0 ${total} ${total}`;
  });

  protected readonly path = computed<string | null>(() => {
    const qr = this.matrix();
    if (qr === null) {
      return null;
    }

    const count = qr.getModuleCount();
    const margin = this.margin();
    const segments: string[] = [];
    for (let row = 0; row < count; row++) {
      for (let col = 0; col < count; col++) {
        if (qr.isDark(row, col)) {
          segments.push(`M${col + margin} ${row + margin}h1v1h-1z`);
        }
      }
    }

    return segments.join('');
  });
}
