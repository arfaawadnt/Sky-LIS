/**
 * Report designs (LIS-CR-010, reviewer point C-14), mirroring `Platform.Domain.Catalogue.ReportDesign`: the layout of a
 * printed result report. The laboratory parameter `reportDesign` stores the lower-case name; a report template
 * (`ReportTemplateListItem.design`) and a payer contract tier (`ContractPricingDetail.reportDesign`) store the member name
 * as an override. Selection order: contract → template → laboratory (the server resolves it; `LaboratoryReportDocument.design`).
 */
export type ReportDesign = 'Classic' | 'Modern' | 'Compact' | 'Letterhead' | 'Cumulative';

/** Which level chose the design of a report. */
export type ReportDesignSource = 'Laboratory' | 'Template' | 'Contract';

export const REPORT_DESIGNS: readonly ReportDesign[] = ['Classic', 'Modern', 'Compact', 'Letterhead', 'Cumulative'];

/** The values of the laboratory parameter, in display order. */
export const REPORT_DESIGN_SETTING_VALUES: readonly string[] = REPORT_DESIGNS.map((design) => design.toLowerCase());

/** The design of a stored value (member or parameter form, any case); anything unknown or missing is Classic, as on the server. */
export function reportDesignOf(value: string | null | undefined): ReportDesign {
  const wanted = (value ?? '').trim().toLowerCase();
  return REPORT_DESIGNS.find((design) => design.toLowerCase() === wanted) ?? 'Classic';
}

/** An override as stored: the member name, or null when the value is empty or unknown (follow the next level). */
export function reportDesignOverride(value: string | null | undefined): ReportDesign | null {
  const wanted = (value ?? '').trim().toLowerCase();
  return REPORT_DESIGNS.find((design) => design.toLowerCase() === wanted) ?? null;
}
