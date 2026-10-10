import { Injectable, computed, inject, resource, signal } from '@angular/core';

import type { ConversionDto, MappingListItem, RangeOverrideDto, SaveMappingRequest, SaveSampleCodeRequest, TestLookupOption } from '../../../core/api/integration.contracts';
import { emptyPage, type PageRequest } from '../../../core/api/paging';
import { mutation } from '../../../core/state/mutation';
import { IntegrationApi } from '../data/integration.api';

/**
 * Analyser tests (CP-59) and analyser samples (CP-60): one analyser at a time (the route's `analyserId` query parameter),
 * its mappings with inline edits, Auto Send, conversion table, range override and code sheet, and its sample codes.
 */
@Injectable()
export class AnalyserTestsFacade {
  private readonly api = inject(IntegrationApi);

  readonly analyserId = signal<string | null>(null);

  readonly filter = signal<{ search: string; page: number; pageSize: number }>({ search: '', page: 1, pageSize: 50 });

  readonly analysers = resource({ loader: ({ abortSignal }) => this.api.analyserOptions(abortSignal) });

  readonly analyser = computed(() => (this.analysers.hasValue() ? (this.analysers.value().items.find((item) => item.id === this.analyserId()) ?? null) : null));

  readonly mappings = resource({
    params: () => ({ analyserId: this.analyserId(), ...this.filter() }),
    loader: ({ params, abortSignal }) =>
      params.analyserId === null ? Promise.resolve(emptyPage<MappingListItem>()) : this.api.listMappings(params.analyserId, params.search, params.page, params.pageSize, abortSignal),
  });

  readonly sampleCodes = resource({
    params: () => ({ analyserId: this.analyserId() }),
    loader: ({ params, abortSignal }) => (params.analyserId === null ? Promise.resolve([]) : this.api.listSampleCodes(params.analyserId, abortSignal)),
  });

  readonly units = resource({ loader: ({ abortSignal }) => this.api.units(abortSignal) });

  readonly sampleTypes = resource({ loader: ({ abortSignal }) => this.api.sampleTypes(abortSignal) });

  readonly save = mutation(
    (input: { mappingId: string | null; request: SaveMappingRequest }) => {
      const analyserId = this.requireAnalyser();
      return input.mappingId === null ? this.api.mapTest(analyserId, input.request).then((created) => created.id) : this.api.updateMapping(analyserId, input.mappingId, input.request).then(() => input.mappingId ?? '');
    },
    { then: () => this.mappings.reload() },
  );

  readonly autoSend = mutation((input: { mappingId: string; enabled: boolean }) => this.api.setAutoSend(this.requireAnalyser(), input.mappingId, input.enabled), { then: () => this.mappings.reload() });

  readonly conversions = mutation((input: { mappingId: string; conversions: readonly ConversionDto[] }) => this.api.replaceConversions(this.requireAnalyser(), input.mappingId, input.conversions), {
    then: () => this.mappings.reload(),
  });

  readonly range = mutation((input: { mappingId: string; range: RangeOverrideDto | null }) => this.api.setRangeOverride(this.requireAnalyser(), input.mappingId, input.range), { then: () => this.mappings.reload() });

  readonly setMappingActive = mutation((input: { mappingId: string; active: boolean }) => this.api.setMappingActive(this.requireAnalyser(), input.mappingId, input.active), {
    then: () => this.mappings.reload(),
  });

  readonly codeSheet = mutation((analyserId: string) => this.api.codeSheet(analyserId));

  readonly addSampleCode = mutation((request: SaveSampleCodeRequest) => this.api.addSampleCode(this.requireAnalyser(), request), { then: () => this.sampleCodes.reload() });

  readonly changeSampleCode = mutation((input: { sampleCodeId: string; request: SaveSampleCodeRequest }) => this.api.changeSampleCode(this.requireAnalyser(), input.sampleCodeId, input.request), {
    then: () => this.sampleCodes.reload(),
  });

  readonly setSampleCodeActive = mutation((input: { sampleCodeId: string; active: boolean }) => this.api.setSampleCodeActive(this.requireAnalyser(), input.sampleCodeId, input.active), {
    then: () => this.sampleCodes.reload(),
  });

  selectAnalyser(analyserId: string | null): void {
    this.analyserId.set(analyserId === '' ? null : analyserId);
    this.filter.update((current) => ({ ...current, page: 1 }));
  }

  setSearch(search: string): void {
    this.filter.update((current) => ({ ...current, search, page: 1 }));
  }

  setPage(page: PageRequest): void {
    this.filter.update((current) => ({ ...current, page: page.page, pageSize: page.pageSize }));
  }

  searchTests(text: string): Promise<readonly TestLookupOption[]> {
    return this.api.searchTests(text);
  }

  private requireAnalyser(): string {
    const id = this.analyserId();
    if (id === null) {
      throw new Error('Choose an analyser first.');
    }

    return id;
  }
}
