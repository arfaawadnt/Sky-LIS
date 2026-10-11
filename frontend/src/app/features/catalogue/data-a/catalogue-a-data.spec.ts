import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { CATALOGUE_A_API } from '../../../core/api/catalogue-a.contracts';
import { CatalogueAApi } from './catalogue-a.api';
import { daysToMask, maskToDays, numberOrNull, textOrNull } from './catalogue-a.mapping';
import { csvCell, csvFileName, toCsv, yesNo } from './csv-export';

describe('catalogue A data helpers', () => {
  it('writes CSV cells with quotes, doubled quotes and a guard against spreadsheet formulas', () => {
    expect(csvCell(null)).toBe('');
    expect(csvCell(12.5)).toBe('12.5');
    expect(csvCell(-3)).toBe('-3');
    expect(csvCell(true)).toBe('true');
    expect(csvCell('Glucose, fasting')).toBe('"Glucose, fasting"');
    expect(csvCell('say "hi"')).toBe('"say ""hi"""');
    expect(csvCell('=HYPERLINK("x")')).toBe(`"'=HYPERLINK(""x"")"`);
    expect(csvCell('@SUM(A1)')).toBe("'@SUM(A1)");
    expect(csvCell('جلوكوز')).toBe('جلوكوز');
  });

  it('builds the file with a byte-order mark, a header row and CRLF lines', () => {
    const csv = toCsv([{ code: 'GLU', name: 'Glucose' }], [
      { header: 'Code', value: (row) => row.code },
      { header: 'Name', value: (row) => row.name },
    ]);

    expect(csv.charCodeAt(0)).toBe(0xfeff);
    expect(csv.slice(1)).toBe('Code,Name\r\nGLU,Glucose\r\n');
    expect(csvFileName('tests', new Date('2026-10-09T10:00:00Z'))).toBe('tests-2026-10-09.csv');
  });

  it('turns the weekday mask into seven flags and back (bit 0 = Sunday)', () => {
    expect(maskToDays(127)).toEqual([true, true, true, true, true, true, true]);
    expect(maskToDays(0b0011111)).toEqual([true, true, true, true, true, false, false]);
    expect(daysToMask([true, false, false, false, false, false, true])).toBe(65);
    expect(daysToMask(maskToDays(42))).toBe(42);
  });

  it('sends empty texts and numbers as null', () => {
    expect(textOrNull('  ')).toBeNull();
    expect(textOrNull(' mg/dL ')).toBe('mg/dL');
    expect(numberOrNull('')).toBeNull();
    expect(numberOrNull(null)).toBeNull();
    expect(numberOrNull(0)).toBe(0);
    expect(numberOrNull('12.5')).toBe(12.5);
    expect(numberOrNull('abc')).toBeNull();
  });
});

describe('catalogue A exports (UAT C-04 / B-09)', () => {
  it('asks the server for the rows of the grid: search, status, type and group', async () => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()] });
    const api = TestBed.inject(CatalogueAApi);
    const backend = TestBed.inject(HttpTestingController);

    const groups = api.exportClinicalGroups({ search: ' V11 ', isActive: true });
    const groupsRequest = backend.expectOne((request) => request.url === `${CATALOGUE_A_API.clinicalGroups}/export`);
    expect(groupsRequest.request.params.get('search')).toBe('V11');
    expect(groupsRequest.request.params.get('isActive')).toBe('true');
    groupsRequest.flush([]);
    await groups;

    const tests = api.exportTests({ search: '', isActive: null }, 'Numeric', 'g1');
    const testsRequest = backend.expectOne((request) => request.url === `${CATALOGUE_A_API.tests}/export`);
    expect(testsRequest.request.params.has('search')).toBe(false);
    expect(testsRequest.request.params.has('isActive')).toBe(false);
    expect(testsRequest.request.params.get('type')).toBe('Numeric');
    expect(testsRequest.request.params.get('clinicalGroupId')).toBe('g1');
    testsRequest.flush([]);
    await tests;
    backend.verify();
  });

  it('writes flags as the translated Yes / No and dates the file in the local calendar', () => {
    expect(yesNo((key) => `t:${key}`, true)).toBe('t:shared.yes');
    expect(yesNo((key) => `t:${key}`, false)).toBe('t:shared.no');
    expect(csvFileName('clinical-groups', new Date(2026, 9, 11, 23, 30))).toBe('clinical-groups-2026-10-11.csv');
  });
});
