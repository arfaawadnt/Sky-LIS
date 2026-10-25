/**
 * Announcements of National Technology and its partners (change request LIS-CR-013, C-17; SA-22), mirrored from
 * `Platform.Contracts.PlatformAdmin.AnnouncementContracts`. Times are ISO-8601 UTC strings.
 */

import type { SortRequest } from './paging';

export type AnnouncementPlacement = 'SignIn' | 'Welcome';
export const ANNOUNCEMENT_PLACEMENTS: readonly AnnouncementPlacement[] = ['SignIn', 'Welcome'];

/** `Any` = Trial or Active; an inactive client never sees an announcement. */
export type AnnouncementAudienceStatus = 'Any' | 'Trial' | 'Active';
export const ANNOUNCEMENT_AUDIENCE_STATUSES: readonly AnnouncementAudienceStatus[] = ['Any', 'Trial', 'Active'];

/** Create or edit (SA-22). An empty client list = every client the other criteria admit. The image is uploaded apart. */
export interface AnnouncementRequest {
  readonly titleEn: string;
  readonly titleAr: string;
  readonly bodyEn: string | null;
  readonly bodyAr: string | null;
  readonly linkUrl: string | null;
  readonly placement: AnnouncementPlacement;
  readonly fromUtc: string;
  readonly toUtc: string | null;
  readonly audienceCountryId: string | null;
  readonly audiencePlanId: string | null;
  readonly audienceStatus: AnnouncementAudienceStatus;
  readonly audienceClientIds: readonly string[];
}

/** One announcement as the SA-22 register and editor show it; `imageVersion` is null without an image. */
export interface AnnouncementView {
  readonly id: string;
  readonly titleEn: string;
  readonly titleAr: string;
  readonly bodyEn: string | null;
  readonly bodyAr: string | null;
  readonly linkUrl: string | null;
  readonly placement: AnnouncementPlacement;
  readonly fromUtc: string;
  readonly toUtc: string | null;
  readonly audienceCountryId: string | null;
  readonly audiencePlanId: string | null;
  readonly audienceStatus: AnnouncementAudienceStatus;
  readonly audienceClientIds: readonly string[];
  readonly isActive: boolean;
  /** Active and inside its schedule now. */
  readonly isLive: boolean;
  readonly imageVersion: number | null;
  readonly impressions: number;
  readonly clicks: number;
  readonly createdAtUtc: string;
}

export interface AnnouncementImageView {
  readonly imageVersion: number;
  readonly contentType: string;
  readonly sizeBytes: number;
}

/** The SA-22 register query; `sort` is a key of `AnnouncementSortKeys` on the server. */
export interface AnnouncementFilter extends SortRequest {
  readonly search: string | null;
  readonly placement: AnnouncementPlacement | null;
  readonly isActive: boolean | null;
  readonly page: number;
  readonly pageSize: number;
}

/** What a laboratory is shown (sign-in hero, dashboard welcome card); nothing about the audience is published. */
export interface PublicAnnouncement {
  readonly id: string;
  readonly titleEn: string;
  readonly titleAr: string;
  readonly bodyEn: string | null;
  readonly bodyAr: string | null;
  readonly linkUrl: string | null;
  readonly imageVersion: number | null;
}

/** SA-04 "Hide announcements for this client". */
export interface ClientAnnouncementsSetting {
  readonly hidden: boolean;
}

/** The image limit of the server (512 KB, PNG / JPEG / WebP decided by the file's bytes). */
export const ANNOUNCEMENT_IMAGE_MAX_BYTES = 512 * 1024;
export const ANNOUNCEMENT_TITLE_MAX = 120;
export const ANNOUNCEMENT_BODY_MAX = 600;
export const ANNOUNCEMENT_LINK_MAX = 500;

export const ANNOUNCEMENT_API = {
  admin: '/api/v1/platform/announcements',
  adminOne: (id: string): string => `/api/v1/platform/announcements/${encodeURIComponent(id)}`,
  adminImage: (id: string): string => `/api/v1/platform/announcements/${encodeURIComponent(id)}/image`,
  clientSetting: (clientId: string): string => `/api/v1/platform/clients/${encodeURIComponent(clientId)}/announcements`,
  /** Anonymous: the live SignIn announcements of a laboratory address (browsers may cache it five minutes). */
  publicList: '/api/v1/public/announcements',
  /** Anonymous image of a live announcement; the version only busts the browser cache. */
  publicImage: (id: string, version: number): string => `/api/v1/public/announcements/${encodeURIComponent(id)}/image?v=${version}`,
  impression: (id: string): string => `/api/v1/public/announcements/${encodeURIComponent(id)}/impression`,
  click: (id: string): string => `/api/v1/public/announcements/${encodeURIComponent(id)}/click`,
  /** Authenticated: the Welcome announcements of the session's laboratory. */
  mine: '/api/v1/account/announcements',
} as const;

/** The title / body / link of an announcement in the interface language (Arabic falls back to English when empty). */
export function announcementText(announcement: PublicAnnouncement, lang: string): { readonly title: string; readonly body: string | null } {
  const arabic = lang === 'ar';
  const title = arabic && announcement.titleAr.trim() !== '' ? announcement.titleAr : announcement.titleEn;
  const body = arabic ? (announcement.bodyAr ?? announcement.bodyEn) : (announcement.bodyEn ?? announcement.bodyAr);
  return { title, body };
}
