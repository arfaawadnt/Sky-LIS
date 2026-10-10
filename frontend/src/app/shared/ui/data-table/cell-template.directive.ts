import { Directive, TemplateRef, inject, input } from '@angular/core';

import type { RowContext, TableColumn } from './data-table.types';

/** What types `let-row`: the rows array of the table, or anything with typed columns (a `MasterDataConfig`). */
export type RowTypeHint<T> = readonly T[] | { readonly columns: readonly TableColumn<T>[] } | null;

/**
 * `<ng-template appCell="status" [appCellOf]="rows()" let-row>…</ng-template>` inside `<app-data-table>` (or inside
 * `<app-master-data-page>` with `[appCellOf]="config"`): renders the cells of the column whose `key` is `status`.
 * `appCellOf` only types `row`.
 */
@Directive({ selector: 'ng-template[appCell]' })
export class CellTemplateDirective<T> {
  readonly template = inject<TemplateRef<RowContext<T>>>(TemplateRef);

  /** The column key. */
  readonly appCell = input.required<string>();
  /** For type inference of `let-row` only. */
  readonly appCellOf = input<RowTypeHint<T>>(null);

  static ngTemplateContextGuard<T>(_directive: CellTemplateDirective<T>, context: unknown): context is RowContext<T> {
    return context !== undefined;
  }
}

/**
 * `<ng-template appRowActions [appRowActionsOf]="rows()" let-row>…buttons…</ng-template>`: the trailing actions
 * cell of every row. Clicks and keys inside it never reach the row (no accidental row activation).
 */
@Directive({ selector: 'ng-template[appRowActions]' })
export class RowActionsTemplateDirective<T> {
  readonly template = inject<TemplateRef<RowContext<T>>>(TemplateRef);

  /** For type inference of `let-row` only. */
  readonly appRowActionsOf = input<RowTypeHint<T>>(null);

  static ngTemplateContextGuard<T>(_directive: RowActionsTemplateDirective<T>, context: unknown): context is RowContext<T> {
    return context !== undefined;
  }
}
