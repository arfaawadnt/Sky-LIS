import { Injectable, computed, inject, resource, signal } from '@angular/core';

import { MAX_PAGE_SIZE } from '../../../core/api/paging';
import type { SampleListFilter, SampleListItem } from '../../../core/api/samples.contracts';
import { mutation } from '../../../core/state/mutation';
import { SamplesApi } from '../data/samples.api';
import { SampleLabelPrinter } from './sample-label-printer';
import { SampleListExport } from './sample-list-export';

/** What the page looks up: an accession typed or scanned, or a visit id handed over by another page. */
export interface VisitLookup {
  readonly accession: string | null;
  readonly visitId: string | null;
}

/**
 * CP-15 Samples by visit (01 §6.3): every tube of the visit (all generations, so a replaced tube shows with its
 * replacement), label printing (one / all, label size) and the CSV export.
 */
@Injectable()
export class VisitSamplesFacade {
  private readonly api = inject(SamplesApi);
  private readonly exporter = inject(SampleListExport);
  readonly labels = inject(SampleLabelPrinter);

  readonly lookup = signal<VisitLookup>({ accession: null, visitId: null });

  private readonly listFilter = computed<SampleListFilter | undefined>(() => {
    const { accession, visitId } = this.lookup();
    if (!accession && !visitId) {
      return undefined;
    }

    return { visitId, accession, status: null, from: null, to: null, branchId: null, clinicalGroupId: null, search: null, page: 1, pageSize: MAX_PAGE_SIZE };
  });

  readonly samples = resource({
    params: () => this.listFilter(),
    loader: ({ params, abortSignal }) => this.api.list(params, abortSignal),
  });

  readonly rows = computed<readonly SampleListItem[]>(() => (this.samples.hasValue() ? [...this.samples.value().items].sort(byUnitThenNumber) : []));

  /** The visit shown (from the first tube): accession, patient and id. */
  readonly visit = computed(() => {
    const first = this.rows()[0];
    return first ? { visitId: first.visitId, accession: first.accession, patientNumber: first.patientNumber, patientName: first.patientName, patientNameAr: first.patientNameAr } : null;
  });

  readonly printOne = mutation((sampleId: string) => this.labels.printSample(sampleId));

  readonly printAll = mutation((visitId: string) => this.labels.printVisit(visitId));

  readonly exporting = mutation((fileName: string) => Promise.resolve(this.exporter.exportRows(this.rows(), fileName)));

  show(lookup: VisitLookup): void {
    const accession = lookup.accession?.trim() ?? '';
    const visitId = lookup.visitId?.trim() ?? '';
    this.lookup.set({ accession: accession.length > 0 ? accession : null, visitId: visitId.length > 0 && accession.length === 0 ? visitId : null });
  }
}

function byUnitThenNumber(a: SampleListItem, b: SampleListItem): number {
  return (a.unitCode ?? '').localeCompare(b.unitCode ?? '') || a.sampleNumber.localeCompare(b.sampleNumber);
}
