using Platform.Application.Catalogue.Services;

namespace Platform.Infrastructure.Persistence.Catalogue;

/// <summary>
/// Registers the application's pure <see cref="FormulaEvaluator"/> as the <see cref="IFormulaEvaluator"/> port: the
/// convention only scans this assembly, so the evaluator gets its adapter here (no behaviour of its own).
/// </summary>
internal sealed class CatalogueFormulaEvaluator : FormulaEvaluator;
