import type { DownloadedFile } from './patients.api';

/** How long an object URL stays valid after it was handed to the browser. */
const REVOKE_AFTER_MS = 60_000;

/**
 * Hands a downloaded file to the browser's save flow. Downloads go through HttpClient (the bearer token lives in
 * memory only, D-01), so a plain link cannot be used; an object URL of the blob is clicked instead and revoked later.
 */
export function saveFile(file: DownloadedFile, target: Document = document): void {
  const url = URL.createObjectURL(file.blob);
  const anchor = target.createElement('a');
  anchor.href = url;
  anchor.download = file.fileName;
  anchor.rel = 'noopener';
  anchor.style.display = 'none';
  target.body.append(anchor);
  anchor.click();
  anchor.remove();
  setTimeout(() => URL.revokeObjectURL(url), REVOKE_AFTER_MS);
}

/** Opens a PDF or an image in a new tab (the browser renders the detected content type inline). */
export function openFile(file: DownloadedFile, view: Window = window): void {
  const url = URL.createObjectURL(file.blob);
  view.open(url, '_blank', 'noopener');
  setTimeout(() => URL.revokeObjectURL(url), REVOKE_AFTER_MS);
}
