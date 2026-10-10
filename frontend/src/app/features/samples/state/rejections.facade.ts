import { Injectable, computed, inject, resource, signal } from '@angular/core';

import type { PageRequest } from '../../../core/api/paging';
import type { CreatedResource, SampleDetail, SampleListFilter } from '../../../core/api/samples.contracts';
import { mutation } from '../../../core/state/mutation';
import { SamplesApi } from '../data/samples.api';
import { normalizeSampleNumber } from '../ui/sample-format';
import { SampleLabelPrinter } from './sample-label-printer';
import { SampleListExport } from './sample-list-export';

export interface RejectedFilter {
  /** `yyyy-MM-dd` local dates bounding the time the samples were ordered, or null. */
  readonly fromDate: string | null;
  readonly toDate: string | null;
  readonly page: number;
  readonly pageSize: number;
}

/**
 * CP-16 Reject / unreject (01 §6.3): look a tube up by barcode, reject it with a catalogue reason, and work the list
 * of rejected tubes (unreject when the tenant allows reinstatement, replace with a new barcode, BR-18). Every write
 * reloads the lookup and the list.
 */
@Injectable()
export class RejectionsFacade {
  private readonly api = inject(SamplesApi);
  private readonly exporter = inject(SampleListExport);
  readonly labels = inject(SampleLabelPrinter);

  readonly lookupNumber = signal<string | null>(null);

  readonly lookup = resource({
    params: () => this.lookupNumber() ?? undefined,
    loader: ({ params, abortSignal }) => this.api.byNumber(params, abortSignal),
  });

  readonly filter = signal<RejectedFilter>({ fromDate: null, toDate: null, page: 1, pageSize: 50 });

  readonly listFilter = computed<SampleListFilter>(() => {
    const filter = this.filter();
    return {
      visitId: null,
      accession: null,
      status: 'Rejected',
      from: filter.fromDate ? localMidnight(filter.fromDate, 0) : null,
      to: filter.toDate ? localMidnight(filter.toDate, 1) : null,
      branchId: null,
      clinicalGroupId: null,
      search: null,
      page: filter.page,
      pageSize: filter.pageSize,
    };
  });

  readonly rejected = resource({
    params: () => this.listFilter(),
    loader: ({ params, abortSignal }) => this.api.list(params, abortSignal),
  });

  readonly reasons = resource({
    loader: ({ abortSignal }) => this.api.rejectionReasons(abortSignal),
  });

  readonly settings = resource({
    loader: ({ abortSignal }) => this.api.settings(abortSignal),
  });

  /** BR-18: reinstating the same barcode is offered only when the tenant does not require a new one. */
  readonly reinstatementAllowed = computed(() => (this.settings.hasValue() ? !this.settings.value().newBarcodeOnReject : false));

  /** The new sample of the last replacement, for the "print its label" strip. */
  readonly replacement = signal<SampleDetail | null>(null);

  readonly reject = mutation((input: { id: string; reasonId: string; note: string | null }) => this.api.reject(input.id, input.reasonId, input.note), { then: () => this.reloadAll() });

  readonly unreject = mutation((input: { id: string; note: string | null }) => this.api.unreject(input.id, input.note), { then: () => this.reloadAll() });

  readonly replace = mutation(
    async (input: { id: string; note: string | null }): Promise<SampleDetail> => {
      const created: CreatedResource = await this.api.replace(input.id, input.note);
      return this.api.get(created.id);
    },
    {
      then: (detail) => {
        this.replacement.set(detail);
        this.reloadAll();
      },
    },
  );

  readonly printLabel = mutation((sampleId: string) => this.labels.printSample(sampleId));

  readonly exporting = mutation((fileName: string) => this.exporter.exportFilter({ ...this.listFilter(), page: 1 }, fileName));

  find(raw: string): void {
    const number = normalizeSampleNumber(raw);
    this.lookupNumber.set(number.length === 0 ? null : number);
    this.replacement.set(null);
  }

  setDates(fromDate: string | null, toDate: string | null): void {
    this.filter.update((current) => ({ ...current, fromDate: fromDate || null, toDate: toDate || null, page: 1 }));
  }

  setPage(page: PageRequest): void {
    this.filter.update((current) => ({ ...current, page: page.page, pageSize: page.pageSize }));
  }

  dismissReplacement(): void {
    this.replacement.set(null);
  }

  private reloadAll(): void {
    this.rejected.reload();
    if (this.lookupNumber() !== null) {
      this.lookup.reload();
    }
  }
}

/** Local midnight of a `yyyy-MM-dd` date plus `addDays`, as an ISO instant. */
function localMidnight(date: string, addDays: number): string | null {
  const [year, month, day] = date.split('-').map((part) => Number.parseInt(part, 10));
  if (!year || !month || !day) {
    return null;
  }

  return new Date(year, month - 1, day + addDays).toISOString();
}
