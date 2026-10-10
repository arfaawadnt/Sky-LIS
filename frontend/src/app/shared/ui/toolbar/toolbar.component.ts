import { ChangeDetectionStrategy, Component } from '@angular/core';

/**
 * `<app-toolbar><app-search-box toolbarFilters /><select toolbarFilters>…</select><button toolbarActions>…</button></app-toolbar>`:
 * the filter row above a list (prototype `.toolbar`). Filters sit at the start, actions at the end; both wrap on
 * small screens.
 */
@Component({
  selector: 'app-toolbar',
  template: `
    <div class="filters"><ng-content select="[toolbarFilters]" /></div>
    <div class="actions"><ng-content select="[toolbarActions]" /></div>
  `,
  styles: `
    :host {
      display: flex;
      gap: var(--sp-2);
      align-items: center;
      flex-wrap: wrap;
      padding: var(--sp-3) var(--sp-4);
      border-bottom: 1px solid var(--line-soft);
    }

    .filters,
    .actions {
      display: flex;
      gap: var(--sp-2);
      align-items: center;
      flex-wrap: wrap;
      min-width: 0;
    }

    .filters {
      flex: 1 1 auto;
    }

    .actions {
      margin-inline-start: auto;
      justify-content: flex-end;
    }

    .actions:empty {
      display: none;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ToolbarComponent {}
