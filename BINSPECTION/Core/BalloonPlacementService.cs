using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace BINSPECTION.Core
{
    // Deterministic balloon placement - replaces the old collision-search
    // BalloonLayoutOptimizer ("Optimize Balloons"), which nudged already-
    // placed balloons apart on a separate, manual pass with its own
    // per-project settings. This runs once, at creation, with no settings
    // and no separate command: BalloonManager.CreateBalloon calls it for
    // every annotation type (dimension, GD&T frame, note, surface finish),
    // so both Create Balloons and Refresh Balloons place every balloon
    // through here automatically.
    //
    // Only an IDisplayDimension source gets the ordinate/hor-linear
    // special case below - "ordinate" and dimension-line orientation only
    // mean anything for a dimension, not a GD&T frame/note/surface finish
    // symbol.
    //
    // Baseline: start at the source annotation's own position (its text
    // center - "the middle of the dimension") and shift straight up by
    // 2.25x the source's character height. That clears the source text
    // without needing a collision search.
    //
    // Ordinate dimensions (swOrdinateDimension and its Hor/Vert/Angular
    // variants) place their value directly off a jogged leader, with
    // other ordinate values stacked above/below along the same baseline -
    // shifting a balloon up there lands it on the next value, not clear
    // space. Shifting left by the balloon's own text width instead moves
    // it off the leader without walking into the stack.
    //
    // swHorLinearDimension means SolidWorks has classified this as a
    // horizontal-distance dimension: its dimension line runs horizontally,
    // so its witness/extension lines run vertically. A vertical shift
    // there would run the balloon straight up a witness line instead of
    // clearing it, so it gets the same left-shift treatment as ordinate.
    // swVertLinearDimension (vertical dimension line, horizontal witness
    // lines) and the generic/unclassified swLinearDimension keep the
    // baseline vertical shift.
    public static class BalloonPlacementService
    {
        private const double VerticalShiftMultiplier = 2.25;
        private const double HorizontalShiftMultiplier = 2.0;

        public static double[] ComputePosition(
            object annotationSource,
            Annotation sourceAnnotation,
            double[] centerPos,
            string displayNumber)
        {
            double x = centerPos[0];
            double y = centerPos[1];
            double z = centerPos[2];

            double charHeight = AnnotationTextMetrics.GetCharHeight(sourceAnnotation);

            if (ShouldShiftLeft(annotationSource))
            {
                int charCount = string.IsNullOrEmpty(displayNumber) ? 1 : displayNumber.Length;

                x -= charCount * charHeight * AnnotationTextMetrics.AvgCharWidthFactor * HorizontalShiftMultiplier;
            }
            else
            {
                y += charHeight * VerticalShiftMultiplier;
            }

            return new double[] { x, y, z };
        }

        private static bool ShouldShiftLeft(object annotationSource)
        {
            IDisplayDimension displayDim = annotationSource as IDisplayDimension;

            if (displayDim == null)
                return false;

            swDimensionType_e type;

            try
            {
                type = (swDimensionType_e)displayDim.Type2;
            }
            catch
            {
                return false;
            }

            switch (type)
            {
                case swDimensionType_e.swOrdinateDimension:
                case swDimensionType_e.swHorOrdinateDimension:
                case swDimensionType_e.swVertOrdinateDimension:
                case swDimensionType_e.swAngularOrdinateDimension:
                case swDimensionType_e.swHorLinearDimension:
                    return true;

                default:
                    return false;
            }
        }
    }
}
