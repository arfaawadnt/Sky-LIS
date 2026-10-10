import { Injectable, inject, resource, signal } from '@angular/core';

import type { SampleTimeline } from '../../../core/api/samples.contracts';
import { SamplesApi } from '../data/samples.api';
import { normalizeSampleNumber } from '../ui/sample-format';

/** CP-17 Sample tracking (01 §6.3): the sample of a barcode with its timeline and stage durations. */
@Injectable()
export class TrackingFacade {
  private readonly api = inject(SamplesApi);

  readonly sampleNumber = signal<string | null>(null);

  readonly timeline = resource<SampleTimeline, string | undefined>({
    params: () => this.sampleNumber() ?? undefined,
    loader: async ({ params, abortSignal }) => {
      const sample = await this.api.byNumber(params, abortSignal);
      return this.api.timeline(sample.summary.id, abortSignal);
    },
  });

  track(raw: string | null | undefined): void {
    const number = normalizeSampleNumber(raw ?? '');
    this.sampleNumber.set(number.length === 0 ? null : number);
  }
}
