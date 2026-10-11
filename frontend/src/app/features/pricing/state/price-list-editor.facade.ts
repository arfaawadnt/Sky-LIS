import { Injectable, computed, inject, resource, signal } from '@angular/core';

import { PRICE_LIMITS, type PriceLineItem, type PriceLineRequest, type ServiceLabel } from '../../../core/api/pricing.contracts';
import { mutation } from '../../../core/state/mutation';
import { hasScale } from '../data/money';
import { PricingApi } from '../data/pricing.api';
import { ServiceNamesStore } from './service-names.store';

/** One line of the editable grid: the stored line merged with the unsaved edit. */
export interface EditorRow {
  readonly id: string;
  readonly serviceId: string;
  readonly price: number;
  readonly isActive: boolean;
  readonly storedPrice: number | null;
  readonly isNew: boolean;
  readonly dirty: boolean;
  readonly invalid: boolean;
}

interface LineEdit {
  readonly price: number;
  readonly isActive: boolean;
}

/**
 * The price grid of one base list (CP-33): the list header, its lines, the unsaved edits (price and active flag per
 * service, new services), and the list actions (save the changed lines in one upsert, activate, copy, apply to the
 * derived contracts). Saving prices never re-prices a contract (BR-07): Apply is a separate, confirmed action.
 * Provided at the route together with `ServiceNamesStore`.
 */
@Injectable()
export class PriceListEditorFacade {
  private readonly api = inject(PricingApi);
  readonly names = inject(ServiceNamesStore);

  readonly listId = signal<string | null>(null);

  readonly detail = resource({
    params: () => this.listId() ?? undefined,
    loader: ({ params, abortSignal }) => this.api.getBasePriceList(params, abortSignal),
  });

  readonly lines = resource({
    params: () => this.listId() ?? undefined,
    loader: async ({ params, abortSignal }) => {
      const lines = await this.api.getPriceLines(params, abortSignal);
      this.names.rememberRows(lines);
      return lines;
    },
  });

  /** Unsaved changes by service id; services not on the list yet are new lines. */
  readonly edits = signal<ReadonlyMap<string, LineEdit>>(new Map());

  /** New services in the order they were added (shown first). */
  readonly added = signal<readonly string[]>([]);

  readonly search = signal('');

  readonly stored = computed<ReadonlyMap<string, PriceLineItem>>(() => new Map((this.lines.hasValue() ? this.lines.value() : []).map((line) => [line.serviceId, line])));

  readonly rows = computed<readonly EditorRow[]>(() => {
    const edits = this.edits();
    const stored = this.stored();
    const fresh = this.added().map((serviceId) => toRow(serviceId, null, edits.get(serviceId)));
    const existing = [...stored.values()].map((line) => toRow(line.serviceId, line, edits.get(line.serviceId)));
    return [...fresh, ...existing];
  });

  readonly visibleRows = computed<readonly EditorRow[]>(() => {
    const term = this.search().trim().toLowerCase();
    if (term === '') {
      return this.rows();
    }

    return this.rows().filter((row) => {
      const label = this.names.label(row.serviceId);
      return [label?.code, label?.nameEn, label?.nameAr].some((text) => (text ?? '').toLowerCase().includes(term));
    });
  });

  readonly dirtyRows = computed(() => this.rows().filter((row) => row.dirty));

  readonly invalidCount = computed(() => this.rows().filter((row) => row.dirty && row.invalid).length);

  readonly takenIds = computed<ReadonlySet<string>>(() => new Set(this.rows().map((row) => row.serviceId)));

  readonly save = mutation(
    (input: { listId: string; lines: readonly PriceLineRequest[] }) => this.api.upsertPriceLines(input.listId, input.lines),
    {
      then: () => {
        this.edits.set(new Map());
        this.added.set([]);
        this.reload();
      },
    },
  );

  readonly setActive = mutation((input: { listId: string; active: boolean }) => this.api.setBasePriceListActive(input.listId, input.active), { then: () => this.detail.reload() });

  readonly apply = mutation((listId: string) => this.api.applyBasePriceList(listId), { then: () => this.detail.reload() });

  readonly copy = mutation((input: { listId: string; request: Parameters<PricingApi['copyBasePriceList']>[1] }) => this.api.copyBasePriceList(input.listId, input.request));

  /** Opens a list; the same list is re-read, because the facade outlives the page (route provider, UAT D-14). */
  load(listId: string): void {
    this.discard();
    if (this.listId() === listId) {
      this.reload();
    } else {
      this.listId.set(listId);
    }
  }

  reload(): void {
    this.detail.reload();
    this.lines.reload();
  }

  setPrice(serviceId: string, price: number): void {
    const current = this.rows().find((row) => row.serviceId === serviceId);
    if (current) {
      this.edit(serviceId, { price, isActive: current.isActive });
    }
  }

  setLineActive(serviceId: string, isActive: boolean): void {
    const current = this.rows().find((row) => row.serviceId === serviceId);
    if (current) {
      this.edit(serviceId, { price: current.price, isActive });
    }
  }

  /** Adds a catalogue service as a new line (price 0, active); false when the list already holds it. */
  addService(service: ServiceLabel): boolean {
    this.names.remember([service]);
    if (this.takenIds().has(service.id)) {
      return false;
    }

    this.added.update((current) => [service.id, ...current]);
    this.edits.update((current) => new Map(current).set(service.id, { price: 0, isActive: true }));
    return true;
  }

  discard(): void {
    this.edits.set(new Map());
    this.added.set([]);
    this.save.reset();
  }

  /** The upsert payload: changed and new lines only. */
  pendingLines(): readonly PriceLineRequest[] {
    return this.dirtyRows().map((row) => ({ serviceId: row.serviceId, price: row.price, isActive: row.isActive }));
  }

  private edit(serviceId: string, edit: LineEdit): void {
    const stored = this.stored().get(serviceId);
    this.edits.update((current) => {
      const next = new Map(current);
      if (stored && stored.price === edit.price && stored.isActive === edit.isActive) {
        next.delete(serviceId);
      } else {
        next.set(serviceId, edit);
      }

      return next;
    });
  }
}

function toRow(serviceId: string, stored: PriceLineItem | null, edit: LineEdit | undefined): EditorRow {
  const price = edit?.price ?? stored?.price ?? 0;
  const isActive = edit?.isActive ?? stored?.isActive ?? true;
  return {
    id: serviceId,
    serviceId,
    price,
    isActive,
    storedPrice: stored?.price ?? null,
    isNew: stored === null,
    dirty: stored === null || edit !== undefined,
    invalid: !(price >= 0 && price <= PRICE_LIMITS.maxAmount && hasScale(price, 2)),
  };
}
