using System;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace BINSPECTION.Core
{
    // Wraps SolidWorks' "persistent reference" API so the rest of the
    // add-in doesn't have to deal with the raw byte[]/Variant marshaling.
    //
    // A persistent reference is SolidWorks' own mechanism for pointing at
    // a specific object (here, a dimension's annotation) in a way that
    // survives saving/closing/reopening the document, and most renames or
    // drawing edits. It's the piece that lets the external data file say
    // "this balloon belongs to THIS exact annotation" reliably - a
    // dimension's *name* is not stable enough to use as the only key,
    // since names can be changed or can collide across sheets/views.
    //
    // Method signatures below were confirmed directly against
    // SolidWorks.Interop.sldworks.dll (IModelDocExtension.GetPersistReference3 /
    // GetObjectByPersistReference3) rather than assumed, since a wrong
    // signature here would silently corrupt or lose balloon data.
    public static class PersistentReferenceHelper
    {
        // Returns a base64-encoded persistent reference ID for the given
        // SolidWorks object (pass the dimension's Annotation, e.g. from
        // DisplayDimension.GetAnnotation()), or null if one could not be
        // generated.
        public static string GetPersistentId(ModelDoc2 model, object swObject)
        {
            if (model == null || swObject == null)
                return null;

            try
            {
                object result =
                    model.Extension.GetPersistReference3(swObject);

                byte[] bytes = result as byte[];

                if (bytes == null || bytes.Length == 0)
                    return null;

                return Convert.ToBase64String(bytes);
            }
            catch (Exception ex)
            {
                BinspectionLog.Error("PersistentReferenceHelper.GetPersistentId", ex);

                return null;
            }
        }

        // Resolves a previously-saved persistent reference ID back to a
        // live SolidWorks object in the currently open document.
        //
        // Returns null if the reference no longer resolves cleanly (for
        // example, the annotation was deleted). "state" reports why, so
        // callers can tell "cleanly gone" apart from "something is wrong"
        // instead of treating every failure the same way.
        public static object ResolvePersistentId(
            ModelDoc2 model,
            string base64Id,
            out swPersistReferencedObjectStates_e state)
        {
            state = swPersistReferencedObjectStates_e.swPersistReferencedObject_Invalid;

            if (model == null || string.IsNullOrEmpty(base64Id))
                return null;

            try
            {
                byte[] bytes = Convert.FromBase64String(base64Id);

                int errorCode = 0;

                object resolved =
                    model.Extension.GetObjectByPersistReference3(
                        bytes,
                        out errorCode);

                state = (swPersistReferencedObjectStates_e)errorCode;

                if (state != swPersistReferencedObjectStates_e.swPersistReferencedObject_Ok)
                    return null;

                return resolved;
            }
            catch (Exception ex)
            {
                BinspectionLog.Error("PersistentReferenceHelper.ResolvePersistentId", ex);

                return null;
            }
        }

        // Convenience wrapper around ResolvePersistentId specifically for
        // dimensions - handles the same "resolved object might BE a
        // dimension, or might be an IAnnotation wrapping one" fallback used
        // in ReconciliationService.Reconcile, for callers (like
        // ReportGenerator) that just want the dimension and don't need the
        // detailed diagnostic reasoning ReconciliationService reports on
        // unresolvable entries.
        public static IDisplayDimension ResolveDimension(
            ModelDoc2 model,
            string base64Id)
        {
            if (string.IsNullOrEmpty(base64Id))
                return null;

            swPersistReferencedObjectStates_e state;

            object resolved =
                ResolvePersistentId(model, base64Id, out state);

            if (resolved == null)
                return null;

            IDisplayDimension dimension = resolved as IDisplayDimension;

            if (dimension != null)
                return dimension;

            IAnnotation annotation = resolved as IAnnotation;

            if (annotation != null)
                return annotation.GetSpecificAnnotation() as IDisplayDimension;

            return null;
        }
    }
}
