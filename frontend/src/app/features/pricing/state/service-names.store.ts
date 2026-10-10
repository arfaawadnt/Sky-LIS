import { Injectable, signal } from '@angular/core';

import type { ServiceLabel } from '../../../core/api/pricing.contracts';

/** A row that carries its service's catalogue code and names (price lines, contract prices). */
export interface LabelledRow {
  readonly serviceId: string;
  readonly serviceCode: string | null;
  readonly serviceNameEn: string | null;
  readonly serviceNameAr: string | null;
}

/**
 * Service codes and names of the pricing grids, for display, search and CSV. The server sends them with every line and
 * price (it reads the catalogue); services picked in the typeahead are added from the picker's result, so a new line
 * shows its name before it is saved. Ids the catalogue does not know show as "—". Provided at the route.
 */
@Injectable()
export class ServiceNamesStore {
  readonly labels = signal<ReadonlyMap<string, ServiceLabel>>(new Map());

  /** Takes the labels the server sent with the rows. */
  rememberRows(rows: readonly LabelledRow[]): void {
    this.remember(
      rows
        .filter((row) => row.serviceCode !== null || row.serviceNameEn !== null || row.serviceNameAr !== null)
        .map((row) => ({ id: row.serviceId, code: row.serviceCode ?? '', nameEn: row.serviceNameEn ?? row.serviceNameAr ?? '', nameAr: row.serviceNameAr ?? row.serviceNameEn ?? '' })),
    );
  }

  remember(labels: readonly ServiceLabel[]): void {
    if (labels.length === 0) {
      return;
    }

    this.labels.update((current) => {
      const next = new Map(current);
      for (const label of labels) {
        next.set(label.id, label);
      }

      return next;
    });
  }

  label(id: string): ServiceLabel | null {
    return this.labels().get(id) ?? null;
  }

  code(id: string): string {
    return this.label(id)?.code ?? '';
  }

  name(id: string, lang: string): string {
    const label = this.label(id);
    if (!label) {
      return '';
    }

    return lang === 'ar' ? label.nameAr || label.nameEn : label.nameEn || label.nameAr;
  }
}
