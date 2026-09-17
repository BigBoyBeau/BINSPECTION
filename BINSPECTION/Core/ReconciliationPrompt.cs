using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;
using BINSPECTION.Models;

namespace BINSPECTION.Core
{
    // Shows the user what doesn't match between the saved balloon data and
    // the live drawing BEFORE anything gets changed. Per how this project
    // decided to handle reconciliation: always confirm, never silently
    // auto-repair.
    //
    // This is a plain MessageBox for now, which is enough to satisfy
    // "confirm before changing anything" without building a full custom
    // dialog. It's kept in its own class specifically so a nicer dialog
    // (a real WinForm, or a PropertyManagerPage to match SolidWorks' own
    // UI) can replace just this one method later without touching the
    // reconciliation logic in ReconciliationService.
    public static class ReconciliationPrompt
    {
        // Returns:
        //   Yes    - recreate the missing balloons now, using their saved numbers
        //   No     - leave missing balloons alone, continue the "Create Balloons" run
        //   Cancel - stop the "Create Balloons" run entirely, change nothing
        public static DialogResult Show(ReconciliationResult result)
        {
            StringBuilder message = new StringBuilder();

            message.AppendLine(
                "BINSPECTION found differences between the saved balloon data and this drawing:");

            message.AppendLine();

            if (result.MissingBalloons.Count > 0)
            {
                message.AppendLine(
                    result.MissingBalloons.Count +
                    " characteristic(s) are recorded but their balloon is missing from the sheet:");

                foreach (MissingBalloonEntry entry in result.MissingBalloons)
                {
                    message.AppendLine(
                        "   (" + entry.Characteristic.Number + ")  " +
                        entry.Characteristic.DimensionName);
                }

                message.AppendLine();
            }

            if (result.Unresolvable.Count > 0)
            {
                message.AppendLine(
                    result.Unresolvable.Count +
                    " characteristic(s) could not be matched back to a dimension:");

                foreach (UnresolvableEntry entry in result.Unresolvable)
                {
                    message.AppendLine(
                        "   (" + entry.Characteristic.Number + ")  " +
                        entry.Characteristic.DimensionName);

                    message.AppendLine(
                        "        reason: " + entry.Reason);
                }

                message.AppendLine();
            }

            if (result.OrphanedBalloonNumbers.Count > 0)
            {
                // Worded generically since this method is shared by both
                // Create Balloons (which always leaves these alone) and
                // Restore Balloons (which asks separately, afterward,
                // whether to delete them - see ShowDeleteOrphanedPrompt).
                message.AppendLine(
                    result.OrphanedBalloonNumbers.Count +
                    " balloon(s) on the sheet don't match anything in the saved data file:");

                foreach (string number in result.OrphanedBalloonNumbers)
                {
                    message.AppendLine("   (" + number + ")");
                }

                message.AppendLine();
            }

            if (result.MissingBalloons.Count == 0)
            {
                // Nothing to recreate - this is an informational-only
                // notice (unresolvable/orphaned entries still get
                // reported above, but there's no action to confirm).
                message.AppendLine("Nothing needs to be recreated. Click OK to continue.");

                MessageBox.Show(
                    message.ToString(),
                    "BINSPECTION - Balloon Data Check",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return DialogResult.No;
            }

            message.AppendLine("Recreate the missing balloon(s) now using their saved numbers?");
            message.AppendLine("Yes = recreate now   No = skip for now   Cancel = stop");

            return MessageBox.Show(
                message.ToString(),
                "BINSPECTION - Balloon Data Check",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Warning);
        }

        // A second, separate confirmation specifically for DELETING
        // balloons that don't match the saved data file - kept apart from
        // Show() above because deleting something from the drawing
        // deserves its own explicit yes/no, not to be bundled into the
        // "recreate missing balloons?" answer. Only Restore Balloons calls
        // this today; Create Balloons never deletes anything.
        //
        // Returns Yes to delete them, anything else to leave them alone.
        public static DialogResult ShowDeleteOrphanedPrompt(List<string> orphanedNumbers)
        {
            StringBuilder message = new StringBuilder();

            message.AppendLine(
                orphanedNumbers.Count +
                " balloon(s) on the sheet don't match anything in the saved data file:");

            message.AppendLine();

            foreach (string number in orphanedNumbers)
            {
                message.AppendLine("   (" + number + ")");
            }

            message.AppendLine();
            message.AppendLine("Delete these balloon(s) now? This cannot be undone by this add-in.");

            return MessageBox.Show(
                message.ToString(),
                "BINSPECTION - Delete Orphaned Balloons",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
        }
    }
}
