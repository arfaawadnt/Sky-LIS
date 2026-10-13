import type { DownloadedFile } from './reporting.api';

/** Hands a file the server generated (the CSV export) to the browser's download through a temporary object URL (no-op without object URLs, e.g. tests). */
export function saveFile(document: Document, file: DownloadedFile): void {
  const view = document.defaultView;
  if (!view?.URL.createObjectURL) {
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
