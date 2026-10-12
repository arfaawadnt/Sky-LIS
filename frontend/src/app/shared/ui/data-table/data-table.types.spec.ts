import { localizedSortKey, serverSortKey, serverSortOf, sortRows, type TableColumn } from './data-table.types';
import { sortParams } from '../../forms/paging';

interface Row {
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
}

const rows: readonly Row[] = [
  { code: 'B2', nameEn: 'Beta', nameAr: 'بيتا' },
  { code: 'A10', nameEn: 'Alpha', nameAr: 'ألفا' },
  { code: 'A9', nameEn: 'Gamma', nameAr: 'جاما' },
];

const columns: readonly TableColumn<Row>[] = [
  { key: 'code', header: 'code', sortable: true },
  { key: 'name', header: 'name', sortable: true, sortKey: localizedSortKey('name'), value: (row) => row.nameEn },
];

describe('data-table sorting helpers (validation v0.5.0 B-03)', () => {
  it('sortRows orders the whole list before a browser-side page is sliced', () => {
    const sorted = sortRows(rows, columns, { key: 'code', direction: 'asc' }, 'en');
    expect(sorted.map((row) => row.code)).toEqual(['A9', 'A10', 'B2']);   // numeric-aware
    expect(sortRows(rows, columns, { key: 'name', direction: 'desc' }, 'en').slice(0, 2).map((row) => row.nameEn)).toEqual(['Gamma', 'Beta']);
    expect(rows.map((row) => row.code)).toEqual(['B2', 'A10', 'A9']);   // the input is not mutated
  });

  it('sortRows compares a column by its sortValue when the cell text does not order (a formatted date)', () => {
    interface Session { readonly id: string; readonly sinceUtc: string }
    const sessions: readonly Session[] = [
      { id: 'b', sinceUtc: '2026-10-09T08:00:00Z' },
      { id: 'a', sinceUtc: '2026-10-10T07:00:00Z' },
      { id: 'c', sinceUtc: '2026-09-30T23:00:00Z' },
    ];
    const text = (iso: string): string => new Date(iso).toLocaleDateString('en-GB');   // "09/10/2026" sorts before "30/09/2026" as text
    const since: TableColumn<Session> = { key: 'since', header: 'since', sortable: true, value: (row) => text(row.sinceUtc), sortValue: (row) => row.sinceUtc };

    expect(sortRows(sessions, [since], { key: 'since', direction: 'asc' }, 'en').map((row) => row.id)).toEqual(['c', 'b', 'a']);
  });

  it('sortRows keeps the order without a sort or for an unknown column', () => {
    expect(sortRows(rows, columns, null, 'en')).toBe(rows);
    expect(sortRows(rows, columns, { key: 'missing', direction: 'asc' }, 'en')).toBe(rows);
  });

  it('a column names its server key, per language for a bilingual name', () => {
    const [code, name] = columns;
    expect(serverSortKey(code!, 'ar')).toBe('code');
    expect(serverSortKey(name!, 'en')).toBe('nameEn');
    expect(serverSortKey(name!, 'ar')).toBe('nameAr');
  });

  it('serverSortOf maps a table sort to the server key of a facade list, per language, and reads the language only when sorted', () => {
    const keys = { code: 'code', name: localizedSortKey('name') };
    let reads = 0;
    const lang = (value: string) => () => {
      reads += 1;
      return value;
    };
    expect(serverSortOf({ key: 'name', direction: 'desc' }, keys, lang('ar'))).toEqual({ key: 'nameAr', direction: 'desc' });
    expect(serverSortOf({ key: 'code', direction: 'asc' }, keys, lang('ar'))).toEqual({ key: 'code', direction: 'asc' });
    expect(serverSortOf({ key: 'unknown', direction: 'asc' }, keys, lang('en'))).toBeNull();
    const before = reads;
    expect(serverSortOf(null, keys, lang('en'))).toBeNull();
    expect(reads).toBe(before);
  });

  it('sortParams turns a server sort into the query pair, or two nulls that toHttpParams drops', () => {
    expect(sortParams({ key: 'nameAr', direction: 'desc' })).toEqual({ sort: 'nameAr', direction: 'desc' });
    expect(sortParams(null)).toEqual({ sort: null, direction: null });
  });
});
