import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import type { Paged } from '../../../core/api/paging';
import { type CatalogueServiceLookupItem, PRICING_API, type ServiceLabel } from '../../../core/api/pricing.contracts';

/** What the catalogue lookup answers: a `Paged` envelope (a plain array is accepted too). */
type LookupResponse = readonly CatalogueServiceLookupItem[] | Paged<CatalogueServiceLookupItem>;

/** The catalogue picker's page size limit (Catalogue A: at most 50). */
const SEARCH_PAGE_SIZE = 20;

/**
 * The catalogue's service picker as the pricing typeahead uses it (`GET /api/v1/catalogue/lookups/services?search=`,
 * Catalogue A, `catalogue.view`). Only for adding services: the grids get codes and names from the pricing DTOs.
 */
@Injectable({ providedIn: 'root' })
export class ServiceCatalogApi {
  private readonly http = inject(HttpClient);

  async search(term: string): Promise<readonly ServiceLabel[]> {
    const params = new HttpParams({ fromObject: { search: term.trim(), pageSize: SEARCH_PAGE_SIZE } });
    const response = await firstValueFrom(this.http.get<LookupResponse>(PRICING_API.catalogueServices, { params }), { defaultValue: [] });
    return normalize(response);
  }
}

/** Accepts both response shapes and both code spellings; drops items without an id. */
export function normalize(response: LookupResponse | null): readonly ServiceLabel[] {
  const items: readonly CatalogueServiceLookupItem[] = response === null ? [] : isPaged(response) ? response.items : response;
  return items
    .filter((item) => typeof item.id === 'string' && item.id.length > 0)
    .map((item) => ({
      id: item.id,
      code: item.code ?? item.shortName ?? '',
      nameEn: item.nameEn ?? item.nameAr ?? '',
      nameAr: item.nameAr ?? item.nameEn ?? '',
    }));
}

function isPaged(response: LookupResponse): response is Paged<CatalogueServiceLookupItem> {
  return !Array.isArray(response) && typeof response === 'object' && 'items' in response;
}
