import { allPages, csvFileName, toCsv } from './csv-export';

describe('organization CSV export (UAT B-10)', () => {
  it('writes a byte-order mark, quotes separators and neutralises formulas', () => {
    const csv = toCsv([{ name: 'North, East', code: '=SUM(A1)' }], [
      { header: 'Name', value: (row) => row.name },
      { header: 'Code', value: (row) => row.code },
    ]);

    expect(csv).toBe(`${String.fromCharCode(0xfeff)}Name,Code\r\n"North, East",'=SUM(A1)\r\n`);
  });

  it('dates the file in the local calendar', () => {
    expect(csvFileName('branches', new Date(2026, 9, 11, 23, 30))).toBe('branches-2026-10-11.csv');
  });

  it('reads every page of the filtered register', async () => {
    const pages = [1, 2, 3].map((page) => ({ items: Array.from({ length: page < 3 ? 200 : 5 }, (_, index) => `${page}-${index}`), page, pageSize: 200, totalCount: 405, totalPages: 3 }));
    const asked: number[] = [];
    const rows = await allPages((page, pageSize) => {
      asked.push(page);
      expect(pageSize).toBe(200);
      const answer = pages[page - 1];
      if (answer === undefined) {
        throw new Error('no such page');
      }

      return Promise.resolve(answer);
    });

    expect(asked).toEqual([1, 2, 3]);
    expect(rows.length).toBe(405);
  });
});
