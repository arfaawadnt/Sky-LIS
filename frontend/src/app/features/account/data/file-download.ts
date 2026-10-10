import type { DownloadedFile } from './account.api';

/**
 * Hands a generated file to the browser's download (the CSV export of CP-68): a temporary object URL on a hidden
 * anchor, clicked once and revoked. Nothing is stored by the application.
 */
export function saveFile(document: Document, file: DownloadedFile): void {
  const view = document.defaultView;
  if (!view) {
    return;
  }

  const url = view.URL.createObjectURL(file.blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = file.fileName;
  anchor.rel = 'noopener';
  anchor.style.display = 'none';
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  view.setTimeout(() => view.URL.revokeObjectURL(url), 0);
}
