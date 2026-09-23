using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swpublished;
using BINSPECTION.CommandManager;
using SolidWorks.Interop.swconst;

namespace BINSPECTION
{
    [Guid("77b7d52a-c6ed-4e9c-ae49-e221f4fcd993")]
    [ComVisible(true)]
    public class BInspectionAddin : ISwAddin
    {
        private ISldWorks _swApp;
        private int _addinID;
        private CommandManagerHandler _commandManager;
        private TaskpaneView _taskpaneView;
        private UI.TaskPane.TaskPaneHostControl _taskPaneHostControl;
        public void OnCreateBalloons()
        {
            _commandManager.OnCreateBalloons();
        }

        public void OnGenerateReport()
        {
            _commandManager.OnGenerateReport();
        }

        public void OnRestoreBalloons()
        {
            _commandManager.OnRestoreBalloons();
        }

        public void OnOpenBalloonManager()
        {
            _commandManager.OnOpenBalloonManager();
        }

        public void OnSheetToleranceSelection()
        {
            _commandManager.OnSheetToleranceSelection();
        }

        public void OnDeleteAllBalloons()
        {
            _commandManager.OnDeleteAllBalloons();
        }

        public void OnRemoveBalloons()
        {
            _commandManager.OnRemoveBalloons();
        }

        public void OnRefreshBalloons()
        {
            _commandManager.OnRefreshBalloons();
        }

        public void OnSavePosition()
        {
            _commandManager.OnSavePosition();
        }

        public void OnRestorePosition()
        {
            _commandManager.OnRestorePosition();
        }
        [ComRegisterFunction]
        public static void RegisterFunction(Type t)
        {
            try
            {
                string guid =
                    "{" + t.GUID.ToString().ToUpper() + "}";

                // Add-in registration
                RegistryKey addinKey =
                    Registry.LocalMachine.CreateSubKey(
                        @"Software\SolidWorks\AddIns\" + guid);

                addinKey.SetValue(null, 1);
                addinKey.SetValue("Title", "BINSPECTION");
                addinKey.SetValue(
                    "Description",
                    "Beau's Inspection Add-In");

                addinKey.Close();

                // Auto-load at startup
                RegistryKey startupKey =
                    Registry.CurrentUser.CreateSubKey(
                        @"Software\SolidWorks\AddInsStartup\" + guid);

                startupKey.SetValue("", 1);

                startupKey.Close();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    ex.Message);
            }
        }

        [ComUnregisterFunction]
        public static void UnregisterFunction(Type t)
        {
            try
            {
                string guid =
                    "{" + t.GUID.ToString().ToUpper() + "}";

                Registry.LocalMachine.DeleteSubKey(
                    @"Software\SolidWorks\AddIns\" + guid,
                    false);

                Registry.CurrentUser.DeleteSubKey(
                    @"Software\SolidWorks\AddInsStartup\" + guid,
                    false);
            }
            catch
            {
            }
        }

        public bool ConnectToSW(object ThisSW, int Cookie)
        {
            try
            {
                _swApp = (ISldWorks)ThisSW;
                _addinID = Cookie;


                // Required for command callbacks
                _swApp.SetAddinCallbackInfo2(
                    0,
                    this,
                    _addinID);

                // Create BINSPECTION command manager
                _commandManager =
                    new CommandManager.CommandManagerHandler(
                        _swApp,
                        _addinID);

                _commandManager.CreateCommandManager();

                // The docked Task Pane hosting Balloon Manager. Failure
                // here (e.g. the ActiveX control couldn't be COM-activated)
                // is caught on its own rather than let it fail the whole
                // add-in load - the ribbon/menu commands still work without
                // it, Balloon Manager just reports itself unavailable (see
                // CommandManagerHandler.OnOpenBalloonManager).
                try
                {
                    string[] taskPaneIconPaths =
                        Core.CommandIconGenerator.BuildMainIconList();

                    _taskpaneView =
                        _swApp.CreateTaskpaneView3(taskPaneIconPaths, "BINSPECTION");

                    if (_taskpaneView != null)
                    {
                        object control =
                            _taskpaneView.AddControl("BINSPECTION.TaskPaneHostControl", "");

                        _taskPaneHostControl = control as UI.TaskPane.TaskPaneHostControl;

                        _commandManager.SetTaskPane(_taskpaneView, _taskPaneHostControl);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"BInspectionAddin Task Pane Error: {ex}");
                }

                // Previously showed an OK popup ("BINSPECTION Loaded") every
                // time SolidWorks started the add-in. That's an interruption
                // for normal use now that the add-in is stable, so this is
                // logged to the debug output instead - still visible to a
                // developer with Visual Studio attached, but no longer an
                // extra click for every SolidWorks launch.
                System.Diagnostics.Debug.WriteLine("BINSPECTION Loaded");

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.ToString());

                return false;
            }
        }

        public bool DisconnectFromSW()
        {
            try
            {
                if (_commandManager != null)
                {
                    _commandManager.RemoveCommandManager();
                }

                try
                {
                    _taskpaneView?.DeleteView();
                }
                catch
                {
                }
                finally
                {
                    _taskpaneView = null;
                    _taskPaneHostControl = null;
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.ToString());

                return false;
            }
        }
    }
}