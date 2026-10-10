import { toCsv } from './csv-export';

interface Row {
  readonly number: string;
  readonly name: string | null;
  readonly count: number;
}

describe('toCsv (CSV export of the sample registers)', () => {
  const columns = [
    { header: 'Number', value: (row: Row) => row.number },
    { header: 'Name', value: (row: Row) => row.name },
    { header: 'Count', value: (row: Row) => row.count },
  ];

  it('writes a header and one CRLF-separated line per row, quoting what needs quoting', () => {
    const csv = toCsv<Row>(
      [
        { number: 'SMP-2026-00001', name: 'Mona, "the" patient', count: 2 },
        { number: 'SMP-2026-00002', name: null, count: 0 },
      ],
      columns,
    );

    expect(csv.split('\r\n')).toEqual(['Number,Name,Count', 'SMP-2026-00001,"Mona, ""the"" patient",2', 'SMP-2026-00002,,0']);
  });

  it('neutralises text a spreadsheet would run as a formula and keeps Arabic as is', () => {
    const csv = toCsv<Row>([{ number: '=HYPERLINK("x")', name: 'منى عادل', count: -1 }], columns);

    expect(csv.split('\r\n')[1]).toBe(`"'=HYPERLINK(""x"")",منى عادل,-1`);
  });
});
