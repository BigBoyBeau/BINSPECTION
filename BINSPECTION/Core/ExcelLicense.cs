using OfficeOpenXml;

namespace BINSPECTION.Core
{
    // EPPlus 8 requires a license context to be set once before any
    // ExcelPackage is created anywhere in the process, or it throws.
    // Centralized here (rather than duplicated at each call site, which is
    // exactly how the Match Legacy Numbers import broke) so every place
    // that touches EPPlus goes through the same call.
    //
    // Currently set to the noncommercial-organization form as a
    // placeholder - confirm with whoever handles software licensing at
    // Precision Mfg whether that actually applies to this add-in's use, or
    // whether a purchased commercial key should be used instead via
    // ExcelPackage.License.SetCommercial().
    public static class ExcelLicense
    {
        private static bool _set;

        public static void EnsureSet()
        {
            if (_set)
                return;

            ExcelPackage.License.SetNonCommercialOrganization("Precision Mfg");

            _set = true;
        }
    }
}
