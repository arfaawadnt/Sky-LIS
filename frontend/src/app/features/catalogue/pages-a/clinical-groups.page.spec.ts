import type { ClinicalGroupListItem } from '../../../core/api/catalogue-a.contracts';
import { clinicalGroupRequest, offersReportTemplate } from './clinical-groups.page';

const chemistry: ClinicalGroupListItem = { id: 'g1', code: 'CHEM', unitCode: 'C', nameEn: 'Chemistry', nameAr: 'الكيمياء', reportName: null, reportTemplateId: 'tpl-1', displayOrder: 1, isActive: true };

const typed = { code: 'CHEM', unitCode: 'C', name: { en: ' Chemistry ', ar: 'الكيمياء' }, reportName: '', displayOrder: 3 };

/** Validation v0.5.0 O-8: the client's report templates are Release 2, so its clinical group editor has no template field. */
describe('Clinical groups: report template field (O-8)', () => {
  it('is offered on the Super Admin portal (SA-12, Release 1) and on the client portal only from Release 2', () => {
    expect([offersReportTemplate('admin', 'R1'), offersReportTemplate('client', 'R1')]).toEqual([true, false]);
    expect(offersReportTemplate('client', 'R2')).toBe(true);
    expect(offersReportTemplate('client')).toBe(false);   // the delivered scope is Release 1
  });

  it('without the field, an edit keeps the template the group holds and a new group has none', () => {
    expect(clinicalGroupRequest(typed, chemistry, false)).toEqual({ code: 'CHEM', unitCode: 'C', nameEn: 'Chemistry', nameAr: 'الكيمياء', reportName: null, reportTemplateId: 'tpl-1', displayOrder: 3 });
    expect(clinicalGroupRequest(typed, null, false).reportTemplateId).toBeNull();
  });

  it('with the field, the chosen template is sent and an empty choice clears it', () => {
    expect(clinicalGroupRequest({ ...typed, reportTemplateId: 'tpl-2' }, chemistry, true).reportTemplateId).toBe('tpl-2');
    expect(clinicalGroupRequest({ ...typed, reportTemplateId: null }, chemistry, true).reportTemplateId).toBeNull();
  });
});
