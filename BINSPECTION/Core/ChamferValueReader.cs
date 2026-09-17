using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace BINSPECTION.Core
{
    // Reads a genuine two-value chamfer dimension's real distance and
    // angle, for grouping it into a "12.7" + "45°" pair of characteristics
    // the same way HoleCalloutExtractor already does for a hole callout's
    // combined line.
    //
    // Confirmed against a real drawing (2026-09-15, via
    // Core/ChamferDiagnostics.cs) that GetDimension2(0)/GetDimension2(1)
    // have NO reliable index-to-value mapping - for the test chamfer,
    // GetDimension2(0).GetUserValueIn returned 135 (the angle, and the
    // WRONG one of the two supplementary readings a two-line vertex
    // admits) while GetDimension2(1) held a nameless placeholder
    // Dimension. ChamferTextStyle ("DistAng") didn't resolve this either -
    // it describes the chamfer's chosen DISPLAY order, not which raw
    // index holds which value.
    //
    // IDimension.GetSystemChamferValues(ref Length, ref Angle) sidesteps
    // all of that: called on EITHER GetDimension2(0) or GetDimension2(1),
    // it returned the SAME correct pair (Length=0.0127 m, Angle=0.7854
    // rad - i.e. 12.7 mm / 45°) for the same test chamfer. It hands back
    // the real values unambiguously by name, with no index or
    // display-style guessing needed.
    public static class ChamferValueReader
    {
        // Returns ["12.7", "45°"] (distance, then angle - both formatted
        // with this app's standard "0.####" convention) for a genuine
        // two-value chamfer dimension. Returns null for a single-value
        // "C" equal-leg chamfer (GetSystemChamferValues fails for that
        // style), or anything that isn't a chamfer dimension at all.
        public static List<string> GetSegments(ModelDoc2 model, DisplayDimension dim)
        {
            try
            {
                if (dim == null || model == null)
                    return null;

                if (dim.Type2 != (int)swDimensionType_e.swChamferDimension)
                    return null;

                Dimension subDim = dim.GetDimension2(0);

                if (subDim == null)
                    return null;

                double lengthMeters = 0;
                double angleRadians = 0;

                bool ok = subDim.GetSystemChamferValues(ref lengthMeters, ref angleRadians);

                if (!ok)
                    return null;

                double lengthDisplay = ConvertMetersToDisplayLength(model, lengthMeters);
                double angleDegrees = angleRadians * 180.0 / Math.PI;

                return new List<string>
                {
                    lengthDisplay.ToString("0.####"),
                    angleDegrees.ToString("0.####") + "°",
                };
            }
            catch
            {
                return null;
            }
        }

        // Live re-display text for BalloonGridService's grid refresh - same
        // convention as HoleCalloutExtractor.GetCombinedDisplayText.
        public static string GetCombinedDisplayText(ModelDoc2 model, DisplayDimension dim)
        {
            List<string> segments = GetSegments(model, dim);

            return segments != null ? string.Join(" X ", segments) : null;
        }

        // GetSystemChamferValues always returns Length in meters
        // (SolidWorks system units) regardless of the document's own
        // display unit setting - converts to whatever the document is
        // actually set to show (mm, inches, etc.) via the document's own
        // swUnitsLinear preference, same swLengthUnit_e values SolidWorks
        // itself uses. Falls back to millimeters (this add-in's normal
        // case) for a unit this doesn't explicitly handle (feet-inches
        // dual notation, mils, micro-inches) or if the preference can't
        // be read at all.
        private static double ConvertMetersToDisplayLength(ModelDoc2 model, double meters)
        {
            try
            {
                int lengthUnit = model.Extension.GetUserPreferenceInteger(
                    (int)swUserPreferenceIntegerValue_e.swUnitsLinear,
                    (int)swUserPreferenceOption_e.swDetailingNoOptionSpecified);

                switch ((swLengthUnit_e)lengthUnit)
                {
                    case swLengthUnit_e.swCM:
                        return meters * 100.0;
                    case swLengthUnit_e.swMETER:
                        return meters;
                    case swLengthUnit_e.swINCHES:
                        return meters / 0.0254;
                    case swLengthUnit_e.swFEET:
                        return meters / 0.3048;
                    case swLengthUnit_e.swMICRON:
                        return meters * 1000000.0;
                    case swLengthUnit_e.swNANOMETER:
                        return meters * 1000000000.0;
                    case swLengthUnit_e.swANGSTROM:
                        return meters * 10000000000.0;
                    case swLengthUnit_e.swMM:
                    default:
                        return meters * 1000.0;
                }
            }
            catch
            {
                return meters * 1000.0;
            }
        }
    }
}
