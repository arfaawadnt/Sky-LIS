import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { ANNOUNCEMENT_API, type PublicAnnouncement } from '../../../core/api/announcement.contracts';

/**
 * LIS-CR-013 (C-17): the Welcome announcements of the session's laboratory for the dashboard card, and the two counters the card
 * reports (the public endpoints; a failed counter is ignored).
 */
@Injectable({ providedIn: 'root' })
export class AccountAnnouncementsApi {
  private readonly http = inject(HttpClient);

  mine(): Promise<readonly PublicAnnouncement[]> {
    return firstValueFrom(this.http.get<readonly PublicAnnouncement[]>(ANNOUNCEMENT_API.mine));
  }

  impression(id: string): void {
    void firstValueFrom(this.http.post(ANNOUNCEMENT_API.impression(id), null)).catch(() => undefined);
  }

  click(id: string): void {
    void firstValueFrom(this.http.post(ANNOUNCEMENT_API.click(id), null)).catch(() => undefined);
  }
}
