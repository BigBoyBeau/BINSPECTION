namespace BINSPECTION.Models
{
    // The inspection-record header fields the "5FM-R-08.5.1" report
    // template (see Core/ReportGenerator) has no SolidWorks-sourced value
    // for - either because BINSPECTION has no matching custom property
    // (Customer, Report Issued By) or because the template never bound
    // that field to anything at all (Lot Size, Sample Size Chart, Sample
    // Size, Sample Frequency, Operator/Inspector Emp #, Cmm Report ID #).
    // Edited once via the Sheet Tolerance Selection window (see
    // UI/SheetToleranceSelectionWindow) and remembered per-project on
    // ProjectData, same as ToleranceSets - see
    // Core/ReportGenerator.FillTemplateSheet for where these get written
    // into the report.
    //
    // "Inspection Date" is deliberately not included here - it's a fact
    // about whenever the physical inspection happens to run, not a
    // per-project constant, so a saved default would just go stale. The
    // template leaves that one for manual/pen fill-in, same as before.
    public class ReportHeaderSettings
    {
        public string Customer { get; set; }
        public string ReportIssuedBy { get; set; }
        public string LotSize { get; set; }
        public string SampleSizeChart { get; set; }
        public string SampleSize { get; set; }
        public string SampleFrequency { get; set; }
        public string OperatorEmployeeNumber { get; set; }
        public string InspectorEmployeeNumber { get; set; }
        public string CmmReportId { get; set; }
    }
}
