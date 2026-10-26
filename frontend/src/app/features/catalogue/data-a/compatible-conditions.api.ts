import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import {
  COMPATIBLE_CONDITIONS_API,
  type CompatibleSampleTypeView,
  type SampleConditionPairItem,
  type SaveCompatibleConditionsRequest,
} from '../../../core/api/compatible-conditions.contracts';

/** Compatible sample conditions (LIS-CR-015 M4): the list of every sample type with its map, and the save of one type's map. */
@Injectable({ providedIn: 'root' })
export class CompatibleConditionsApi {
  private readonly http = inject(HttpClient);

  list(signal?: AbortSignal): Promise<readonly CompatibleSampleTypeView[]> {
    return firstValueFrom(this.http.get<readonly CompatibleSampleTypeView[]>(COMPATIBLE_CONDITIONS_API.list), { defaultValue: [] }).then(abortable(signal));
  }

  /** A 204 arrives as a null body; it resolves to undefined. */
  save(sampleTypeId: string, pairs: readonly SampleConditionPairItem[]): Promise<void> {
    const body: SaveCompatibleConditionsRequest = { pairs };
    return firstValueFrom(this.http.put<void>(COMPATIBLE_CONDITIONS_API.save(sampleTypeId), body), { defaultValue: undefined }).then(() => undefined);
  }
}

/** Resources pass an abort signal; a late result for an aborted load is dropped. */
function abortable<T>(signal: AbortSignal | undefined): (value: T) => T {
  return (value) => {
    if (signal?.aborted) {
      throw new DOMException('aborted', 'AbortError');
    }

    return value;
  };
}
