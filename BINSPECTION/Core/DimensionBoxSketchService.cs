using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace BINSPECTION.Core
{
    // Draws/removes a thin rectangle sketch around a dimension's annotation
    // on the drawing sheet - used by BalloonGridService.SetBasic to visually
    // flag a dimension marked Basic. Ported from a VBA macro prototype that
    // tried to correct for a view-position offset plus a hand-tuned 1in
    // fudge; that turned out to be solving a problem that didn't exist here.
    // IAnnotation.GetPosition() already returns sheet-space coordinates for
    // a dimension's own annotation - confirmed both by how BalloonManager
    // positions new balloon notes directly off other annotations'
    // GetPosition() with no view-relative transform, and independently by
    // the SolidWorks API community (a dimension's GetPosition() anchor is
    // documented/confirmed as sheet coordinates, not view-local ones). So
    // this draws the box straight off that position with no correction,
    // in a sheet-level sketch (entered with nothing selected, so SolidWorks
    // doesn't scope it to whatever view happens to be active).
    //
    // There is no reliable SolidWorks API for a dimension text's exact
    // bounding box (a known gap - GetPosition returns only an anchor point,
    // and the width/height helpers on IAnnotation are unreliable across
    // dimension styles). Like the original macro, this estimates the box
    // from character count and font height with generous padding - close
    // enough to visibly wrap the text, not a precise typographic fit.
    public static class DimensionBoxSketchService
    {
        // SetLineWidthCustom takes an arbitrary width in meters rather than
        // one of the fixed swLineWeights_e steps, so the thinnest available
        // line is just the smallest width still worth rendering/printing.
        private const double ThinLineWidthMeters = 0.0001; // 0.1 mm

        private const double MarginXMeters = 0.003;
        private const double MarginYMeters = 0.0015;

        // Creates the box and returns the new sketch feature's SolidWorks-
        // assigned name (e.g. "Sketch12"), so RemoveBox can find it again
        // later - or null if the dimension's annotation/position couldn't
        // be read, in which case nothing is drawn.
        public static string AddBox(
            ModelDoc2 model,
            DrawingDoc drawing,
            IDisplayDimension displayDim,
            string sheetName)
        {
            DrawingSheetHelper.ActivateSheet(drawing, sheetName);

            Annotation ann = displayDim.GetAnnotation() as Annotation;

            if (ann == null)
                return null;

            double[] pos = ann.GetPosition() as double[];

            if (pos == null || pos.Length < 3)
                return null;

            double charHeight = AnnotationTextMetrics.GetCharHeight(ann);

            // GetDimension2(0).FullName is SolidWorks' internal dimension
            // name (e.g. "D5@Sketch3"), not what's printed on the drawing -
            // using it here previously sized the box off the wrong string
            // entirely. swDimensionTextAll is the assembled text actually
            // shown (value plus any prefix/suffix), and AnnotationScanner.
            // SplitLines already knows how to strip SolidWorks' "<TAG>"
            // markup out of that (same convention as note/balloon text), so
            // reuse it rather than duplicating the strip logic here.
            string dimText;

            try
            {
                dimText = displayDim.GetText((int)swDimensionTextParts_e.swDimensionTextAll);
            }
            catch
            {
                dimText = null;
            }

            List<string> lines = AnnotationScanner.SplitLines(dimText);

            int charCount = lines.Count > 0 ? lines.Max(l => l.Length) : 0;
            int lineCount = Math.Max(lines.Count, 1);

            if (charCount == 0)
                charCount = 6;

            double estWidth = (charCount * charHeight * AnnotationTextMetrics.AvgCharWidthFactor) + (2 * MarginXMeters);
            double estHeight = (lineCount * charHeight) + (2 * MarginYMeters);

            double x1 = pos[0] - (estWidth / 2);
            double y1 = pos[1] + (estHeight / 2);
            double x2 = pos[0] + (estWidth / 2);
            double y2 = pos[1] - (estHeight / 2);

            model.ClearSelection2(true);

            SketchManager sketchMgr = model.SketchManager;

            sketchMgr.InsertSketch(true);
            object rectLines = sketchMgr.CreateCornerRectangle(x1, y1, 0, x2, y2, 0);
            sketchMgr.InsertSketch(true);

            Feature boxFeature = model.FeatureByPositionReverse(0) as Feature;

            if (boxFeature == null)
                return null;

            SelectRectangleLines(rectLines);
            drawing.SetLineWidthCustom(ThinLineWidthMeters);
            model.ClearSelection2(true);

            model.GraphicsRedraw2();

            return boxFeature.Name;
        }

        // Deletes a previously-created box by the sketch feature name AddBox
        // returned. Best-effort: a missing/already-deleted feature (e.g. the
        // box was manually deleted, or the drawing was edited some other
        // way) is not treated as an error, same graceful-degradation
        // convention as DrawingSheetHelper.
        public static void RemoveBox(
            ModelDoc2 model,
            DrawingDoc drawing,
            string sketchFeatureName,
            string sheetName)
        {
            if (string.IsNullOrEmpty(sketchFeatureName))
                return;

            DrawingSheetHelper.ActivateSheet(drawing, sheetName);

            model.ClearSelection2(true);

            bool selected = model.Extension.SelectByID2(
                sketchFeatureName, "SKETCH", 0, 0, 0, false, 0, null, 0);

            if (selected)
                model.EditDelete();

            model.ClearSelection2(true);
            model.GraphicsRedraw2();
        }

        private static void SelectRectangleLines(object rectLines)
        {
            object[] lines = rectLines as object[];

            if (lines == null)
                return;

            bool append = false;

            foreach (object lineObj in lines)
            {
                SketchSegment segment = lineObj as SketchSegment;

                if (segment == null)
                    continue;

                segment.Select4(append, null);
                append = true;
            }
        }
    }
}
