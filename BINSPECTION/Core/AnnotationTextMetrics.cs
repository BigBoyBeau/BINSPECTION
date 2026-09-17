using SolidWorks.Interop.sldworks;

namespace BINSPECTION.Core
{
    // Shared char-height/width-estimate lookup for anything that sizes
    // geometry off an annotation's own text - originally inline in
    // DimensionBoxSketchService.AddBox, pulled out here so
    // BalloonPlacementService can use the same numbers. There is no
    // reliable SolidWorks API for an annotation's exact rendered text
    // bounding box, so GetCharHeight/AvgCharWidthFactor are a deliberate
    // estimate (character count x height x a fixed width factor), not a
    // precise typographic fit - same caveat as the box-sketch feature.
    public static class AnnotationTextMetrics
    {
        public const double FallbackCharHeightMeters = 0.0035;
        public const double AvgCharWidthFactor = 0.6;

        public static double GetCharHeight(Annotation annotation)
        {
            if (annotation != null)
            {
                TextFormat textFormat = annotation.GetTextFormat(0) as TextFormat;

                if (textFormat != null && textFormat.CharHeight > 0)
                    return textFormat.CharHeight;
            }

            return FallbackCharHeightMeters;
        }
    }
}
