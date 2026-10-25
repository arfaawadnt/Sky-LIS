import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import {
  ANNOUNCEMENT_API,
  type AnnouncementFilter,
  type AnnouncementImageView,
  type AnnouncementRequest,
  type AnnouncementView,
  type ClientAnnouncementsSetting,
} from '../../../core/api/announcement.contracts';
import type { CreatedResource } from '../../../core/api/created-resource';
import type { Paged } from '../../../core/api/paging';
import { toHttpParams } from '../../../shared/forms/paging';

/** SA-22 Announcements and the SA-04 opt-out (LIS-CR-013, C-17); URLs through `HttpParams` (pitfall P-60). */
@Injectable({ providedIn: 'root' })
export class AnnouncementsApi {
  private readonly http = inject(HttpClient);

  list(filter: AnnouncementFilter, signal?: AbortSignal): Promise<Paged<AnnouncementView>> {
    return firstValueFrom(this.http.get<Paged<AnnouncementView>>(ANNOUNCEMENT_API.admin, { params: toHttpParams({ ...filter }) })).then((page) => {
      if (signal?.aborted) {
        throw new DOMException('aborted', 'AbortError');   // a late answer of a superseded load is dropped
      }

      return page;
    });
  }

  get(id: string): Promise<AnnouncementView> {
    return firstValueFrom(this.http.get<AnnouncementView>(ANNOUNCEMENT_API.adminOne(id)));
  }

  create(request: AnnouncementRequest): Promise<CreatedResource> {
    return firstValueFrom(this.http.post<CreatedResource>(ANNOUNCEMENT_API.admin, request));
  }

  async update(id: string, request: AnnouncementRequest): Promise<void> {
    await firstValueFrom(this.http.put(ANNOUNCEMENT_API.adminOne(id), request));
  }

  async setActive(id: string, active: boolean): Promise<void> {
    await firstValueFrom(this.http.post(`${ANNOUNCEMENT_API.adminOne(id)}/${active ? 'activate' : 'deactivate'}`, null));
  }

  /** The image for the editor's preview: it needs the bearer token, so it is read as a blob (shown through an object URL). */
  image(id: string): Promise<Blob> {
    return firstValueFrom(this.http.get(ANNOUNCEMENT_API.adminImage(id), { responseType: 'blob' }));
  }

  uploadImage(id: string, file: File): Promise<AnnouncementImageView> {
    const form = new FormData();
    form.append('file', file, file.name);
    return firstValueFrom(this.http.post<AnnouncementImageView>(ANNOUNCEMENT_API.adminImage(id), form));
  }

  async removeImage(id: string): Promise<void> {
    await firstValueFrom(this.http.delete(ANNOUNCEMENT_API.adminImage(id)));
  }

  clientSetting(clientId: string): Promise<ClientAnnouncementsSetting> {
    return firstValueFrom(this.http.get<ClientAnnouncementsSetting>(ANNOUNCEMENT_API.clientSetting(clientId)));
  }

  async setClientHidden(clientId: string, hidden: boolean): Promise<void> {
    await firstValueFrom(this.http.put(ANNOUNCEMENT_API.clientSetting(clientId), { hidden } satisfies ClientAnnouncementsSetting));
  }
}
