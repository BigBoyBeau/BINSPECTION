using System;
using System.Collections.Generic;
using System.Text;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace BINSPECTION.Core
{
    // TEMPORARY diagnostic, added 2026-09-15 to debug "a chamfer's distance
    // and angle aren't being split into a grouped balloon" - a prior fix
    // guessed that a chamfer's combined text lives in the same
    // swDimensionTextCalloutAbove/CalloutBelow slots a hole callout uses
    // (see HoleCalloutExtractor.GetSegments), gated on
    // Type2 == swChamferDimension. That guess didn't work. Rather than
    // guess again, this dumps everything BINSPECTION currently reads off a
    // dimension - real values, from a real drawing - so the actual fix can
    // be precise. Same disposable pattern as the earlier
    // HoleCalloutDiagnostics/NoteEligibilityDiagnostics investigations:
    // remove this file, its BINSPECTION.csproj <Compile Include> entry, and
    // its OnCreateBalloons call site once the real bug is confirmed fixed.
    internal static class ChamferDiagnostics
    {
        public static string Describe(ModelDoc2 model, DisplayDimension dim)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("----------------------------------------");

            if (dim == null)
            {
                sb.AppendLine("dim is null");
                return sb.ToString();
            }

            // DisplayDimension itself has no FullName - GetDimension2(0)'s
            // FullName (reported below) is the closest thing to "this
            // dimension's name".
            sb.AppendLine("Type2 (raw): " + SafeString(() => dim.Type2.ToString()));
            sb.AppendLine(
                "Type2 == swChamferDimension: " +
                SafeString(() => (dim.Type2 == (int)swDimensionType_e.swChamferDimension).ToString()));
            sb.AppendLine("IsHoleCallout(): " + SafeString(() => dim.IsHoleCallout().ToString()));
            sb.AppendLine("IsDimXpert(): " + SafeString(() => dim.IsDimXpert().ToString()));

            // ChamferTextStyle (only meaningful when Type2 is actually
            // swChamferDimension) tells us, directly from SolidWorks,
            // which of GetDimension2(0)/(1) is the distance and which is
            // the angle - no need to guess at index order:
            // swDetailChamferDimDistDist=1 (dist X dist),
            // swDetailChamferDimDistAng=2 (dist X angle - index 0 =
            // distance, index 1 = angle), swDetailChamferDimAngDist=3
            // (angle X dist - index 0 = angle, index 1 = distance),
            // swDetailChamferDimCDist=4 ("C" equal-distance style).
            sb.AppendLine("ChamferTextStyle (raw): " + SafeString(() => dim.ChamferTextStyle.ToString()));
            sb.AppendLine("ChamferPrecision(0): " + SafeString(() => dim.ChamferPrecision[0].ToString()));
            sb.AppendLine("ChamferPrecision(1): " + SafeString(() => dim.ChamferPrecision[1].ToString()));
            sb.AppendLine("GetChamferUnits: " + SafeGetChamferUnits(dim));

            // The real swDimensionTextParts_e members (confirmed against
            // the installed SolidWorks.Interop.swconst.dll - there is no
            // plain "Above"/"Below"/"Callout" member, only these five):
            // swDimensionTextAll, swDimensionTextPrefix,
            // swDimensionTextSuffix, swDimensionTextCalloutAbove,
            // swDimensionTextCalloutBelow (plus "*Definition" variants of
            // the last four).
            sb.AppendLine(
                "GetText(swDimensionTextPrefix): " +
                Escape(SafeString(() => dim.GetText((int)swDimensionTextParts_e.swDimensionTextPrefix))));
            sb.AppendLine(
                "GetText(swDimensionTextSuffix): " +
                Escape(SafeString(() => dim.GetText((int)swDimensionTextParts_e.swDimensionTextSuffix))));
            sb.AppendLine(
                "GetText(swDimensionTextCalloutAbove): " +
                Escape(SafeString(() => dim.GetText((int)swDimensionTextParts_e.swDimensionTextCalloutAbove))));
            sb.AppendLine(
                "GetText(swDimensionTextCalloutBelow): " +
                Escape(SafeString(() => dim.GetText((int)swDimensionTextParts_e.swDimensionTextCalloutBelow))));
            sb.AppendLine(
                "GetText(swDimensionTextAll): " +
                Escape(SafeString(() => dim.GetText((int)swDimensionTextParts_e.swDimensionTextAll))));

            sb.AppendLine("GetDimension2(0): " + DescribeSubDimension(dim, 0, model));
            sb.AppendLine("GetDimension2(1): " + DescribeSubDimension(dim, 1, model));

            List<string> segments = null;

            try
            {
                segments = HoleCalloutExtractor.GetSegments(model, dim);
            }
            catch (Exception ex)
            {
                sb.AppendLine("HoleCalloutExtractor.GetSegments threw: " + ex);
            }

            sb.AppendLine(
                "HoleCalloutExtractor.GetSegments: " +
                (segments == null ? "null" : "[" + string.Join(" | ", segments) + "]"));

            sb.AppendLine(
                "HoleCalloutExtractor.GetCombinedDisplayText: " +
                (SafeString(() => HoleCalloutExtractor.GetCombinedDisplayText(model, dim)) ?? "null"));

            return sb.ToString();
        }

        private static string SafeGetChamferUnits(DisplayDimension dim)
        {
            try
            {
                int lengthUnit, angularUnit;

                bool ok = dim.GetChamferUnits(out lengthUnit, out angularUnit);

                return "ok=" + ok + ", LengthUnit=" + lengthUnit + ", AngularUnit=" + angularUnit;
            }
            catch (Exception ex)
            {
                return "threw: " + ex.Message;
            }
        }

        // IDimension.GetSystemChamferValues(out Length, out Angle) - found
        // via reflection on the real interop DLL - hands back the
        // chamfer's distance and angle directly and unambiguously,
        // sidestepping GetDimension2(0)/(1) index-order guessing and
        // ChamferTextStyle entirely. Length/Angle come back in SYSTEM
        // units (meters / radians per standard SolidWorks convention, NOT
        // the document's display units) - converted here to mm/degrees
        // purely for this dump to be human-readable; not yet confirmed
        // against real data, hence probing both sub-dimensions rather
        // than assuming which (if either) it's valid on.
        private static string SafeGetSystemChamferValues(Dimension subDim)
        {
            try
            {
                double length = 0, angle = 0;

                bool ok = subDim.GetSystemChamferValues(ref length, ref angle);

                double lengthMm = length * 1000.0;
                double angleDeg = angle * 180.0 / Math.PI;

                return "ok=" + ok +
                    ", Length(raw)=" + length + " (=" + lengthMm + " mm)" +
                    ", Angle(raw)=" + angle + " (=" + angleDeg + " deg)";
            }
            catch (Exception ex)
            {
                return "threw: " + ex.Message;
            }
        }

        private static string DescribeSubDimension(DisplayDimension dim, int index, ModelDoc2 model)
        {
            try
            {
                Dimension subDim = dim.GetDimension2(index);

                if (subDim == null)
                    return "null";

                string fullName = SafeString(() => subDim.FullName);

                string value = SafeString(() => subDim.GetUserValueIn(model).ToString());

                string chamferValues = SafeGetSystemChamferValues(subDim);

                return "FullName=" + fullName + ", GetUserValueIn=" + value +
                    ", GetSystemChamferValues=" + chamferValues;
            }
            catch (Exception ex)
            {
                return "threw: " + ex.Message;
            }
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

        // Makes hidden separator/control characters visible in a text dump
        // - same escaping this codebase's earlier HoleCalloutDiagnostics
        // used, so a combined value's real line/paragraph-separator
        // characters (or a literal embedded CR/LF/tab) show up instead of
        // silently vanishing or looking like a plain space.
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
