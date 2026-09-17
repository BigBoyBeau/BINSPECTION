using System;
using System.Text;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace BINSPECTION.Core
{
    // TEMPORARY diagnostic, added 2026-09-15 to debug "the reference-
    // dimension filter is excluding regular/basic/multi-instance dimensions
    // and NOT excluding some ordinate reference dimensions". A first
    // attempt used IDimension.IsReference() (wrong - true for nearly every
    // drawing dimension); the current attempt uses
    // IDisplayDimension.ShowParenthesis, which is the literal "displays
    // with parentheses" flag. This dumps every candidate signal for every
    // dimension the scanner sees (both included and excluded) so a
    // still-wrong result can be diagnosed from real data instead of another
    // guess. Same disposable pattern as ChamferDiagnostics - remove this
    // file, its BINSPECTION.csproj <Compile Include> entry, and its
    // DimensionScanner call site once the real behavior is confirmed
    // correct.
    internal static class ReferenceDimensionDiagnostics
    {
        public static string Describe(DisplayDimension dim, bool wasIncluded)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("----------------------------------------");
            sb.AppendLine("Included in balloon candidates: " + wasIncluded);

            if (dim == null)
            {
                sb.AppendLine("dim is null");
                return sb.ToString();
            }

            sb.AppendLine(
                "GetText(swDimensionTextAll): " +
                Escape(SafeString(() => dim.GetText((int)swDimensionTextParts_e.swDimensionTextAll))));
            sb.AppendLine("ShowParenthesis: " + SafeString(() => dim.ShowParenthesis.ToString()));
            sb.AppendLine("ShowTolParenthesis: " + SafeString(() => dim.ShowTolParenthesis.ToString()));
            sb.AppendLine("ShowLowerParenthesis: " + SafeString(() => dim.ShowLowerParenthesis.ToString()));
            sb.AppendLine("IsReferenceDim(): " + SafeString(() => dim.IsReferenceDim().ToString()));
            sb.AppendLine("Type2 (raw): " + SafeString(() => dim.Type2.ToString()));
            sb.AppendLine("IsDimXpert(): " + SafeString(() => dim.IsDimXpert().ToString()));

            Dimension swDim = null;

            try
            {
                swDim = dim.GetDimension2(0);
            }
            catch (Exception ex)
            {
                sb.AppendLine("GetDimension2(0) threw: " + ex.Message);
            }

            if (swDim == null)
            {
                sb.AppendLine("GetDimension2(0): null");
            }
            else
            {
                sb.AppendLine("GetDimension2(0).IsReference(): " + SafeString(() => swDim.IsReference().ToString()));
                sb.AppendLine("GetDimension2(0).DrivenState: " + SafeString(() => swDim.DrivenState.ToString()));

                string tolType =
                    SafeString(() => ((swTolType_e)swDim.Tolerance.Type).ToString());

                sb.AppendLine("GetDimension2(0).Tolerance.Type: " + tolType);
            }

            return sb.ToString();
        }

        private static string SafeString(Func<string> read)
        {
            try
            {
                return read();
            }
            catch (Exception ex)
            {
                return "<threw " + ex.GetType().Name + ": " + ex.Message + ">";
            }
        }

        private static string Escape(string text)
        {
            if (text == null)
                return "<null>";

            StringBuilder sb = new StringBuilder();

            foreach (char c in text)
            {
                switch (c)
                {
                    case '\r':
                        sb.Append("[CR]");
                        break;
                    case '\n':
                        sb.Append("[LF]");
                        break;
                    case '\v':
                        sb.Append("[VT]");
                        break;
                    case '\t':
                        sb.Append("[TAB]");
                        break;
                    case (char)0x2028:
                        sb.Append("[LS]");
                        break;
                    case (char)0x2029:
                        sb.Append("[PS]");
                        break;
                    default:
                        if (c < 0x20 || c == 0x7F)
                            sb.Append("[U+").Append(((int)c).ToString("X4")).Append(']');
                        else
                            sb.Append(c);
                        break;
                }
            }

            return sb.ToString();
        }
    }
}
