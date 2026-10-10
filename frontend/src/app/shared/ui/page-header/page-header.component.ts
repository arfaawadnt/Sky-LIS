import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';

/** One crumb of the optional breadcrumb; `path` null renders plain text (the current page). */
export interface Breadcrumb {
  readonly label: string;
  readonly path: string | null;
}

/**
 * `<app-page-header [title]="…" [subtitle]="…" [breadcrumb]="[…]"><button pageActions>…</button></app-page-header>`:
 * the heading band of every page (prototype `.page-head`). Labels are passed already translated.
 */
@Component({
  selector: 'app-page-header',
  imports: [RouterLink],
  templateUrl: './page-header.component.html',
  styleUrl: './page-header.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PageHeaderComponent {
  readonly title = input.required<string>();
  readonly subtitle = input<string | null>(null);
  readonly breadcrumb = input<readonly Breadcrumb[]>([]);
}
