import { Injectable, inject, resource, signal } from '@angular/core';

import {
  type CreateLabTestRequest,
  type FormulaOperandChoice,
  type FormulaOperandItem,
  type LabTestGeneral,
  type LabTestTiming,
  type ListValueItem,
  type ParagraphItem,
  type RangeBandItem,
  type ServiceLookupItem,
  type TestLookupItem,
  serviceTakesTestsFreely,
} from '../../../core/api/catalogue-a.contracts';
import { mutation } from '../../../core/state/mutation';
import { CatalogueAApi } from '../data-a/catalogue-a.api';

/**
 * The test editor (CP-41): the loaded detail and one write per configuration (general, timing, range bands, list
 * values, formula, paragraphs, parent services), the formula check, the test-reference picker and the parent-service
 * picker. Provided at the editor route.
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

  /** The search of the parent-service picker of the general tab (null = closed): only profiles and cultures are offered. */
  readonly parentSearch = signal<string | null>(null);

  readonly parentCandidates = resource({
    params: () => this.parentSearch() ?? undefined,
    loader: async ({ params, abortSignal }): Promise<readonly ServiceLookupItem[]> =>
      (await this.api.lookupServices(params, null, abortSignal)).items.filter((item) => serviceTakesTestsFreely(item.type)),
  });

  readonly create = mutation((request: CreateLabTestRequest) => this.api.createTest(request));

  readonly update = mutation((input: { readonly id: string; readonly request: LabTestGeneral }) => this.api.updateTest(input.id, input.request));

  readonly timing = mutation((input: { readonly id: string; readonly timing: LabTestTiming }) => this.api.setTestTiming(input.id, input.timing));

  readonly bands = mutation((input: { readonly id: string; readonly bands: readonly RangeBandItem[] }) => this.api.replaceRangeBands(input.id, input.bands));

  readonly values = mutation((input: { readonly id: string; readonly values: readonly ListValueItem[] }) => this.api.replaceListValues(input.id, input.values));

  readonly formula = mutation((input: { readonly id: string; readonly expression: string | null; readonly operands?: readonly FormulaOperandItem[] | null }) =>
    this.api.setFormula(input.id, input.expression, input.operands ?? null),
  );

  readonly paragraphs = mutation((input: { readonly id: string; readonly paragraphs: readonly ParagraphItem[] }) => this.api.replaceParagraphs(input.id, input.paragraphs));

  readonly parents = mutation((input: { readonly id: string; readonly serviceIds: readonly string[] }) => this.api.setParentServices(input.id, input.serviceIds));

  readonly check = mutation((input: { readonly expression: string; readonly testId: string | null }) => this.api.checkFormula(input.expression, input.testId));

  readonly setActive = mutation((input: { readonly id: string; readonly active: boolean }) => this.api.setTestActive(input.id, input.active), {
    then: () => this.detail.reload(),
  });

  /** Opens a record; the detail is re-read even for the id already loaded, because the facade outlives the page (UAT D-14). */
  load(id: string | null): void {
    this.id.set(id);
    this.detail.reload();
    this.references.reload();
    this.parentCandidates.reload();
  }

  /**
   * The operand choices of a stored formula (LIS-CR-015): the check answers, per reference, the services that may be ordered for it.
   * Read on opening a formula test, so the operand table shows without a click; an unreadable answer is no choices (the table stays empty).
   */
  async operandChoices(expression: string, testId: string | null): Promise<readonly FormulaOperandChoice[]> {
    try {
      const result = await this.api.checkFormula(expression, testId);
      return result.valid ? result.operands : [];
    } catch {
      return [];
    }
  }

  searchReferences(search: string): void {
    this.referenceSearch.set(search);
  }

  searchParents(search: string): void {
    this.parentSearch.set(search);
  }
}
