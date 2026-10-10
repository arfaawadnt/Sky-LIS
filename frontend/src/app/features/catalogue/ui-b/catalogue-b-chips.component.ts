import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/** One chip: a short code and its full name as the tooltip. */
export interface CatalogueBChip {
  readonly key: string;
  readonly text: string;
  readonly title: string;
  readonly inactive?: boolean;
}

/**
 * `<app-catalogue-b-chips [chips]="…" [max]="4" [emptyText]="…" />`: the linked records of a row (services,
 * branches) as compact tags; beyond `max` a "+N" tag carries the rest in its tooltip.
 */
@Component({
  selector: 'app-catalogue-b-chips',
  template: `
    @for (chip of visible(); track chip.key) {
      <span class="tag" [class.inactive]="chip.inactive === true" [title]="chip.title">{{ chip.text }}</span>
    } @empty {
      @if (emptyText()) {
        <span class="muted small">{{ emptyText() }}</span>
      }
    }
    @if (hidden().length > 0) {
      <span class="tag more" [title]="hiddenTitle()">+{{ hidden().length }}</span>
    }
  `,
  styles: `
    :host { display: inline-flex; flex-wrap: wrap; gap: 4px; }
    .tag { display: inline-block; padding: 1px var(--sp-2); border-radius: var(--r-pill); background: var(--blue-soft); color: var(--ink); font-family: var(--mono); font-size: var(--fs-xs); white-space: nowrap; }
    .tag.inactive { background: var(--surface-2); color: var(--muted); text-decoration: line-through; }
    .tag.more { background: var(--surface-2); color: var(--ink-2); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CatalogueBChipsComponent {
  readonly chips = input.required<readonly CatalogueBChip[]>();
  readonly max = input(4);
  readonly emptyText = input('');

  protected readonly visible = computed(() => this.chips().slice(0, this.max()));
  protected readonly hidden = computed(() => this.chips().slice(this.max()));
  protected readonly hiddenTitle = computed(() => this.hidden().map((chip) => chip.title).join(', '));
}
