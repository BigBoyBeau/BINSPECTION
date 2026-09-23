using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

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
        // model (optional): needed only to read a dimension's displayed
        // value for the zero-value-ordinate exclusion below
        // (IDimension.GetUserValueIn requires it). A caller that leaves
        // this null just skips that one exclusion - every other filter
        // still applies.
        public List<DimensionHit> GetAllDimensions(
            DrawingDoc drawing,
            Action<DisplayDimension, bool> onEachDimension = null,
            List<DrawingSheetHelper.ViewOnSheet> viewsBySheet = null,
            ModelDoc2 model = null)
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

                    // An ordinate dimension reading exactly 0 is the
                    // datum/origin of its ordinate set (X0 or Y0), not a
                    // real inspectable measurement - every other value in
                    // that set is measured FROM it. Ballooning it produces
                    // a meaningless "0.000" characteristic with no
                    // tolerance that means anything. Non-ordinate
                    // dimensions that happen to read 0 (a position
                    // dimension coincident with its datum, etc.) are left
                    // alone - this exclusion only applies to the ordinate
                    // dimension types themselves.
                    bool isZeroOrdinate =
                        IsZeroValueOrdinate(dim, model);

                    bool included =
                        !isReference && !isZeroOrdinate;

                    if (included)
                    {
                        hits.Add(new DimensionHit
                        {
                            Dimension = dim,
                            View = view,
                            SheetName = entry.SheetName,
                        });
                    }

                    onEachDimension?.Invoke(dim, included);

                    dim =
                        dim.GetNext5();
                }
            }

            return hits;
        }

        // True if dim is an ordinate-type dimension (swOrdinateDimension
        // and its Hor/Vert/Angular variants - same set BalloonPlacementService
        // treats specially for placement) currently displaying 0 in the
        // drawing's own units. A small epsilon rather than an exact == 0
        // compare, since GetUserValueIn returns a rounded display-unit
        // double that can carry ordinary floating-point noise even for a
        // dimension that reads as a clean "0" on the sheet. A null model
        // (caller didn't have one to pass) or any failure reading the type/
        // value just means this exclusion doesn't apply - never treated as
        // reference-dimension-style exclusion.
        private static bool IsZeroValueOrdinate(DisplayDimension dim, ModelDoc2 model)
        {
            if (model == null)
                return false;

            try
            {
                swDimensionType_e type =
                    (swDimensionType_e)dim.Type2;

                bool isOrdinate =
                    type == swDimensionType_e.swOrdinateDimension ||
                    type == swDimensionType_e.swHorOrdinateDimension ||
                    type == swDimensionType_e.swVertOrdinateDimension ||
                    type == swDimensionType_e.swAngularOrdinateDimension;

                if (!isOrdinate)
                    return false;

                Dimension swDim =
                    dim.GetDimension2(0);

                if (swDim == null)
                    return false;

                double value =
                    swDim.GetUserValueIn(model);

                return Math.Abs(value) < 1e-6;
            }
            catch
            {
                return false;
            }
        }
    }
}
