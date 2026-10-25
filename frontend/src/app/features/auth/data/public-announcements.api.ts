import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { ANNOUNCEMENT_API, type PublicAnnouncement } from '../../../core/api/announcement.contracts';

/**
 * LIS-CR-013 (C-17): the anonymous SignIn announcements of a laboratory address and the two counters the sign-in page reports.
 * A failed read is no announcement (the slogan stays); a failed counter is ignored (it never blocks the page).
 */
@Injectable({ providedIn: 'root' })
export class PublicAnnouncementsApi {
  private readonly http = inject(HttpClient);

  forLaboratory(slug: string): Promise<readonly PublicAnnouncement[]> {
    const params = new HttpParams({ fromObject: { placement: 'SignIn', laboratory: slug } });
    return firstValueFrom(this.http.get<readonly PublicAnnouncement[]>(ANNOUNCEMENT_API.publicList, { params })).catch(() => []);
  }

  impression(id: string): void {
    void firstValueFrom(this.http.post(ANNOUNCEMENT_API.impression(id), null)).catch(() => undefined);
  }

  click(id: string): void {
    void firstValueFrom(this.http.post(ANNOUNCEMENT_API.click(id), null)).catch(() => undefined);
  }
}
