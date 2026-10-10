import { Injectable, inject, resource, signal } from '@angular/core';

import type { CreateLabTestRequest, LabTestGeneral, LabTestTiming, ListValueItem, ParagraphItem, RangeBandItem, TestLookupItem } from '../../../core/api/catalogue-a.contracts';
import { mutation } from '../../../core/state/mutation';
import { CatalogueAApi } from '../data-a/catalogue-a.api';

/**
 * The test editor (CP-41): the loaded detail and one write per configuration (general, timing, range bands, list
 * values, formula, paragraphs), the formula check and the test-reference picker. Provided at the editor route.
 */
@Injectable()
export class TestEditorFacade {
  private readonly api = inject(CatalogueAApi);

  /** The test being edited, or null for a new one. */
  readonly id = signal<string | null>(null);

  readonly detail = resource({
    params: () => this.id() ?? undefined,
    loader: ({ params }) => this.api.getTest(params),
  });

  /** The search of the "insert test reference" picker of the formula tab (null = closed). */
  readonly referenceSearch = signal<string | null>(null);

  readonly references = resource({
    params: () => this.referenceSearch() ?? undefined,
    loader: async ({ params, abortSignal }): Promise<readonly TestLookupItem[]> => (await this.api.lookupTests(params, null, abortSignal)).items,
  });

  readonly create = mutation((request: CreateLabTestRequest) => this.api.createTest(request));

  readonly update = mutation((input: { readonly id: string; readonly request: LabTestGeneral }) => this.api.updateTest(input.id, input.request));

  readonly timing = mutation((input: { readonly id: string; readonly timing: LabTestTiming }) => this.api.setTestTiming(input.id, input.timing));

  readonly bands = mutation((input: { readonly id: string; readonly bands: readonly RangeBandItem[] }) => this.api.replaceRangeBands(input.id, input.bands));

  readonly values = mutation((input: { readonly id: string; readonly values: readonly ListValueItem[] }) => this.api.replaceListValues(input.id, input.values));

  readonly formula = mutation((input: { readonly id: string; readonly expression: string | null }) => this.api.setFormula(input.id, input.expression));

  readonly paragraphs = mutation((input: { readonly id: string; readonly paragraphs: readonly ParagraphItem[] }) => this.api.replaceParagraphs(input.id, input.paragraphs));

  readonly check = mutation((input: { readonly expression: string; readonly testId: string | null }) => this.api.checkFormula(input.expression, input.testId));

  readonly setActive = mutation((input: { readonly id: string; readonly active: boolean }) => this.api.setTestActive(input.id, input.active), {
    then: () => this.detail.reload(),
  });

  /** Opens a record; the detail is re-read even for the id already loaded, because the facade outlives the page (UAT D-14). */
  load(id: string | null): void {
    this.id.set(id);
    this.detail.reload();
    this.references.reload();
  }

  searchReferences(search: string): void {
    this.referenceSearch.set(search);
  }
}
