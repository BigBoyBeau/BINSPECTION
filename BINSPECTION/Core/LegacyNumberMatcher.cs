using System;
using System.Collections.Generic;
using System.Linq;
using BINSPECTION.Models;
using SolidWorks.Interop.sldworks;

namespace BINSPECTION.Core
{
    // Matches each row of the uploaded legacy-system reference spreadsheet
    // (see LegacyNumberImporter) to this drawing's live characteristics by
    // nominal dimension value - the basis the user asked for, since legacy
    // balloon numbers themselves have no relationship to Binspection's own
    // numbering.
    //
    // Mirrors ReconciliationService's shape: bucket everything into
    // "matched automatically" vs. the exceptions a human has to resolve,
    // and never write anything back until the caller has the user's
    // confirmation for those exceptions.
    public static class LegacyNumberMatcher
    {
        // Two nominal values within this far apart are considered the same
        // dimension. Loose enough to absorb both a legacy sheet rounding to
        // fewer decimal places than SolidWorks reports AND
        // LegacyNumberImporter deriving a nominal as the midpoint of
        // Upper/Lower Limit columns rounded to hundredths (each limit can
        // be off by up to 0.005 from the true value, so their average can
        // be off by nearly as much) - tight enough that genuinely different
        // dimensions on the same part practically never collide.
        public const double NominalTolerance = 0.01;

        public static LegacyMatchResult Match(
            ModelDoc2 model,
            List<Characteristic> characteristics,
            List<LegacyBalloonRow> legacyRows)
        {
            LegacyMatchResult result = new LegacyMatchResult();

            if (model == null || characteristics == null || legacyRows == null)
                return result;

            List<Characteristic> candidates = characteristics
                .Where(c => !c.IsUnnumbered)
                .ToList();

            // Resolved once up front - reading each dimension's live value
            // is a COM round-trip, and every legacy row potentially
            // compares against every candidate.
            Dictionary<Characteristic, double> nominalByCharacteristic =
                new Dictionary<Characteristic, double>();

            foreach (Characteristic characteristic in candidates)
            {
                double nominal, plusTol, minusTol;
                bool resolved;

                ReportGenerator.ReadDimensionValues(
                    model, characteristic, out nominal, out plusTol, out minusTol, out resolved);

                if (resolved)
                    nominalByCharacteristic[characteristic] = nominal;
            }

            foreach (LegacyBalloonRow legacyRow in legacyRows)
            {
                List<Characteristic> matches = nominalByCharacteristic
                    .Where(kvp => Math.Abs(kvp.Value - legacyRow.Nominal) <= NominalTolerance)
                    .Select(kvp => kvp.Key)
                    .ToList();

                if (matches.Count == 1)
                {
                    result.Matched.Add(new LegacyMatchEntry
                    {
                        LegacyRow = legacyRow,
                        Characteristic = matches[0],
                    });
                }
                else if (matches.Count > 1)
                {
                    result.Ambiguous.Add(new LegacyAmbiguousEntry
                    {
                        LegacyRow = legacyRow,
                        Candidates = matches,
                    });
                }
                else
                {
                    result.Unmatched.Add(legacyRow);
                }
            }

            return result;
        }
    }
}
