using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;

namespace BINSPECTION.Core
{
    // Sheet-name helpers shared by CommandManagerHandler (Sheet Tolerance
    // Selection, Create Balloons) and BalloonGridService (Balloon Manager) -
    // pulled out of CommandManagerHandler so both can call them without one
    // depending on the other.
    public static class DrawingSheetHelper
    {
        public static List<string> GetSheetNames(DrawingDoc drawing)
        {
            object rawNames =
                drawing.GetSheetNames();

            string[] names =
                rawNames as string[];

            if (names == null)
                return new List<string>();

            return names.ToList();
        }

        // One View from GetAllViewsBySheet, paired with the sheet name it
        // was found under.
        public struct ViewOnSheet
        {
            public View View;
            public string SheetName;
        }

        // Every View in the drawing, across every sheet, each one paired
        // with the sheet name it actually belongs to.
        //
        // DimensionScanner/AnnotationScanner used to walk
        // drawing.GetFirstView()/view.GetNextView() ONCE, on the assumption
        // that walk already spans every sheet - confirmed via live
        // diagnostic data (2026-09-15, see
        // [[binspection_create_balloons_multi_sheet_silent_abort]]) that
        // it doesn't: that walk only ever returns views on whichever sheet
        // is CURRENTLY ACTIVE.
        //
        // A later attempt (2026-09-16) switched to IDrawingDoc::GetViews(),
        // documented to return every sheet's views in one call as an array
        // of arrays, resolving each sheet's name from its own array's first
        // element (the sheet's own view) via View.Sheet.GetName(). That
        // turned out to fail LIVE for essentially every dimension - not
        // just placed views, the sheet's own view too - reproducing the
        // exact "sheet name could not be determined, 0 balloons created"
        // symptom this whole rewrite was meant to fix, just moved from
        // "sometimes" to "always". View.Sheet is evidently not a reliable
        // per-view property in this SolidWorks version/install at all.
        //
        // Current approach: reuse the one View-enumeration technique
        // that's already been confirmed live to work correctly - GetFirstView/
        // GetNextView really is scoped to whichever sheet is active (that's
        // exactly the bug documented above) - so activate each sheet in
        // turn ourselves and walk it while it's active, attributing every
        // view returned to the sheet we just activated. No per-view
        // property lookup needed at all; the sheet name comes from which
        // iteration of this loop found the view, not from asking the view
        // itself. Same activate-then-walk pattern already proven for
        // annotations in [[binspection_refresh_balloons_zero_changed_bug]].
        // Restores whichever sheet was active before this call once done,
        // since this is a read-only scan, not a sheet-switching operation.
        //
        // sheetNames (optional): restrict the walk to just these sheets
        // instead of every sheet in the drawing. Each sheet costs one real
        // SolidWorks sheet activation (visibly redraws the graphics area),
        // so a caller that's about to discard everything outside a known
        // subset of sheets anyway (Create Balloons already knows which
        // sheets the user checked before it ever scans; Balloon Manager's
        // grid is always scoped to one sheet) should pass that subset here
        // rather than paying to activate - and immediately throw away the
        // results for - every sheet the user didn't ask about.
        public static List<ViewOnSheet> GetAllViewsBySheet(DrawingDoc drawing, IEnumerable<string> sheetNames = null)
        {
            List<ViewOnSheet> views = new List<ViewOnSheet>();

            if (drawing == null)
                return views;

            string originalSheetName = null;

            try
            {
                originalSheetName = (drawing.GetCurrentSheet() as Sheet)?.GetName();
            }
            catch
            {
            }

            foreach (string sheetName in sheetNames ?? GetSheetNames(drawing))
            {
                if (!ActivateSheet(drawing, sheetName))
                    continue;

                View view = drawing.GetFirstView() as View;

                while (view != null)
                {
                    views.Add(new ViewOnSheet { View = view, SheetName = sheetName });

                    view = view.GetNextView() as View;
                }
            }

            if (!string.IsNullOrEmpty(originalSheetName))
                ActivateSheet(drawing, originalSheetName);

            return views;
        }

        // Best-effort: switches SolidWorks' own active sheet to the given
        // sheet name. IModelDoc2.InsertNote (used by BalloonManager.
        // CreateBalloon) always inserts into whichever sheet SolidWorks
        // itself currently has active - it has no "which sheet" parameter
        // of its own - so every balloon-creating call site must activate
        // the correct sheet first or the new note silently lands on
        // whatever sheet the user happened to have open, not the sheet its
        // source dimension/annotation actually lives on. A null/empty name
        // (sheet couldn't be determined) or a failed activation is not
        // treated as fatal - the balloon still gets created, just possibly
        // on the wrong sheet.
        public static bool ActivateSheet(DrawingDoc drawing, string sheetName)
        {
            if (drawing == null || string.IsNullOrEmpty(sheetName))
                return false;

            try
            {
                return drawing.ActivateSheet(sheetName);
            }
            catch
            {
                return false;
            }
        }
    }
}
