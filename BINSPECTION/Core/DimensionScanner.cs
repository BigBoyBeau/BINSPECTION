using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;

namespace BINSPECTION.Core
{
    // A dimension found by GetAllDimensions, paired with the drawing View it
    // was found on and the sheet name DrawingSheetHelper.GetAllViewsBySheet
    // resolved for that View.
    public class DimensionHit
    {
        public DisplayDimension Dimension { get; set; }

        public View View { get; set; }

        public string SheetName { get; set; }
    }

    public class DimensionScanner
    {
        // Walks every view on every sheet via
        // DrawingSheetHelper.GetAllViewsBySheet - NOT
        // drawing.GetFirstView()/view.GetNextView(), which turned out to
        // only return views on whichever sheet is currently ACTIVE (see
        // GetAllViewsBySheet's remarks and
        // [[binspection_create_balloons_multi_sheet_silent_abort]] for the
        // live diagnostic data that caught this).
        // onEachDimension (optional) is invoked for EVERY dimension found,
        // before filtering, as (dim, wasIncluded) - TEMPORARY diagnostic
        // hook, see Core/ReferenceDimensionDiagnostics.cs. Remove this
        // parameter along with that file once the reference-dimension
        // filter below is confirmed correct on real drawings.
        // viewsBySheet (optional): pass in a list already computed by
        // DrawingSheetHelper.GetAllViewsBySheet so this scan doesn't
        // re-activate every sheet in the drawing all over again - each
        // activation is a real, visible SolidWorks sheet switch, so
        // OnCreateBalloons computes this ONCE and shares it across all four
        // of its scans (dimensions/GD&T/surface finishes/notes) instead of
        // each one paying that cost independently. A caller with no such
        // list handy (e.g. BalloonGridService, which only ever runs this
        // one scan) can leave it null and it's computed here instead.
        public List<DimensionHit> GetAllDimensions(
            DrawingDoc drawing,
            Action<DisplayDimension, bool> onEachDimension = null,
            List<DrawingSheetHelper.ViewOnSheet> viewsBySheet = null)
        {
            List<DimensionHit> hits =
                new List<DimensionHit>();

            foreach (DrawingSheetHelper.ViewOnSheet entry in viewsBySheet ?? DrawingSheetHelper.GetAllViewsBySheet(drawing))
            {
                View view = entry.View;

                DisplayDimension dim =
                    view.GetFirstDisplayDimension5();

                while (dim != null)
                {
                    // Reference dimensions (shown in parentheses in SW) are
                    // driven by other dimensions/geometry and aren't
                    // inspectable in their own right, so they're never
                    // eligible for a balloon - permanently, not a per-run
                    // option.
                    //
                    // NOTE: this is IDisplayDimension.ShowParenthesis (the
                    // literal "displays with parentheses" flag), not
                    // IDimension.IsReference() or IDisplayDimension.
                    // IsReferenceDim() - both of those turned out to read
                    // true for ordinary drawing dimensions (Basic-
                    // toleranced, multi-instance "2X ..." dimensions, etc.)
                    // and excluded far more than intended.
                    bool isReference =
                        dim.ShowParenthesis;

                    if (!isReference)
                    {
                        hits.Add(new DimensionHit
                        {
                            Dimension = dim,
                            View = view,
                            SheetName = entry.SheetName,
                        });
                    }

                    onEachDimension?.Invoke(dim, !isReference);

                    dim =
                        dim.GetNext5();
                }
            }

            return hits;
        }
    }
}
