# SOLIDWORKS API Documentation — Index

Generated from the official SOLIDWORKS API Help .chm files (CHM -> HTML -> PDF). Content is split into many small PDFs so each one stays a manageable size to open and read. Every interface/enum/coclass keeps all its members together in one file (or, for a handful of very large interfaces, split into sequential parts).

Two of these modules — **swinspectionapi** and **swinspectionapivb6** — are the SOLIDWORKS Inspection Add-in API itself, i.e. the API this project (Binspection) is re-implementing. Start there for anything balloon/characteristic/inspection-project related; use sldworksapi/sldworksapivb6 for general document, feature, and selection APIs, and swconst for enum values referenced anywhere.

Folder layout: `<module>/Reference/` (interfaces, enums, coclasses, one type per file) and `<module>/Examples/` (worked code examples in VB/VBA/C#/VB.NET/C++).

---

## sldworksapivb6

SOLIDWORKS API (VB6 / COM) — the core sldworks/swconst object model, classic COM bindings


**Reference** (102 files):

- `sldworksapivb6_Reference_001_-Animation.pdf` —  … Animation (7 types)
- `sldworksapivb6_Reference_002_Annotation-AppearanceSetting.pdf` — Annotation … AppearanceSetting (3 types)
- `sldworksapivb6_Reference_003_AssemblyDoc (part 1 of 2).pdf` — AssemblyDoc (part 1 of 2)
- `sldworksapivb6_Reference_004_AssemblyDoc (part 2 of 2).pdf` — AssemblyDoc (part 2 of 2)
- `sldworksapivb6_Reference_005_Attribute-BendTableAnnotation.pdf` — Attribute … BendTableAnnotation (10 types)
- `sldworksapivb6_Reference_006_BlockDefinition-Body.pdf` — BlockDefinition … Body (3 types)
- `sldworksapivb6_Reference_007_Body2.pdf` — Body2
- `sldworksapivb6_Reference_008_BodyFolder-BreakLine.pdf` — BodyFolder … BreakLine (9 types)
- `sldworksapivb6_Reference_009_BrokenOutSectionFeatureData-ChainPatternFeatureData.pdf` — BrokenOutSectionFeatureData … ChainPatternFeatureData (14 types)
- `sldworksapivb6_Reference_010_ChamferFeatureData-CombineBodiesFeatureData.pdf` — ChamferFeatureData … CombineBodiesFeatureData (13 types)
- `sldworksapivb6_Reference_011_CommandGroup-Component.pdf` — CommandGroup … Component (8 types)
- `sldworksapivb6_Reference_012_Component2-ConcentricMateFeatureData.pdf` — Component2 … ConcentricMateFeatureData (3 types)
- `sldworksapivb6_Reference_013_Configuration-CoordinateSystemFeatureData.pdf` — Configuration … CoordinateSystemFeatureData (6 types)
- `sldworksapivb6_Reference_014_CoreFeatureData-CThread.pdf` — CoreFeatureData … CThread (13 types)
- `sldworksapivb6_Reference_015_Curve-CustomPropertyManager.pdf` — Curve … CustomPropertyManager (5 types)
- `sldworksapivb6_Reference_016_CustomSymbol-DeleteFaceFeatureData.pdf` — CustomSymbol … DeleteFaceFeatureData (9 types)
- `sldworksapivb6_Reference_017_DerivedPartFeatureData-DimensionSensorData.pdf` — DerivedPartFeatureData … DimensionSensorData (7 types)
- `sldworksapivb6_Reference_018_DimensionTolerance-DisplayData.pdf` — DimensionTolerance … DisplayData (4 types)
- `sldworksapivb6_Reference_019_DisplayDimension-DistanceMateFeatureData.pdf` — DisplayDimension … DistanceMateFeatureData (3 types)
- `sldworksapivb6_Reference_020_DocumentSpecification-DraftFeatureData2.pdf` — DocumentSpecification … DraftFeatureData2 (7 types)
- `sldworksapivb6_Reference_021_DragArrowManipulator-DrawingComponent.pdf` — DragArrowManipulator … DrawingComponent (3 types)
- `sldworksapivb6_Reference_022_DrawingDoc (part 1 of 3).pdf` — DrawingDoc (part 1 of 3)
- `sldworksapivb6_Reference_023_DrawingDoc (part 2 of 3).pdf` — DrawingDoc (part 2 of 3)
- `sldworksapivb6_Reference_024_DrawingDoc (part 3 of 3).pdf` — DrawingDoc (part 3 of 3)
- `sldworksapivb6_Reference_025_DrSection-Edge.pdf` — DrSection … Edge (3 types)
- `sldworksapivb6_Reference_026_EdgeFlangeFeatureData-EnumSketchHatches.pdf` — EdgeFlangeFeatureData … EnumSketchHatches (20 types)
- `sldworksapivb6_Reference_027_EnumSketchPoints-ExtrudeFeatureData.pdf` — EnumSketchPoints … ExtrudeFeatureData (7 types)
- `sldworksapivb6_Reference_028_ExtrudeFeatureData2-Face.pdf` — ExtrudeFeatureData2 … Face (2 types)
- `sldworksapivb6_Reference_029_Face2-FaultEntity.pdf` — Face2 … FaultEntity (7 types)
- `sldworksapivb6_Reference_030_FeatMgrView-FeatureFolder.pdf` — FeatMgrView … FeatureFolder (3 types)
- `sldworksapivb6_Reference_031_FeatureManager (part 1 of 3).pdf` — FeatureManager (part 1 of 3)
- `sldworksapivb6_Reference_032_FeatureManager (part 2 of 3).pdf` — FeatureManager (part 2 of 3)
- `sldworksapivb6_Reference_033_FeatureManager (part 3 of 3).pdf` — FeatureManager (part 3 of 3)
- `sldworksapivb6_Reference_034_FeatureStatistics-GeneralTableFeature.pdf` — FeatureStatistics … GeneralTableFeature (12 types)
- `sldworksapivb6_Reference_035_GeneralToleranceTableAnnotation-HealEdgesFeatureData.pdf` — GeneralToleranceTableAnnotation … HealEdgesFeatureData (8 types)
- `sldworksapivb6_Reference_036_HelixFeatureData-IAdvancedSaveAsOptions.pdf` — HelixFeatureData … IAdvancedSaveAsOptions (12 types)
- `sldworksapivb6_Reference_037_IAngleMateFeatureData-ICalloutStringVariable.pdf` — IAngleMateFeatureData … ICalloutStringVariable (15 types)
- `sldworksapivb6_Reference_038_ICalloutVariable-ICornerReliefFeatureData.pdf` — ICalloutVariable … ICornerReliefFeatureData (18 types)
- `sldworksapivb6_Reference_039_ICornerTreatmentFeatureData-IDSPBRMaterial.pdf` — ICornerTreatmentFeatureData … IDSPBRMaterial (14 types)
- `sldworksapivb6_Reference_040_IFacet-IHoleStandardsData.pdf` — IFacet … IHoleStandardsData (18 types)
- `sldworksapivb6_Reference_041_IImport3DInterconnectData-IMateFeatureData.pdf` — IImport3DInterconnectData … IMateFeatureData (14 types)
- `sldworksapivb6_Reference_042_IMBD3DPdfData-IndentFeatureData.pdf` — IMBD3DPdfData … IndentFeatureData (14 types)
- `sldworksapivb6_Reference_043_InstanceToVaryOptions-IPMIDimensionData.pdf` — InstanceToVaryOptions … IPMIDimensionData (16 types)
- `sldworksapivb6_Reference_044_IPMIDimensionItem-IRayTraceRenderer.pdf` — IPMIDimensionItem … IRayTraceRenderer (18 types)
- `sldworksapivb6_Reference_045_IRayTraceRendererOptions-ISheetMetalFolder.pdf` — IRayTraceRendererOptions … ISheetMetalFolder (15 types)
- `sldworksapivb6_Reference_046_ISheetMetalGaugeTableParameters-IStraightElementData.pdf` — ISheetMetalGaugeTableParameters … IStraightElementData (15 types)
- `sldworksapivb6_Reference_047_IStraightTapElementData-ITabAndSlotGroupData.pdf` — IStraightTapElementData … ITabAndSlotGroupData (14 types)
- `sldworksapivb6_Reference_048_ITangentMateFeatureData-LayerMgr.pdf` — ITangentMateFeatureData … LayerMgr (14 types)
- `sldworksapivb6_Reference_049_LibraryFeatureData-LocalCurvePatternFeatureData.pdf` — LibraryFeatureData … LocalCurvePatternFeatureData (8 types)
- `sldworksapivb6_Reference_050_LocalLinearPatternFeatureData-Loop.pdf` — LocalLinearPatternFeatureData … Loop (6 types)
- `sldworksapivb6_Reference_051_Loop2-MassProperty.pdf` — Loop2 … MassProperty (5 types)
- `sldworksapivb6_Reference_052_MassProperty2-MathTransform.pdf` — MassProperty2 … MathTransform (14 types)
- `sldworksapivb6_Reference_053_MathUtility-MidSurface3.pdf` — MathUtility … MidSurface3 (11 types)
- `sldworksapivb6_Reference_054_MirrorComponentFeatureData-MiterFlangeFeatureData.pdf` — MirrorComponentFeatureData … MiterFlangeFeatureData (5 types)
- `sldworksapivb6_Reference_055_ModelDoc (part 1 of 4).pdf` — ModelDoc (part 1 of 4)
- `sldworksapivb6_Reference_056_ModelDoc (part 2 of 4).pdf` — ModelDoc (part 2 of 4)
- `sldworksapivb6_Reference_057_ModelDoc (part 3 of 4).pdf` — ModelDoc (part 3 of 4)
- `sldworksapivb6_Reference_058_ModelDoc (part 4 of 4).pdf` — ModelDoc (part 4 of 4)
- `sldworksapivb6_Reference_059_ModelDoc2 (part 1 of 5).pdf` — ModelDoc2 (part 1 of 5)
- `sldworksapivb6_Reference_060_ModelDoc2 (part 2 of 5).pdf` — ModelDoc2 (part 2 of 5)
- `sldworksapivb6_Reference_061_ModelDoc2 (part 3 of 5).pdf` — ModelDoc2 (part 3 of 5)
- `sldworksapivb6_Reference_062_ModelDoc2 (part 4 of 5).pdf` — ModelDoc2 (part 4 of 5)
- `sldworksapivb6_Reference_063_ModelDoc2 (part 5 of 5).pdf` — ModelDoc2 (part 5 of 5)
- `sldworksapivb6_Reference_064_ModelDocExtension (part 1 of 3).pdf` — ModelDocExtension (part 1 of 3)
- `sldworksapivb6_Reference_065_ModelDocExtension (part 2 of 3).pdf` — ModelDocExtension (part 2 of 3)
- `sldworksapivb6_Reference_066_ModelDocExtension (part 3 of 3).pdf` — ModelDocExtension (part 3 of 3)
- `sldworksapivb6_Reference_067_Modeler.pdf` — Modeler
- `sldworksapivb6_Reference_068_ModelView-MotionPlotFeatureData.pdf` — ModelView … MotionPlotFeatureData (5 types)
- `sldworksapivb6_Reference_069_Mouse-MultiJogLeader.pdf` — Mouse … MultiJogLeader (4 types)
- `sldworksapivb6_Reference_070_Note-PackAndGo.pdf` — Note … PackAndGo (3 types)
- `sldworksapivb6_Reference_071_PageSetup-Parameter.pdf` — PageSetup … Parameter (4 types)
- `sldworksapivb6_Reference_072_PartDoc (part 1 of 2).pdf` — PartDoc (part 1 of 2)
- `sldworksapivb6_Reference_073_PartDoc (part 2 of 2).pdf` — PartDoc (part 2 of 2)
- `sldworksapivb6_Reference_074_PartExplodeStep-PrimaryMemberPathSegmentFeatureData.pdf` — PartExplodeStep … PrimaryMemberPathSegmentFeatureData (18 types)
- `sldworksapivb6_Reference_075_PrimaryMemberPointLengthFeatureData-PropertyManagerPageCombobox.pdf` — PrimaryMemberPointLengthFeatureData … PropertyManagerPageCombobox (17 types)
- `sldworksapivb6_Reference_076_PropertyManagerPageControl-RayTraceRenderer.pdf` — PropertyManagerPageControl … RayTraceRenderer (15 types)
- `sldworksapivb6_Reference_077_RayTraceRendererOptions-RenamedDocumentReferences.pdf` — RayTraceRendererOptions … RenamedDocumentReferences (10 types)
- `sldworksapivb6_Reference_078_RenderMaterial-RevolveFeatureData.pdf` — RenderMaterial … RevolveFeatureData (6 types)
- `sldworksapivb6_Reference_079_RevolveFeatureData2-SecondaryMemberBetweenPointsFeatureData.pdf` — RevolveFeatureData2 … SecondaryMemberBetweenPointsFeatureData (12 types)
- `sldworksapivb6_Reference_080_SecondaryMemberSupportPlaneFeatureData-Sensor.pdf` — SecondaryMemberSupportPlaneFeatureData … Sensor (10 types)
- `sldworksapivb6_Reference_081_SFSymbol-ShellFeatureData.pdf` — SFSymbol … ShellFeatureData (6 types)
- `sldworksapivb6_Reference_082_ShutOffSurfaceFeatureData-Simulation.pdf` — ShutOffSurfaceFeatureData … Simulation (8 types)
- `sldworksapivb6_Reference_083_Simulation3DContactFeatureData-SimulationSpringFeatureData.pdf` — Simulation3DContactFeatureData … SimulationSpringFeatureData (7 types)
- `sldworksapivb6_Reference_084_Sketch-SketchBlockDefinition.pdf` — Sketch … SketchBlockDefinition (3 types)
- `sldworksapivb6_Reference_085_SketchBlockInstance-SketchLine.pdf` — SketchBlockInstance … SketchLine (6 types)
- `sldworksapivb6_Reference_086_SketchManager-SketchPatternFeatureData.pdf` — SketchManager … SketchPatternFeatureData (4 types)
- `sldworksapivb6_Reference_087_SketchPicture-SldWorks.pdf` — SketchPicture … SldWorks (12 types)
- `sldworksapivb6_Reference_088_SldWorks-SldWorks.pdf` — SldWorks … SldWorks (183 types)
- `sldworksapivb6_Reference_089_SldWorks-SldWorks.pdf` — SldWorks … SldWorks (186 types)
- `sldworksapivb6_Reference_090_SldWorks-SplitBodyFeatureData.pdf` — SldWorks … SplitBodyFeatureData (35 types)
- `sldworksapivb6_Reference_091_SplitLineFeatureData-StructureSystemSplitMember.pdf` — SplitLineFeatureData … StructureSystemSplitMember (12 types)
- `sldworksapivb6_Reference_092_Surface-SurfaceRadiateFeatureData.pdf` — Surface … SurfaceRadiateFeatureData (8 types)
- `sldworksapivb6_Reference_093_SurfaceTrimFeatureData-SWPropertySheet.pdf` — SurfaceTrimFeatureData … SWPropertySheet (10 types)
- `sldworksapivb6_Reference_094_SWScene-TableAnnotation.pdf` — SWScene … TableAnnotation (6 types)
- `sldworksapivb6_Reference_095_TablePatternFeatureData-ThickenFeatureData.pdf` — TablePatternFeatureData … ThickenFeatureData (9 types)
- `sldworksapivb6_Reference_096_ThreadFeatureData-VariableFilletFeatureData.pdf` — ThreadFeatureData … VariableFilletFeatureData (13 types)
- `sldworksapivb6_Reference_097_VariableFilletFeatureData2-Vertex.pdf` — VariableFilletFeatureData2 … Vertex (3 types)
- `sldworksapivb6_Reference_098_View (part 1 of 3).pdf` — View (part 1 of 3)
- `sldworksapivb6_Reference_099_View (part 2 of 3).pdf` — View (part 2 of 3)
- `sldworksapivb6_Reference_100_View (part 3 of 3).pdf` — View (part 3 of 3)
- `sldworksapivb6_Reference_101_View3D-WizardHoleFeatureData.pdf` — View3D … WizardHoleFeatureData (9 types)
- `sldworksapivb6_Reference_102_WizardHoleFeatureData2-WrapSketchFeatureData.pdf` — WizardHoleFeatureData2 … WrapSketchFeatureData (2 types)

**Examples** (1 files):

- `sldworksapivb6_Examples_001_SldWorks_gettingstarted-SldWorks_references.pdf` — SldWorks_gettingstarted … SldWorks_references (4 types)

---

## sldworksapi

SOLIDWORKS API (.NET / VBA / C#) — the core sldworks object model, modern bindings


**Reference** (337 files):

- `sldworksapi_Reference_001__namespace_hierarchy-DAssemblyDocEvents_FeatureSketchEditPreN.pdf` — _namespace_hierarchy … DAssemblyDocEvents_FeatureSketchEditPreNotifyEventHandler (57 types)
- `sldworksapi_Reference_002_DAssemblyDocEvents_FileDropNotifyEventHa-DDrawingDocEvents_AutoSaveToStorageNotif.pdf` — DAssemblyDocEvents_FileDropNotifyEventHandler … DDrawingDocEvents_AutoSaveToStorageNotifyEventHandler (56 types)
- `sldworksapi_Reference_003_DDrawingDocEvents_AutoSaveToStorageStore-DModelViewEvents_DestroyNotifyEventHandl.pdf` — DDrawingDocEvents_AutoSaveToStorageStoreNotifyEventHandler … DModelViewEvents_DestroyNotifyEventHandler (57 types)
- `sldworksapi_Reference_004_DModelViewEvents_DisplayModeChangePostNo-DPartDocEvents_DynamicHighlightNotifyEve.pdf` — DModelViewEvents_DisplayModeChangePostNotifyEventHandler … DPartDocEvents_DynamicHighlightNotifyEventHandler (55 types)
- `sldworksapi_Reference_005_DPartDocEvents_EquationEditorPostNotifyE-DSldWorksEvents_Begin3DInterconnectTrans.pdf` — DPartDocEvents_EquationEditorPostNotifyEventHandler … DSldWorksEvents_Begin3DInterconnectTranslationNotifyEventHandler (57 types)
- `sldworksapi_Reference_006_DSldWorksEvents_BeginRecordNotifyEventHa-DTaskpaneViewEvents_TaskPaneToolbarButto.pdf` — DSldWorksEvents_BeginRecordNotifyEventHandler … DTaskpaneViewEvents_TaskPaneToolbarButtonClickedEventHandler (46 types)
- `sldworksapi_Reference_007_IAdvancedHoleElementData-IAdvancedSaveAsOptions.pdf` — IAdvancedHoleElementData … IAdvancedSaveAsOptions (3 types)
- `sldworksapi_Reference_008_IAdvancedSelectionCriteria-IAnimation.pdf` — IAdvancedSelectionCriteria … IAnimation (3 types)
- `sldworksapi_Reference_009_IAnnotation (part 1 of 3).pdf` — IAnnotation (part 1 of 3)
- `sldworksapi_Reference_010_IAnnotation (part 2 of 3).pdf` — IAnnotation (part 2 of 3)
- `sldworksapi_Reference_011_IAnnotation (part 3 of 3).pdf` — IAnnotation (part 3 of 3)
- `sldworksapi_Reference_012_IAnnotationView-IAppearanceSetting.pdf` — IAnnotationView … IAppearanceSetting (2 types)
- `sldworksapi_Reference_013_IAssemblyDoc (part 1 of 5).pdf` — IAssemblyDoc (part 1 of 5)
- `sldworksapi_Reference_014_IAssemblyDoc (part 2 of 5).pdf` — IAssemblyDoc (part 2 of 5)
- `sldworksapi_Reference_015_IAssemblyDoc (part 3 of 5).pdf` — IAssemblyDoc (part 3 of 5)
- `sldworksapi_Reference_016_IAssemblyDoc (part 4 of 5).pdf` — IAssemblyDoc (part 4 of 5)
- `sldworksapi_Reference_017_IAssemblyDoc (part 5 of 5).pdf` — IAssemblyDoc (part 5 of 5)
- `sldworksapi_Reference_018_IAttribute-IAttributeDef.pdf` — IAttribute … IAttributeDef (2 types)
- `sldworksapi_Reference_019_IAutoBalloonOptions-IBalloonStack.pdf` — IAutoBalloonOptions … IBalloonStack (3 types)
- `sldworksapi_Reference_020_IBaseFlangeFeatureData.pdf` — IBaseFlangeFeatureData
- `sldworksapi_Reference_021_IBeltChainFeatureData-IBendTableAnnotation.pdf` — IBeltChainFeatureData … IBendTableAnnotation (4 types)
- `sldworksapi_Reference_022_IBlockDefinition-IBlockInstance.pdf` — IBlockDefinition … IBlockInstance (2 types)
- `sldworksapi_Reference_023_IBody (part 1 of 3).pdf` — IBody (part 1 of 3)
- `sldworksapi_Reference_024_IBody (part 2 of 3).pdf` — IBody (part 2 of 3)
- `sldworksapi_Reference_025_IBody (part 3 of 3).pdf` — IBody (part 3 of 3)
- `sldworksapi_Reference_026_IBody2 (part 1 of 5).pdf` — IBody2 (part 1 of 5)
- `sldworksapi_Reference_027_IBody2 (part 2 of 5).pdf` — IBody2 (part 2 of 5)
- `sldworksapi_Reference_028_IBody2 (part 3 of 5).pdf` — IBody2 (part 3 of 5)
- `sldworksapi_Reference_029_IBody2 (part 4 of 5).pdf` — IBody2 (part 4 of 5)
- `sldworksapi_Reference_030_IBody2 (part 5 of 5).pdf` — IBody2 (part 5 of 5)
- `sldworksapi_Reference_031_IBodyFolder-IBomFeature.pdf` — IBodyFolder … IBomFeature (2 types)
- `sldworksapi_Reference_032_IBomTable-IBomTableAnnotation.pdf` — IBomTable … IBomTableAnnotation (2 types)
- `sldworksapi_Reference_033_IBomTableSortData-IBoundaryBossFeatureData.pdf` — IBomTableSortData … IBoundaryBossFeatureData (2 types)
- `sldworksapi_Reference_034_IBoundingBoxFeatureData-IBrokenOutSectionFeatureData.pdf` — IBoundingBoxFeatureData … IBrokenOutSectionFeatureData (4 types)
- `sldworksapi_Reference_035_IBSurfParamData-ICalloutStringVariable.pdf` — IBSurfParamData … ICalloutStringVariable (5 types)
- `sldworksapi_Reference_036_ICalloutVariable.pdf` — ICalloutVariable
- `sldworksapi_Reference_037_ICamera-ICavityFeatureData.pdf` — ICamera … ICavityFeatureData (3 types)
- `sldworksapi_Reference_038_ICenterLine-ICenterOfMass.pdf` — ICenterLine … ICenterOfMass (3 types)
- `sldworksapi_Reference_039_IChainPatternFeatureData-IChamferFeatureData.pdf` — IChainPatternFeatureData … IChamferFeatureData (2 types)
- `sldworksapi_Reference_040_IChamferFeatureData2.pdf` — IChamferFeatureData2
- `sldworksapi_Reference_041_ICircularPatternFeatureData.pdf` — ICircularPatternFeatureData
- `sldworksapi_Reference_042_IClearanceResult-ICoEdge.pdf` — IClearanceResult … ICoEdge (4 types)
- `sldworksapi_Reference_043_ICoincidentMateFeatureData-IColorTable.pdf` — ICoincidentMateFeatureData … IColorTable (5 types)
- `sldworksapi_Reference_044_ICombineBodiesFeatureData-ICommandGroup.pdf` — ICombineBodiesFeatureData … ICommandGroup (2 types)
- `sldworksapi_Reference_045_ICommandManager-ICommandTabBox.pdf` — ICommandManager … ICommandTabBox (3 types)
- `sldworksapi_Reference_046_IComment-IComplexCornerTreatmentFeatureData.pdf` — IComment … IComplexCornerTreatmentFeatureData (3 types)
- `sldworksapi_Reference_047_IComponent.pdf` — IComponent
- `sldworksapi_Reference_048_IComponent2 (part 1 of 4).pdf` — IComponent2 (part 1 of 4)
- `sldworksapi_Reference_049_IComponent2 (part 2 of 4).pdf` — IComponent2 (part 2 of 4)
- `sldworksapi_Reference_050_IComponent2 (part 3 of 4).pdf` — IComponent2 (part 3 of 4)
- `sldworksapi_Reference_051_IComponent2 (part 4 of 4).pdf` — IComponent2 (part 4 of 4)
- `sldworksapi_Reference_052_ICompositeCurveFeatureData-IConcentricMateFeatureData.pdf` — ICompositeCurveFeatureData … IConcentricMateFeatureData (2 types)
- `sldworksapi_Reference_053_IConfiguration (part 1 of 2).pdf` — IConfiguration (part 1 of 2)
- `sldworksapi_Reference_054_IConfiguration (part 2 of 2).pdf` — IConfiguration (part 2 of 2)
- `sldworksapi_Reference_055_IConfigurationManager-IConnectionPointFeatureData.pdf` — IConfigurationManager … IConnectionPointFeatureData (2 types)
- `sldworksapi_Reference_056_IConvertSolidFeatureData-ICoordinateSystemElement.pdf` — IConvertSolidFeatureData … ICoordinateSystemElement (2 types)
- `sldworksapi_Reference_057_ICoordinateSystemFeatureData-ICoreFeatureData.pdf` — ICoordinateSystemFeatureData … ICoreFeatureData (2 types)
- `sldworksapi_Reference_058_ICornerManagementFolder-ICornerTreatmentGroupFolder.pdf` — ICornerManagementFolder … ICornerTreatmentGroupFolder (5 types)
- `sldworksapi_Reference_059_ICosmeticThreadFeatureData-ICosmeticWeldBeadFeatureData.pdf` — ICosmeticThreadFeatureData … ICosmeticWeldBeadFeatureData (2 types)
- `sldworksapi_Reference_060_ICosmeticWeldBeadFolder-ICThread.pdf` — ICosmeticWeldBeadFolder … ICThread (5 types)
- `sldworksapi_Reference_061_ICurve (part 1 of 2).pdf` — ICurve (part 1 of 2)
- `sldworksapi_Reference_062_ICurve (part 2 of 2).pdf` — ICurve (part 2 of 2)
- `sldworksapi_Reference_063_ICurveDrivenPatternFeatureData.pdf` — ICurveDrivenPatternFeatureData
- `sldworksapi_Reference_064_ICurveParamData-ICustomPropertyManager.pdf` — ICurveParamData … ICustomPropertyManager (3 types)
- `sldworksapi_Reference_065_ICustomSymbol-ICutListSortOptions.pdf` — ICustomSymbol … ICutListSortOptions (3 types)
- `sldworksapi_Reference_066_IDatumOrigin-IDatumTag.pdf` — IDatumOrigin … IDatumTag (2 types)
- `sldworksapi_Reference_067_IDatumTargetSym-IDecal.pdf` — IDatumTargetSym … IDecal (2 types)
- `sldworksapi_Reference_068_IDeleteBodyFeatureData-IDerivedPartFeatureData.pdf` — IDeleteBodyFeatureData … IDerivedPartFeatureData (3 types)
- `sldworksapi_Reference_069_IDerivedPatternFeatureData.pdf` — IDerivedPatternFeatureData
- `sldworksapi_Reference_070_IDesignTable.pdf` — IDesignTable
- `sldworksapi_Reference_071_IDetailCircle-IDiagnoseResult.pdf` — IDetailCircle … IDiagnoseResult (2 types)
- `sldworksapi_Reference_072_IDimension.pdf` — IDimension
- `sldworksapi_Reference_073_IDimensionSensorData-IDimensionTolerance.pdf` — IDimensionSensorData … IDimensionTolerance (2 types)
- `sldworksapi_Reference_074_IDimPatternFeatureData-IDimXpertManager.pdf` — IDimPatternFeatureData … IDimXpertManager (2 types)
- `sldworksapi_Reference_075_IDisplayData.pdf` — IDisplayData
- `sldworksapi_Reference_076_IDisplayDimension (part 1 of 3).pdf` — IDisplayDimension (part 1 of 3)
- `sldworksapi_Reference_077_IDisplayDimension (part 2 of 3).pdf` — IDisplayDimension (part 2 of 3)
- `sldworksapi_Reference_078_IDisplayDimension (part 3 of 3).pdf` — IDisplayDimension (part 3 of 3)
- `sldworksapi_Reference_079_IDisplayStateSetting-IDocumentSpecification.pdf` — IDisplayStateSetting … IDocumentSpecification (3 types)
- `sldworksapi_Reference_080_IDomeFeatureData-IDraftFeatureData.pdf` — IDomeFeatureData … IDraftFeatureData (4 types)
- `sldworksapi_Reference_081_IDraftFeatureData2-IDragArrowManipulator.pdf` — IDraftFeatureData2 … IDragArrowManipulator (2 types)
- `sldworksapi_Reference_082_IDragOperator.pdf` — IDragOperator
- `sldworksapi_Reference_083_IDrawingComponent.pdf` — IDrawingComponent
- `sldworksapi_Reference_084_IDrawingDoc (part 1 of 7).pdf` — IDrawingDoc (part 1 of 7)
- `sldworksapi_Reference_085_IDrawingDoc (part 2 of 7).pdf` — IDrawingDoc (part 2 of 7)
- `sldworksapi_Reference_086_IDrawingDoc (part 3 of 7).pdf` — IDrawingDoc (part 3 of 7)
- `sldworksapi_Reference_087_IDrawingDoc (part 4 of 7).pdf` — IDrawingDoc (part 4 of 7)
- `sldworksapi_Reference_088_IDrawingDoc (part 5 of 7).pdf` — IDrawingDoc (part 5 of 7)
- `sldworksapi_Reference_089_IDrawingDoc (part 6 of 7).pdf` — IDrawingDoc (part 6 of 7)
- `sldworksapi_Reference_090_IDrawingDoc (part 7 of 7).pdf` — IDrawingDoc (part 7 of 7)
- `sldworksapi_Reference_091_IDrSection.pdf` — IDrSection
- `sldworksapi_Reference_092_IDSPBRMaterial.pdf` — IDSPBRMaterial
- `sldworksapi_Reference_093_IEdge.pdf` — IEdge
- `sldworksapi_Reference_094_IEdgeFlangeFeatureData-IEdgePoint.pdf` — IEdgeFlangeFeatureData … IEdgePoint (2 types)
- `sldworksapi_Reference_095_IEndCapFeatureData-IEnumBodies2.pdf` — IEndCapFeatureData … IEnumBodies2 (4 types)
- `sldworksapi_Reference_096_IEnumCoEdges-IEnumFaces.pdf` — IEnumCoEdges … IEnumFaces (9 types)
- `sldworksapi_Reference_097_IEnumFaces2-IEnumSketchSegments.pdf` — IEnumFaces2 … IEnumSketchSegments (7 types)
- `sldworksapi_Reference_098_IEnvironment-IEquationMgr.pdf` — IEnvironment … IEquationMgr (2 types)
- `sldworksapi_Reference_099_IExplodeStep-IExportPdfData.pdf` — IExplodeStep … IExportPdfData (2 types)
- `sldworksapi_Reference_100_IExtrudeFeatureData.pdf` — IExtrudeFeatureData
- `sldworksapi_Reference_101_IExtrudeFeatureData2 (part 1 of 2).pdf` — IExtrudeFeatureData2 (part 1 of 2)
- `sldworksapi_Reference_102_IExtrudeFeatureData2 (part 2 of 2).pdf` — IExtrudeFeatureData2 (part 2 of 2)
- `sldworksapi_Reference_103_IFace (part 1 of 2).pdf` — IFace (part 1 of 2)
- `sldworksapi_Reference_104_IFace (part 2 of 2).pdf` — IFace (part 2 of 2)
- `sldworksapi_Reference_105_IFace2 (part 1 of 3).pdf` — IFace2 (part 1 of 3)
- `sldworksapi_Reference_106_IFace2 (part 2 of 3).pdf` — IFace2 (part 2 of 3)
- `sldworksapi_Reference_107_IFace2 (part 3 of 3).pdf` — IFace2 (part 3 of 3)
- `sldworksapi_Reference_108_IFaceDecalProperties-IFamilyTableAnnotation.pdf` — IFaceDecalProperties … IFamilyTableAnnotation (4 types)
- `sldworksapi_Reference_109_IFamilyTableFeature-IFeatMgrView.pdf` — IFamilyTableFeature … IFeatMgrView (3 types)
- `sldworksapi_Reference_110_IFeature (part 1 of 3).pdf` — IFeature (part 1 of 3)
- `sldworksapi_Reference_111_IFeature (part 2 of 3).pdf` — IFeature (part 2 of 3)
- `sldworksapi_Reference_112_IFeature (part 3 of 3).pdf` — IFeature (part 3 of 3)
- `sldworksapi_Reference_113_IFeatureFolder.pdf` — IFeatureFolder
- `sldworksapi_Reference_114_IFeatureManager (part 1 of 9).pdf` — IFeatureManager (part 1 of 9)
- `sldworksapi_Reference_115_IFeatureManager (part 2 of 9).pdf` — IFeatureManager (part 2 of 9)
- `sldworksapi_Reference_116_IFeatureManager (part 3 of 9).pdf` — IFeatureManager (part 3 of 9)
- `sldworksapi_Reference_117_IFeatureManager (part 4 of 9).pdf` — IFeatureManager (part 4 of 9)
- `sldworksapi_Reference_118_IFeatureManager (part 5 of 9).pdf` — IFeatureManager (part 5 of 9)
- `sldworksapi_Reference_119_IFeatureManager (part 6 of 9).pdf` — IFeatureManager (part 6 of 9)
- `sldworksapi_Reference_120_IFeatureManager (part 7 of 9).pdf` — IFeatureManager (part 7 of 9)
- `sldworksapi_Reference_121_IFeatureManager (part 8 of 9).pdf` — IFeatureManager (part 8 of 9)
- `sldworksapi_Reference_122_IFeatureManager (part 9 of 9).pdf` — IFeatureManager (part 9 of 9)
- `sldworksapi_Reference_123_IFeatureStatistics-IFillPatternFeatureData.pdf` — IFeatureStatistics … IFillPatternFeatureData (2 types)
- `sldworksapi_Reference_124_IFillSurfaceFeatureData.pdf` — IFillSurfaceFeatureData
- `sldworksapi_Reference_125_IFlatPatternFeatureData-IFlyoutGroup.pdf` — IFlatPatternFeatureData … IFlyoutGroup (3 types)
- `sldworksapi_Reference_126_IFoldsFeatureData-IFrame.pdf` — IFoldsFeatureData … IFrame (2 types)
- `sldworksapi_Reference_127_IFreePointCurveFeatureData-IGroundPlaneFeatureData.pdf` — IFreePointCurveFeatureData … IGroundPlaneFeatureData (8 types)
- `sldworksapi_Reference_128_IGtol (part 1 of 3).pdf` — IGtol (part 1 of 3)
- `sldworksapi_Reference_129_IGtol (part 2 of 3).pdf` — IGtol (part 2 of 3)
- `sldworksapi_Reference_130_IGtol (part 3 of 3).pdf` — IGtol (part 3 of 3)
- `sldworksapi_Reference_131_IGtolFrame-IHealEdgesFeatureData.pdf` — IGtolFrame … IHealEdgesFeatureData (3 types)
- `sldworksapi_Reference_132_IHelixFeatureData-IHingeMateFeatureData.pdf` — IHelixFeatureData … IHingeMateFeatureData (3 types)
- `sldworksapi_Reference_133_IHoleDataTable-IHoleSeriesFeatureData.pdf` — IHoleDataTable … IHoleSeriesFeatureData (2 types)
- `sldworksapi_Reference_134_IHoleSeriesFeatureData2-IHoleStandardsData.pdf` — IHoleSeriesFeatureData2 … IHoleStandardsData (2 types)
- `sldworksapi_Reference_135_IHoleTable-IImport3DInterconnectData.pdf` — IHoleTable … IImport3DInterconnectData (3 types)
- `sldworksapi_Reference_136_IImportDxfDwgData-IImportStepData.pdf` — IImportDxfDwgData … IImportStepData (4 types)
- `sldworksapi_Reference_137_IIndentFeatureData-IInterference.pdf` — IIndentFeatureData … IInterference (3 types)
- `sldworksapi_Reference_138_IInterferenceDetectionMgr-IIntersectFeatureData.pdf` — IInterferenceDetectionMgr … IIntersectFeatureData (2 types)
- `sldworksapi_Reference_139_IJogFeatureData-ILayer.pdf` — IJogFeatureData … ILayer (3 types)
- `sldworksapi_Reference_140_ILayerMgr-ILibraryFeatureData.pdf` — ILayerMgr … ILibraryFeatureData (2 types)
- `sldworksapi_Reference_141_ILibraryFormToolFeatureData-ILinearCouplerMateFeatureData.pdf` — ILibraryFormToolFeatureData … ILinearCouplerMateFeatureData (4 types)
- `sldworksapi_Reference_142_ILinearPatternFeatureData.pdf` — ILinearPatternFeatureData
- `sldworksapi_Reference_143_ILocalCircularPatternFeatureData.pdf` — ILocalCircularPatternFeatureData
- `sldworksapi_Reference_144_ILocalCurvePatternFeatureData.pdf` — ILocalCurvePatternFeatureData
- `sldworksapi_Reference_145_ILocalLinearPatternFeatureData.pdf` — ILocalLinearPatternFeatureData
- `sldworksapi_Reference_146_ILocalSketchPatternFeatureData-ILoftedBendsFeatureData.pdf` — ILocalSketchPatternFeatureData … ILoftedBendsFeatureData (3 types)
- `sldworksapi_Reference_147_ILoftFeatureData.pdf` — ILoftFeatureData
- `sldworksapi_Reference_148_ILoop-ILoop2.pdf` — ILoop … ILoop2 (2 types)
- `sldworksapi_Reference_149_IMacroFeatureData (part 1 of 2).pdf` — IMacroFeatureData (part 1 of 2)
- `sldworksapi_Reference_150_IMacroFeatureData (part 2 of 2).pdf` — IMacroFeatureData (part 2 of 2)
- `sldworksapi_Reference_151_IMagneticLine-IManipulator.pdf` — IMagneticLine … IManipulator (2 types)
- `sldworksapi_Reference_152_IMassProperty.pdf` — IMassProperty
- `sldworksapi_Reference_153_IMassProperty2-IMate.pdf` — IMassProperty2 … IMate (3 types)
- `sldworksapi_Reference_154_IMate2-IMateControllerFeatureData.pdf` — IMate2 … IMateControllerFeatureData (2 types)
- `sldworksapi_Reference_155_IMateEntity-IMateReference.pdf` — IMateEntity … IMateReference (6 types)
- `sldworksapi_Reference_156_IMaterialVisualPropertiesData-IMathTransform.pdf` — IMaterialVisualPropertiesData … IMathTransform (3 types)
- `sldworksapi_Reference_157_IMathUtility-IMathVector.pdf` — IMathUtility … IMathVector (2 types)
- `sldworksapi_Reference_158_IMBD3DPdfData-IMBDSTEP242Data.pdf` — IMBD3DPdfData … IMBDSTEP242Data (2 types)
- `sldworksapi_Reference_159_IMeasure-IMessageBarDefinition.pdf` — IMeasure … IMessageBarDefinition (3 types)
- `sldworksapi_Reference_160_IMidSurface-IMidSurface2.pdf` — IMidSurface … IMidSurface2 (2 types)
- `sldworksapi_Reference_161_IMidSurface3.pdf` — IMidSurface3
- `sldworksapi_Reference_162_IMirrorComponentFeatureData-IMirrorPartFeatureData.pdf` — IMirrorComponentFeatureData … IMirrorPartFeatureData (2 types)
- `sldworksapi_Reference_163_IMirrorPatternFeatureData-IMirrorSolidFeatureData.pdf` — IMirrorPatternFeatureData … IMirrorSolidFeatureData (2 types)
- `sldworksapi_Reference_164_IMiterFlangeFeatureData.pdf` — IMiterFlangeFeatureData
- `sldworksapi_Reference_165_IModelDoc (part 1 of 13).pdf` — IModelDoc (part 1 of 13)
- `sldworksapi_Reference_166_IModelDoc (part 2 of 13).pdf` — IModelDoc (part 2 of 13)
- `sldworksapi_Reference_167_IModelDoc (part 3 of 13).pdf` — IModelDoc (part 3 of 13)
- `sldworksapi_Reference_168_IModelDoc (part 4 of 13).pdf` — IModelDoc (part 4 of 13)
- `sldworksapi_Reference_169_IModelDoc (part 5 of 13).pdf` — IModelDoc (part 5 of 13)
- `sldworksapi_Reference_170_IModelDoc (part 6 of 13).pdf` — IModelDoc (part 6 of 13)
- `sldworksapi_Reference_171_IModelDoc (part 7 of 13).pdf` — IModelDoc (part 7 of 13)
- `sldworksapi_Reference_172_IModelDoc (part 8 of 13).pdf` — IModelDoc (part 8 of 13)
- `sldworksapi_Reference_173_IModelDoc (part 9 of 13).pdf` — IModelDoc (part 9 of 13)
- `sldworksapi_Reference_174_IModelDoc (part 10 of 13).pdf` — IModelDoc (part 10 of 13)
- `sldworksapi_Reference_175_IModelDoc (part 11 of 13).pdf` — IModelDoc (part 11 of 13)
- `sldworksapi_Reference_176_IModelDoc (part 12 of 13).pdf` — IModelDoc (part 12 of 13)
- `sldworksapi_Reference_177_IModelDoc (part 13 of 13).pdf` — IModelDoc (part 13 of 13)
- `sldworksapi_Reference_178_IModelDoc2 (part 1 of 17).pdf` — IModelDoc2 (part 1 of 17)
- `sldworksapi_Reference_179_IModelDoc2 (part 2 of 17).pdf` — IModelDoc2 (part 2 of 17)
- `sldworksapi_Reference_180_IModelDoc2 (part 3 of 17).pdf` — IModelDoc2 (part 3 of 17)
- `sldworksapi_Reference_181_IModelDoc2 (part 4 of 17).pdf` — IModelDoc2 (part 4 of 17)
- `sldworksapi_Reference_182_IModelDoc2 (part 5 of 17).pdf` — IModelDoc2 (part 5 of 17)
- `sldworksapi_Reference_183_IModelDoc2 (part 6 of 17).pdf` — IModelDoc2 (part 6 of 17)
- `sldworksapi_Reference_184_IModelDoc2 (part 7 of 17).pdf` — IModelDoc2 (part 7 of 17)
- `sldworksapi_Reference_185_IModelDoc2 (part 8 of 17).pdf` — IModelDoc2 (part 8 of 17)
- `sldworksapi_Reference_186_IModelDoc2 (part 9 of 17).pdf` — IModelDoc2 (part 9 of 17)
- `sldworksapi_Reference_187_IModelDoc2 (part 10 of 17).pdf` — IModelDoc2 (part 10 of 17)
- `sldworksapi_Reference_188_IModelDoc2 (part 11 of 17).pdf` — IModelDoc2 (part 11 of 17)
- `sldworksapi_Reference_189_IModelDoc2 (part 12 of 17).pdf` — IModelDoc2 (part 12 of 17)
- `sldworksapi_Reference_190_IModelDoc2 (part 13 of 17).pdf` — IModelDoc2 (part 13 of 17)
- `sldworksapi_Reference_191_IModelDoc2 (part 14 of 17).pdf` — IModelDoc2 (part 14 of 17)
- `sldworksapi_Reference_192_IModelDoc2 (part 15 of 17).pdf` — IModelDoc2 (part 15 of 17)
- `sldworksapi_Reference_193_IModelDoc2 (part 16 of 17).pdf` — IModelDoc2 (part 16 of 17)
- `sldworksapi_Reference_194_IModelDoc2 (part 17 of 17).pdf` — IModelDoc2 (part 17 of 17)
- `sldworksapi_Reference_195_IModelDocExtension (part 1 of 8).pdf` — IModelDocExtension (part 1 of 8)
- `sldworksapi_Reference_196_IModelDocExtension (part 2 of 8).pdf` — IModelDocExtension (part 2 of 8)
- `sldworksapi_Reference_197_IModelDocExtension (part 3 of 8).pdf` — IModelDocExtension (part 3 of 8)
- `sldworksapi_Reference_198_IModelDocExtension (part 4 of 8).pdf` — IModelDocExtension (part 4 of 8)
- `sldworksapi_Reference_199_IModelDocExtension (part 5 of 8).pdf` — IModelDocExtension (part 5 of 8)
- `sldworksapi_Reference_200_IModelDocExtension (part 6 of 8).pdf` — IModelDocExtension (part 6 of 8)
- `sldworksapi_Reference_201_IModelDocExtension (part 7 of 8).pdf` — IModelDocExtension (part 7 of 8)
- `sldworksapi_Reference_202_IModelDocExtension (part 8 of 8).pdf` — IModelDocExtension (part 8 of 8)
- `sldworksapi_Reference_203_IModeler (part 1 of 4).pdf` — IModeler (part 1 of 4)
- `sldworksapi_Reference_204_IModeler (part 2 of 4).pdf` — IModeler (part 2 of 4)
- `sldworksapi_Reference_205_IModeler (part 3 of 4).pdf` — IModeler (part 3 of 4)
- `sldworksapi_Reference_206_IModeler (part 4 of 4).pdf` — IModeler (part 4 of 4)
- `sldworksapi_Reference_207_IModelView (part 1 of 2).pdf` — IModelView (part 1 of 2)
- `sldworksapi_Reference_208_IModelView (part 2 of 2).pdf` — IModelView (part 2 of 2)
- `sldworksapi_Reference_209_IModelViewManager.pdf` — IModelViewManager
- `sldworksapi_Reference_210_IModelWindow-IMoveCopyBodyFeatureData.pdf` — IModelWindow … IMoveCopyBodyFeatureData (5 types)
- `sldworksapi_Reference_211_IMoveFaceFeatureData-IMultiJogLeader.pdf` — IMoveFaceFeatureData … IMultiJogLeader (2 types)
- `sldworksapi_Reference_212_INote (part 1 of 3).pdf` — INote (part 1 of 3)
- `sldworksapi_Reference_213_INote (part 2 of 3).pdf` — INote (part 2 of 3)
- `sldworksapi_Reference_214_INote (part 3 of 3).pdf` — INote (part 3 of 3)
- `sldworksapi_Reference_215_IOneBendFeatureData.pdf` — IOneBendFeatureData
- `sldworksapi_Reference_216_IPackAndGo-IPageSetup.pdf` — IPackAndGo … IPageSetup (2 types)
- `sldworksapi_Reference_217_IParagraphs-IParameter.pdf` — IParagraphs … IParameter (3 types)
- `sldworksapi_Reference_218_IPartDoc (part 1 of 4).pdf` — IPartDoc (part 1 of 4)
- `sldworksapi_Reference_219_IPartDoc (part 2 of 4).pdf` — IPartDoc (part 2 of 4)
- `sldworksapi_Reference_220_IPartDoc (part 3 of 4).pdf` — IPartDoc (part 3 of 4)
- `sldworksapi_Reference_221_IPartDoc (part 4 of 4).pdf` — IPartDoc (part 4 of 4)
- `sldworksapi_Reference_222_IPartExplodeStep-IPartingLineFeatureData.pdf` — IPartExplodeStep … IPartingLineFeatureData (3 types)
- `sldworksapi_Reference_223_IPartingSurfaceFeatureData-IPMIDatumFeature.pdf` — IPartingSurfaceFeatureData … IPMIDatumFeature (6 types)
- `sldworksapi_Reference_224_IPMIDatumTarget-IPMIFrameData.pdf` — IPMIDatumTarget … IPMIFrameData (4 types)
- `sldworksapi_Reference_225_IPMIGtolBoxData-IPrimaryMemberFacePlaneIntersectionFeatu.pdf` — IPMIGtolBoxData … IPrimaryMemberFacePlaneIntersectionFeatureData (4 types)
- `sldworksapi_Reference_226_IPrimaryMemberPathSegmentFeatureData-IPrint3DDialog.pdf` — IPrimaryMemberPathSegmentFeatureData … IPrint3DDialog (5 types)
- `sldworksapi_Reference_227_IPrintSpecification-IProjectionArrow.pdf` — IPrintSpecification … IProjectionArrow (4 types)
- `sldworksapi_Reference_228_IProjectionCurveFeatureData-IPropertyManagerPage.pdf` — IProjectionCurveFeatureData … IPropertyManagerPage (2 types)
- `sldworksapi_Reference_229_IPropertyManagerPage2-IPropertyManagerPageBitmap.pdf` — IPropertyManagerPage2 … IPropertyManagerPageBitmap (3 types)
- `sldworksapi_Reference_230_IPropertyManagerPageBitmapButton-IPropertyManagerPageControl.pdf` — IPropertyManagerPageBitmapButton … IPropertyManagerPageControl (5 types)
- `sldworksapi_Reference_231_IPropertyManagerPageGroup-IPropertyManagerPageListbox.pdf` — IPropertyManagerPageGroup … IPropertyManagerPageListbox (3 types)
- `sldworksapi_Reference_232_IPropertyManagerPageNumberbox-IPropertyManagerPageSelectionbox.pdf` — IPropertyManagerPageNumberbox … IPropertyManagerPageSelectionbox (3 types)
- `sldworksapi_Reference_233_IPropertyManagerPageSlider-IPunchTableAnnotation.pdf` — IPropertyManagerPageSlider … IPunchTableAnnotation (6 types)
- `sldworksapi_Reference_234_IRackPinionMateFeatureData-IRayTraceRenderer.pdf` — IRackPinionMateFeatureData … IRayTraceRenderer (2 types)
- `sldworksapi_Reference_235_IRayTraceRendererOptions-IRefAxis.pdf` — IRayTraceRendererOptions … IRefAxis (2 types)
- `sldworksapi_Reference_236_IRefAxisFeatureData-IRefPlane.pdf` — IRefAxisFeatureData … IRefPlane (4 types)
- `sldworksapi_Reference_237_IRefPlaneFeatureData-IRefPointFeatureData.pdf` — IRefPlaneFeatureData … IRefPointFeatureData (3 types)
- `sldworksapi_Reference_238_IRenamedDocumentReferences.pdf` — IRenamedDocumentReferences
- `sldworksapi_Reference_239_IRenderMaterial (part 1 of 2).pdf` — IRenderMaterial (part 1 of 2)
- `sldworksapi_Reference_240_IRenderMaterial (part 2 of 2).pdf` — IRenderMaterial (part 2 of 2)
- `sldworksapi_Reference_241_IReplaceFaceFeatureData-IRevisionTableAnnotation.pdf` — IReplaceFaceFeatureData … IRevisionTableAnnotation (3 types)
- `sldworksapi_Reference_242_IRevisionTableFeature-IRevolveFeatureData2.pdf` — IRevisionTableFeature … IRevolveFeatureData2 (3 types)
- `sldworksapi_Reference_243_IRibFeatureData-IRipFeatureData.pdf` — IRibFeatureData … IRipFeatureData (3 types)
- `sldworksapi_Reference_244_IRoutingSettings-IRuledSurfaceFeatureData.pdf` — IRoutingSettings … IRuledSurfaceFeatureData (2 types)
- `sldworksapi_Reference_245_ISafeArrayUtility-ISaveTo3DExperienceOptions.pdf` — ISafeArrayUtility … ISaveTo3DExperienceOptions (3 types)
- `sldworksapi_Reference_246_IScaleFeatureData-ISecondaryMemberSupportPlaneFeatureData.pdf` — IScaleFeatureData … ISecondaryMemberSupportPlaneFeatureData (4 types)
- `sldworksapi_Reference_247_ISecondaryMemberUpToMembersFeatureData-ISecondaryStructuralMemberFeatureData.pdf` — ISecondaryMemberUpToMembersFeatureData … ISecondaryStructuralMemberFeatureData (2 types)
- `sldworksapi_Reference_248_ISectionViewData-ISelectData.pdf` — ISectionViewData … ISelectData (2 types)
- `sldworksapi_Reference_249_ISelectionMgr (part 1 of 2).pdf` — ISelectionMgr (part 1 of 2)
- `sldworksapi_Reference_250_ISelectionMgr (part 2 of 2).pdf` — ISelectionMgr (part 2 of 2)
- `sldworksapi_Reference_251_ISelectionSet-ISensor.pdf` — ISelectionSet … ISensor (4 types)
- `sldworksapi_Reference_252_ISFSymbol.pdf` — ISFSymbol
- `sldworksapi_Reference_253_ISheet.pdf` — ISheet
- `sldworksapi_Reference_254_ISheetMetalFeatureData-ISheetMetalGaugeTableParameters.pdf` — ISheetMetalFeatureData … ISheetMetalGaugeTableParameters (3 types)
- `sldworksapi_Reference_255_IShellFeatureData-ISilhouetteEdge.pdf` — IShellFeatureData … ISilhouetteEdge (3 types)
- `sldworksapi_Reference_256_ISimpleCornerTreatmentFeatureData-ISimpleFilletFeatureData.pdf` — ISimpleCornerTreatmentFeatureData … ISimpleFilletFeatureData (2 types)
- `sldworksapi_Reference_257_ISimpleFilletFeatureData2 (part 1 of 2).pdf` — ISimpleFilletFeatureData2 (part 1 of 2)
- `sldworksapi_Reference_258_ISimpleFilletFeatureData2 (part 2 of 2).pdf` — ISimpleFilletFeatureData2 (part 2 of 2)
- `sldworksapi_Reference_259_ISimpleHoleFeatureData-ISimpleHoleFeatureData2.pdf` — ISimpleHoleFeatureData … ISimpleHoleFeatureData2 (2 types)
- `sldworksapi_Reference_260_ISimulation-ISimulationDamperFeatureData.pdf` — ISimulation … ISimulationDamperFeatureData (3 types)
- `sldworksapi_Reference_261_ISimulationForceFeatureData-ISimulationLinearSpringFeatureData.pdf` — ISimulationForceFeatureData … ISimulationLinearSpringFeatureData (3 types)
- `sldworksapi_Reference_262_ISimulationMotorFeatureData-ISimulationSpringFeatureData.pdf` — ISimulationMotorFeatureData … ISimulationSpringFeatureData (2 types)
- `sldworksapi_Reference_263_ISketch (part 1 of 3).pdf` — ISketch (part 1 of 3)
- `sldworksapi_Reference_264_ISketch (part 2 of 3).pdf` — ISketch (part 2 of 3)
- `sldworksapi_Reference_265_ISketch (part 3 of 3).pdf` — ISketch (part 3 of 3)
- `sldworksapi_Reference_266_ISketchArc.pdf` — ISketchArc
- `sldworksapi_Reference_267_ISketchBlockDefinition.pdf` — ISketchBlockDefinition
- `sldworksapi_Reference_268_ISketchBlockInstance-ISketchContour.pdf` — ISketchBlockInstance … ISketchContour (2 types)
- `sldworksapi_Reference_269_ISketchedBendFeatureData-ISketchEllipse.pdf` — ISketchedBendFeatureData … ISketchEllipse (2 types)
- `sldworksapi_Reference_270_ISketchHatch-ISketchLine.pdf` — ISketchHatch … ISketchLine (2 types)
- `sldworksapi_Reference_271_ISketchManager (part 1 of 3).pdf` — ISketchManager (part 1 of 3)
- `sldworksapi_Reference_272_ISketchManager (part 2 of 3).pdf` — ISketchManager (part 2 of 3)
- `sldworksapi_Reference_273_ISketchManager (part 3 of 3).pdf` — ISketchManager (part 3 of 3)
- `sldworksapi_Reference_274_ISketchParabola-ISketchPath.pdf` — ISketchParabola … ISketchPath (2 types)
- `sldworksapi_Reference_275_ISketchPatternFeatureData-ISketchPicture.pdf` — ISketchPatternFeatureData … ISketchPicture (2 types)
- `sldworksapi_Reference_276_ISketchPoint-ISketchRegion.pdf` — ISketchPoint … ISketchRegion (2 types)
- `sldworksapi_Reference_277_ISketchRelation-ISketchRelationManager.pdf` — ISketchRelation … ISketchRelationManager (2 types)
- `sldworksapi_Reference_278_ISketchSegment-ISketchSlot.pdf` — ISketchSegment … ISketchSlot (2 types)
- `sldworksapi_Reference_279_ISketchSpline.pdf` — ISketchSpline
- `sldworksapi_Reference_280_ISketchText.pdf` — ISketchText
- `sldworksapi_Reference_281_ISldWorks (part 1 of 8).pdf` — ISldWorks (part 1 of 8)
- `sldworksapi_Reference_282_ISldWorks (part 2 of 8).pdf` — ISldWorks (part 2 of 8)
- `sldworksapi_Reference_283_ISldWorks (part 3 of 8).pdf` — ISldWorks (part 3 of 8)
- `sldworksapi_Reference_284_ISldWorks (part 4 of 8).pdf` — ISldWorks (part 4 of 8)
- `sldworksapi_Reference_285_ISldWorks (part 5 of 8).pdf` — ISldWorks (part 5 of 8)
- `sldworksapi_Reference_286_ISldWorks (part 6 of 8).pdf` — ISldWorks (part 6 of 8)
- `sldworksapi_Reference_287_ISldWorks (part 7 of 8).pdf` — ISldWorks (part 7 of 8)
- `sldworksapi_Reference_288_ISldWorks (part 8 of 8).pdf` — ISldWorks (part 8 of 8)
- `sldworksapi_Reference_289_ISlicingData-ISMCornerReliefData.pdf` — ISlicingData … ISMCornerReliefData (4 types)
- `sldworksapi_Reference_290_ISMGussetFeatureData-ISMNormalCutFeatureData.pdf` — ISMGussetFeatureData … ISMNormalCutFeatureData (2 types)
- `sldworksapi_Reference_291_ISMNormalCutFeatureData2-ISplineHandle.pdf` — ISMNormalCutFeatureData2 … ISplineHandle (4 types)
- `sldworksapi_Reference_292_ISplineParamData-ISplitBodyFeatureData.pdf` — ISplineParamData … ISplitBodyFeatureData (2 types)
- `sldworksapi_Reference_293_ISplitLineFeatureData.pdf` — ISplitLineFeatureData
- `sldworksapi_Reference_294_ISpring-IStatusBarPane.pdf` — ISpring … IStatusBarPane (3 types)
- `sldworksapi_Reference_295_IStraightElementData-IStructuralMemberFeatureData.pdf` — IStraightElementData … IStructuralMemberFeatureData (3 types)
- `sldworksapi_Reference_296_IStructuralMemberGroup-IStructureSystemMemberFeatureData.pdf` — IStructuralMemberGroup … IStructureSystemMemberFeatureData (3 types)
- `sldworksapi_Reference_297_IStructureSystemMemberProfile-IStructureSystemSplitMember.pdf` — IStructureSystemMemberProfile … IStructureSystemSplitMember (2 types)
- `sldworksapi_Reference_298_ISurface (part 1 of 2).pdf` — ISurface (part 1 of 2)
- `sldworksapi_Reference_299_ISurface (part 2 of 2).pdf` — ISurface (part 2 of 2)
- `sldworksapi_Reference_300_ISurfaceCutFeatureData-ISurfaceFlattenFeatureData.pdf` — ISurfaceCutFeatureData … ISurfaceFlattenFeatureData (3 types)
- `sldworksapi_Reference_301_ISurfaceKnitFeatureData-ISurfaceParameterizationData.pdf` — ISurfaceKnitFeatureData … ISurfaceParameterizationData (3 types)
- `sldworksapi_Reference_302_ISurfacePlanarFeatureData-ISurfaceTrimFeatureData.pdf` — ISurfacePlanarFeatureData … ISurfaceTrimFeatureData (3 types)
- `sldworksapi_Reference_303_ISurfExtrudeFeatureData-ISurfRevolveFeatureData.pdf` — ISurfExtrudeFeatureData … ISurfRevolveFeatureData (2 types)
- `sldworksapi_Reference_304_ISweepFeatureData.pdf` — ISweepFeatureData
- `sldworksapi_Reference_305_ISweptFlangeFeatureData-ISwPEClassFactory.pdf` — ISweptFlangeFeatureData … ISwPEClassFactory (3 types)
- `sldworksapi_Reference_306_ISwPEToken-ISymmetricMateFeatureData.pdf` — ISwPEToken … ISymmetricMateFeatureData (4 types)
- `sldworksapi_Reference_307_ITabAndSlotFeatureData-ITableAnchor.pdf` — ITabAndSlotFeatureData … ITableAnchor (3 types)
- `sldworksapi_Reference_308_ITableAnnotation (part 1 of 2).pdf` — ITableAnnotation (part 1 of 2)
- `sldworksapi_Reference_309_ITableAnnotation (part 2 of 2).pdf` — ITableAnnotation (part 2 of 2)
- `sldworksapi_Reference_310_ITablePatternFeatureData-ITaperedTapElementData.pdf` — ITablePatternFeatureData … ITaperedTapElementData (3 types)
- `sldworksapi_Reference_311_ITaskpaneView.pdf` — ITaskpaneView
- `sldworksapi_Reference_312_ITessellation.pdf` — ITessellation
- `sldworksapi_Reference_313_ITextAndCustomProperty-ITexture.pdf` — ITextAndCustomProperty … ITexture (3 types)
- `sldworksapi_Reference_314_IThickenFeatureData.pdf` — IThickenFeatureData
- `sldworksapi_Reference_315_IThreadFeatureData-ITitleBlockTableAnnotation.pdf` — IThreadFeatureData … ITitleBlockTableAnnotation (3 types)
- `sldworksapi_Reference_316_ITitleBlockTableFeature-ITriadManipulator.pdf` — ITitleBlockTableFeature … ITriadManipulator (4 types)
- `sldworksapi_Reference_317_ITwoMemberCornerTreatmentFeatureData-IUserUnit.pdf` — ITwoMemberCornerTreatmentFeatureData … IUserUnit (5 types)
- `sldworksapi_Reference_318_IVariableFilletFeatureData.pdf` — IVariableFilletFeatureData
- `sldworksapi_Reference_319_IVariableFilletFeatureData2.pdf` — IVariableFilletFeatureData2
- `sldworksapi_Reference_320_IVersionCompatibilityItem-IVertex.pdf` — IVersionCompatibilityItem … IVertex (2 types)
- `sldworksapi_Reference_321_IView (part 1 of 10).pdf` — IView (part 1 of 10)
- `sldworksapi_Reference_322_IView (part 2 of 10).pdf` — IView (part 2 of 10)
- `sldworksapi_Reference_323_IView (part 3 of 10).pdf` — IView (part 3 of 10)
- `sldworksapi_Reference_324_IView (part 4 of 10).pdf` — IView (part 4 of 10)
- `sldworksapi_Reference_325_IView (part 5 of 10).pdf` — IView (part 5 of 10)
- `sldworksapi_Reference_326_IView (part 6 of 10).pdf` — IView (part 6 of 10)
- `sldworksapi_Reference_327_IView (part 7 of 10).pdf` — IView (part 7 of 10)
- `sldworksapi_Reference_328_IView (part 8 of 10).pdf` — IView (part 8 of 10)
- `sldworksapi_Reference_329_IView (part 9 of 10).pdf` — IView (part 9 of 10)
- `sldworksapi_Reference_330_IView (part 10 of 10).pdf` — IView (part 10 of 10)
- `sldworksapi_Reference_331_IView3D-IWeldmentBeadFeatureData.pdf` — IView3D … IWeldmentBeadFeatureData (3 types)
- `sldworksapi_Reference_332_IWeldmentCutListAnnotation-IWeldmentTrimExtendFeatureData.pdf` — IWeldmentCutListAnnotation … IWeldmentTrimExtendFeatureData (3 types)
- `sldworksapi_Reference_333_IWeldSymbol.pdf` — IWeldSymbol
- `sldworksapi_Reference_334_IWidthMateFeatureData-IWizardHoleFeatureData.pdf` — IWidthMateFeatureData … IWizardHoleFeatureData (2 types)
- `sldworksapi_Reference_335_IWizardHoleFeatureData2 (part 1 of 2).pdf` — IWizardHoleFeatureData2 (part 1 of 2)
- `sldworksapi_Reference_336_IWizardHoleFeatureData2 (part 2 of 2).pdf` — IWizardHoleFeatureData2 (part 2 of 2)
- `sldworksapi_Reference_337_IWrapSketchFeatureData-SolidWorks.Interop.sldworks.pdf` — IWrapSketchFeatureData … SolidWorks.Interop.sldworks (2 types)

**Examples** (50 files):

- `sldworksapi_Examples_001_Access_and_Release_Access_to_Edges_Examp-Add_.NET_Controls_to_SOLIDWORKS_Using_an.pdf` — Access_and_Release_Access_to_Edges_Example_CSharp … Add_.NET_Controls_to_SOLIDWORKS_Using_an_Add-in_Example_CSharp (22 types)
- `sldworksapi_Examples_002_Add_.NET_Controls_to_SolidWorks_Using_an-Add_and_Remove_Items_to_File_Save_As_and.pdf` — Add_.NET_Controls_to_SolidWorks_Using_an_Add-in_Example_VBNET … Add_and_Remove_Items_to_File_Save_As_and_Open_Menus_CSharp (37 types)
- `sldworksapi_Examples_003_Add_and_Remove_Items_to_File_Save_As_and-Add_Menu_and_Menu_Item_Example_CSharp.pdf` — Add_and_Remove_Items_to_File_Save_As_and_Open_Menus_VBNET … Add_Menu_and_Menu_Item_Example_CSharp (50 types)
- `sldworksapi_Examples_004_Add_Menu_and_Menu_Item_Example_VBNET-Analyze_Text_and_Geometry_in_GTol_Flat_S.pdf` — Add_Menu_and_Menu_Item_Example_VBNET … Analyze_Text_and_Geometry_in_GTol_Flat_Symbol_Example_VB (41 types)
- `sldworksapi_Examples_005_Analyze_Text_and_Geometry_in_GTol_Flat_S-Change_Active_Tab_in_Manager_Pane_Exampl.pdf` — Analyze_Text_and_Geometry_in_GTol_Flat_Symbol_Example_VBNET … Change_Active_Tab_in_Manager_Pane_Example_VBNET (70 types)
- `sldworksapi_Examples_006_Change_Angle_to_Supplementary_Angle_Exam-Combine_Bodies_Example_VBNET.pdf` — Change_Angle_to_Supplementary_Angle_Example_CSharp … Combine_Bodies_Example_VBNET (82 types)
- `sldworksapi_Examples_007_Compare_DimXpert_Annotations_in_Differen-Create_Advanced_Hole_Example_CSharp.pdf` — Compare_DimXpert_Annotations_in_Different_Versions_of_Same_Part_Example_CSharp … Create_Advanced_Hole_Example_CSharp (76 types)
- `sldworksapi_Examples_008_Create_Advanced_Hole_Example_VB-Create_Base_Flange_Feature_Example_CShar.pdf` — Create_Advanced_Hole_Example_VB … Create_Base_Flange_Feature_Example_CSharp (51 types)
- `sldworksapi_Examples_009_Create_Base_Flange_Feature_Example_VBNET-Create_Circular_Pattern_of_Subassembly_E.pdf` — Create_Base_Flange_Feature_Example_VBNET … Create_Circular_Pattern_of_Subassembly_Example_VBNET (34 types)
- `sldworksapi_Examples_010_Create_CommandManager_Tab_and_Tab_Boxes_-Create_Drawing_Sheet_Zones_Example_VBNET.pdf` — Create_CommandManager_Tab_and_Tab_Boxes_Example_CSharp … Create_Drawing_Sheet_Zones_Example_VBNET (45 types)
- `sldworksapi_Examples_011_Create_Edge_Flange_Example_CSharp-Create_Forming_Tool_Feature_Example_CSha.pdf` — Create_Edge_Flange_Example_CSharp … Create_Forming_Tool_Feature_Example_CSharp (27 types)
- `sldworksapi_Examples_012_Create_Forming_Tool_Feature_Example_VBNE-Create_Machining_Costing_Analyses_Exampl.pdf` — Create_Forming_Tool_Feature_Example_VBNET … Create_Machining_Costing_Analyses_Example_VBNET (63 types)
- `sldworksapi_Examples_013_Create_Macro_Feature_Subfeature_Example_-Create_Planar_Surface_Feature_Example_VB.pdf` — Create_Macro_Feature_Subfeature_Example_VB … Create_Planar_Surface_Feature_Example_VB (40 types)
- `sldworksapi_Examples_014_Create_Planar_Surface_Feature_Example_VB-Create_Ref_Plane_SSMember_Example_VBNET.pdf` — Create_Planar_Surface_Feature_Example_VBNET … Create_Ref_Plane_SSMember_Example_VBNET (25 types)
- `sldworksapi_Examples_015_Create_Reference_Curve_Example_CSharp-Create_Sketch_Point_Example_VB.pdf` — Create_Reference_Curve_Example_CSharp … Create_Sketch_Point_Example_VB (39 types)
- `sldworksapi_Examples_016_Create_Sketch_Point_Example_VBNET-Create_Swept_Flange_Using_Gauge_Table_Ex.pdf` — Create_Sketch_Point_Example_VBNET … Create_Swept_Flange_Using_Gauge_Table_Example_CSharp (42 types)
- `sldworksapi_Examples_017_Create_Swept_Flange_Using_Gauge_Table_Ex-Create_Variable_Pitch_Helix_Example_VBNE.pdf` — Create_Swept_Flange_Using_Gauge_Table_Example_VBNET … Create_Variable_Pitch_Helix_Example_VBNET (35 types)
- `sldworksapi_Examples_018_Create_Width_Mate_Example_CSharp-Display_Configuration_Description_in_Bil.pdf` — Create_Width_Mate_Example_CSharp … Display_Configuration_Description_in_Bill_of_Materials_Example_VBNET (78 types)
- `sldworksapi_Examples_019_Display_Elevation_Symbol_Example_VB-Expand_and_Collapse_FeatureManager_Desig.pdf` — Display_Elevation_Symbol_Example_VB … Expand_and_Collapse_FeatureManager_Design_Tree_Nodes_Example_CSharp (54 types)
- `sldworksapi_Examples_020_Expand_and_Collapse_FeatureManager_Desig-Fire_Notification_After_Adding_a_Mate_Ex.pdf` — Expand_and_Collapse_FeatureManager_Design_Tree_Nodes_Example_VBNET … Fire_Notification_After_Adding_a_Mate_Example_VB (60 types)
- `sldworksapi_Examples_021_Fire_Notification_After_Adding_a_Mate_Ex-Get_All_Sheet_Metal_Feature_Data_Example.pdf` — Fire_Notification_After_Adding_a_Mate_Example_VBNET … Get_All_Sheet_Metal_Feature_Data_Example_VB (73 types)
- `sldworksapi_Examples_022_Get_All_Sheet_Metal_Feature_Data_Example-Get_and_Set_Seed_Components_Example_VB.pdf` — Get_All_Sheet_Metal_Feature_Data_Example_VBNET … Get_and_Set_Seed_Components_Example_VB (64 types)
- `sldworksapi_Examples_023_Get_and_Set_Seed_Components_Example_VBNE-Get_Arcs_in_Sketch_Example_VB.pdf` — Get_and_Set_Seed_Components_Example_VBNET … Get_Arcs_in_Sketch_Example_VB (43 types)
- `sldworksapi_Examples_024_Get_Area_Hatch_Data_Example_CSharp-Get_Centerline_Annotation_Information_Ex.pdf` — Get_Area_Hatch_Data_Example_CSharp … Get_Centerline_Annotation_Information_Example_VB (58 types)
- `sldworksapi_Examples_025_Get_Centerlines_in_Drawing_Example_CShar-Get_Core_Feature_Example_VB.pdf` — Get_Centerlines_in_Drawing_Example_CSharp … Get_Core_Feature_Example_VB (68 types)
- `sldworksapi_Examples_026_Get_Core_Feature_Example_VBNET-Get_DimXpert_Display_Dimensions_and_Feat.pdf` — Get_Core_Feature_Example_VBNET … Get_DimXpert_Display_Dimensions_and_Feature_Example_VBNET (71 types)
- `sldworksapi_Examples_027_Get_DimXpertManager_Info_Example_VB-Get_Faces_Affected_by_Draft_Feature_Exam.pdf` — Get_DimXpertManager_Info_Example_VB … Get_Faces_Affected_by_Draft_Feature_Example_CSharp (74 types)
- `sldworksapi_Examples_028_Get_Faces_Affected_by_Draft_Feature_Exam-Get_Labels_of_Datum_Origin_Example_VBNET.pdf` — Get_Faces_Affected_by_Draft_Feature_Example_VB … Get_Labels_of_Datum_Origin_Example_VBNET (75 types)
- `sldworksapi_Examples_029_Get_Language_and_Localized_Menu_Names_Ex-Get_Mates_and_Mate_Entities_Example_VBNE.pdf` — Get_Language_and_Localized_Menu_Names_Example_VB … Get_Mates_and_Mate_Entities_Example_VBNET (63 types)
- `sldworksapi_Examples_030_Get_Mates_Example_CSharp-Get_Number_of_Lines_Flat-pattern_Drawing.pdf` — Get_Mates_Example_CSharp … Get_Number_of_Lines_Flat-pattern_Drawing_View_Boundary-box_Sketch_Example_VB (71 types)
- `sldworksapi_Examples_031_Get_Number_of_Lines_Flat-pattern_Drawing-Get_Preselected_Object_Example_VB.pdf` — Get_Number_of_Lines_Flat-pattern_Drawing_View_Boundary-box_Sketch_Example_VBNET … Get_Preselected_Object_Example_VB (69 types)
- `sldworksapi_Examples_032_Get_Preselected_Object_Example_VBNET-Get_Sketch_Contours_Example_VB.pdf` — Get_Preselected_Object_Example_VBNET … Get_Sketch_Contours_Example_VB (84 types)
- `sldworksapi_Examples_033_Get_Sketch_Contours_Example_VBNET-Get_Template_Sheet_Metal_Feature_Data_Ex.pdf` — Get_Sketch_Contours_Example_VBNET … Get_Template_Sheet_Metal_Feature_Data_Example_CSharp (70 types)
- `sldworksapi_Examples_034_Get_Template_Sheet_Metal_Feature_Data_Ex-Get_Visible_Drawing_Components_Example_C.pdf` — Get_Template_Sheet_Metal_Feature_Data_Example_VB … Get_Visible_Drawing_Components_Example_CSharp (68 types)
- `sldworksapi_Examples_035_Get_Visible_Drawing_Components_Example_V-Ignore_Feature_Colors_Example_VB.pdf` — Get_Visible_Drawing_Components_Example_VBNET … Ignore_Feature_Colors_Example_VB (57 types)
- `sldworksapi_Examples_036_Import3DInterconnect_Example_CSharp (part 1 of 1).pdf` — Import3DInterconnect_Example_CSharp (part 1 of 1)
- `sldworksapi_Examples_037_Import_DXF_DWG_File_Example_VB-Insert_Boundary_Surface_Feature_Example_.pdf` — Import_DXF_DWG_File_Example_VB … Insert_Boundary_Surface_Feature_Example_VB (76 types)
- `sldworksapi_Examples_038_Insert_Camera_Example_CSharp-Insert_GTol_Example_VB.pdf` — Insert_Camera_Example_CSharp … Insert_GTol_Example_VB (78 types)
- `sldworksapi_Examples_039_Insert_GTol_Example_VBNET-Insert_Sheet_Metal_Gusset_Feature_Exampl.pdf` — Insert_GTol_Example_VBNET … Insert_Sheet_Metal_Gusset_Feature_Example_CSharp (70 types)
- `sldworksapi_Examples_040_Insert_Sheet_Metal_Gusset_Feature_Exampl-Insert_Wrap_Feature_Example_VB.pdf` — Insert_Sheet_Metal_Gusset_Feature_Example_VB … Insert_Wrap_Feature_Example_VB (61 types)
- `sldworksapi_Examples_041_Inspect_Facets_Example_CSharp-Measure_Selected_Entities_Example_VB.pdf` — Inspect_Facets_Example_CSharp … Measure_Selected_Entities_Example_VB (52 types)
- `sldworksapi_Examples_042_Measure_Selected_Entities_Example_VBNET-Move_and_Copy_Body_Using_Vertex_Example_.pdf` — Measure_Selected_Entities_Example_VBNET … Move_and_Copy_Body_Using_Vertex_Example_CSharp (51 types)
- `sldworksapi_Examples_043_Move_and_Copy_Body_using_Vertex_Example_-Put_Assembly_Components_in_Drawing_View_.pdf` — Move_and_Copy_Body_using_Vertex_Example_VB … Put_Assembly_Components_in_Drawing_View_on_Different_Layers_Example_VB (81 types)
- `sldworksapi_Examples_044_QueryInterface_Example_CPlusPlus_COM-Roll_Back_Model_Example_VB.pdf` — QueryInterface_Example_CPlusPlus_COM … Roll_Back_Model_Example_VB (93 types)
- `sldworksapi_Examples_045_Roll_Back_Model_Example_VBNET-Select_Assembly_Components_by_Size_Examp.pdf` — Roll_Back_Model_Example_VBNET … Select_Assembly_Components_by_Size_Example_VBNET (79 types)
- `sldworksapi_Examples_046_Select_Assembly_Components_Using_Advance-Set_Bodies_for_Move_Copy_Example_VBNET.pdf` — Select_Assembly_Components_Using_Advanced_Selection_Criteria_Example_CSharp … Set_Bodies_for_Move_Copy_Example_VBNET (65 types)
- `sldworksapi_Examples_047_Set_Body_for_View_Example_CSharp-Set_View_Scale_Opposite_Parent_View_Scal.pdf` — Set_Body_for_View_Example_CSharp … Set_View_Scale_Opposite_Parent_View_Scale_Example_VB (63 types)
- `sldworksapi_Examples_048_Set_Viewports_Example_CSharp-Switch_Edit_Context_Example_VBNET.pdf` — Set_Viewports_Example_CSharp … Switch_Edit_Context_Example_VBNET (58 types)
- `sldworksapi_Examples_049_Table_of_Wizard_Hole_Types_and_Valid_Pro-Undo_Deleted_Note_and_Fire_Undo_Post-Not.pdf` — Table_of_Wizard_Hole_Types_and_Valid_Properties … Undo_Deleted_Note_and_Fire_Undo_Post-Notify_Event_Example_VB (59 types)
- `sldworksapi_Examples_050_Undo_Deleted_Note_and_Fire_Undo_Post-Not-Zoom_to_Region_Example_VB.pdf` — Undo_Deleted_Note_and_Fire_Undo_Post-Notify_Event_Example_VBNET … Zoom_to_Region_Example_VB (39 types)

---

## pdmworksapivb6

PDMWorks API (VB6 / COM, legacy PDM)


**Reference** (2 files):

- `pdmworksapivb6_Reference_001_PDMWAndOr-PDMWSearchResult.pdf` — PDMWAndOr … PDMWSearchResult (25 types)
- `pdmworksapivb6_Reference_002_PDMWSearchResults-PDMWUsers.pdf` — PDMWSearchResults … PDMWUsers (3 types)

**Examples** (1 files):

- `pdmworksapivb6_Examples_001_PDMWorks_gettingstarted-PDMWorks_references.pdf` — PDMWorks_gettingstarted … PDMWorks_references (4 types)

---

## pdmworksapi

PDMWorks API (.NET, legacy PDM)


**Reference** (4 files):

- `pdmworksapi_Reference_001__namespace_hierarchy-IPDMWConnection.pdf` — _namespace_hierarchy … IPDMWConnection (4 types)
- `pdmworksapi_Reference_002_IPDMWDocument-IPDMWDocuments.pdf` — IPDMWDocument … IPDMWDocuments (4 types)
- `pdmworksapi_Reference_003_IPDMWGroup-IPDMWProperty.pdf` — IPDMWGroup … IPDMWProperty (8 types)
- `pdmworksapi_Reference_004_IPDMWSearchCriteria-PDMWRevisionOptionType.pdf` — IPDMWSearchCriteria … PDMWRevisionOptionType (14 types)

**Examples** (1 files):

- `pdmworksapi_Examples_001_Create_Search_Criteria_and_Search_Vault_-Welcome-pdmworksapi.pdf` — Create_Search_Criteria_and_Search_Vault_Example_VB … Welcome-pdmworksapi (10 types)

---

## pdmprowebapihelp

SOLIDWORKS PDM Professional Web API (REST endpoints)


**Reference** (3 files):

- `pdmprowebapihelp_Reference_001_g-081b00cf-917a-401f-9276-9f29de5178b5-r-api-{vaultName}-files-{fileId}-bominfo.pdf` — g-081b00cf-917a-401f-9276-9f29de5178b5 … r-api-{vaultName}-files-{fileId}-bominfo-folderId={folderId} (43 types)
- `pdmprowebapihelp_Reference_002_r-api-{vaultName}-files-{fileId}-datacar-r-api-{vaultName}-files-{fileId}-{versio.pdf` — r-api-{vaultName}-files-{fileId}-datacard … r-api-{vaultName}-files-{fileId}-{version}-{configId}-{folderId}-whereused-anyVersion={anyVersion} (42 types)
- `pdmprowebapihelp_Reference_003_r-api-{vaultName}-files-{fileId}-{versio-r-api-{vaultName}-workflows-icons.pdf` — r-api-{vaultName}-files-{fileId}-{version}-{folderId} … r-api-{vaultName}-workflows-icons (47 types)

**Examples** (1 files):

- `pdmprowebapihelp_Examples_001_GettingStarted-Welcome.pdf` — GettingStarted … Welcome (8 types)

---

## swcommands

SOLIDWORKS Command IDs (swCommands_e, swMouse_e)


**Reference** (1 files):

- `swcommands_Reference_001__namespace_hierarchy-swMouse_e.pdf` — _namespace_hierarchy … swMouse_e (4 types)

**Examples** (1 files):

- `swcommands_Examples_001_SolidWorks.Interop.swcommands.pdf` — SolidWorks.Interop.swcommands

---

## swconst

SOLIDWORKS Enumerations (swconst) — every sw*_e enum used across the API


**Reference** (17 files):

- `swconst_Reference_001__namespace_hierarchy-swApplicationType_e.pdf` — _namespace_hierarchy … swApplicationType_e (44 types)
- `swconst_Reference_002_swAppNotify_e-swBOMConfigurationWhatToShow_e.pdf` — swAppNotify_e … swBOMConfigurationWhatToShow_e (60 types)
- `swconst_Reference_003_swBOMControlMissingRowDisplay_e-swComponentSolvingOption_e.pdf` — swBOMControlMissingRowDisplay_e … swComponentSolvingOption_e (65 types)
- `swconst_Reference_004_swComponentSuppressionState_e-swDatumGbLeaderStyle_e.pdf` — swComponentSuppressionState_e … swDatumGbLeaderStyle_e (64 types)
- `swconst_Reference_005_swDatumTagTextParts_e-swDistanceMateArcConditions_e.pdf` — swDatumTagTextParts_e … swDistanceMateArcConditions_e (65 types)
- `swconst_Reference_006_swDocTemplateTypes_e-swFeatureEditStatus_e.pdf` — swDocTemplateTypes_e … swFeatureEditStatus_e (64 types)
- `swconst_Reference_007_swFeatureError_e-swHlrQuality_e.pdf` — swFeatureError_e … swHlrQuality_e (61 types)
- `swconst_Reference_008_swHoleElementOrientation_e-swLinkDimensionError_e.pdf` — swHoleElementOrientation_e … swLinkDimensionError_e (65 types)
- `swconst_Reference_009_swLinkString-swMoveCopyOptions_e.pdf` — swLinkString … swMoveCopyOptions_e (64 types)
- `swconst_Reference_010_swMoveFaceType_e-swPMIUnit_e.pdf` — swMoveFaceType_e … swPMIUnit_e (63 types)
- `swconst_Reference_011_swPointInferenceBrokerOption_e-swRemoveCommandGroupErrors.pdf` — swPointInferenceBrokerOption_e … swRemoveCommandGroupErrors (63 types)
- `swconst_Reference_012_swRenamedDocumentFinalAction_e-swSetHelixRegionParameterStatus_e.pdf` — swRenamedDocumentFinalAction_e … swSetHelixRegionParameterStatus_e (59 types)
- `swconst_Reference_013_swSetRouteFixedLengthError_e-swSolidworksWeldmentEndCondOptions_e.pdf` — swSetRouteFixedLengthError_e … swSolidworksWeldmentEndCondOptions_e (65 types)
- `swconst_Reference_014_swSpeedpakUpdate_e-swTangentMagnitudeDirection_e.pdf` — swSpeedpakUpdate_e … swTangentMagnitudeDirection_e (66 types)
- `swconst_Reference_015_swTaperedTapCustomSizing_e-swUserPreferenceDoubleValue_e.pdf` — swTaperedTapCustomSizing_e … swUserPreferenceDoubleValue_e (58 types)
- `swconst_Reference_016_swUserPreferenceIntegerValue_e-swWeldBeadType_e.pdf` — swUserPreferenceIntegerValue_e … swWeldBeadType_e (24 types)
- `swconst_Reference_017_swWeldmentTrimExtendOptionType_e-swZoomLevelOnOpenType_e.pdf` — swWeldmentTrimExtendOptionType_e … swZoomLevelOnOpenType_e (23 types)

**Examples** (8 files):

- `swconst_Examples_001_DP_Annotations-DP_Detailing.pdf` — DP_Annotations … DP_Detailing (16 types)
- `swconst_Examples_002_DP_Dimensions-DP_DimXpert-ChainDimension.pdf` — DP_Dimensions … DP_DimXpert-ChainDimension (13 types)
- `swconst_Examples_003_DP_DimXpert-ChamferControls-DP_PlaneDisplay.pdf` — DP_DimXpert-ChamferControls … DP_PlaneDisplay (17 types)
- `swconst_Examples_004_DP_SheetMetal-DP_ViewLabels-Other.pdf` — DP_SheetMetal … DP_ViewLabels-Other (17 types)
- `swconst_Examples_005_DP_ViewLabels-Section-FileSaveAsIGESOptions.pdf` — DP_ViewLabels-Section … FileSaveAsIGESOptions (28 types)
- `swconst_Examples_006_FileSaveAsParasolidOptions-SO_Drawings.pdf` — FileSaveAsParasolidOptions … SO_Drawings (20 types)
- `swconst_Examples_007_SO_Drawings-AreaHatchFill-SO_Messages.pdf` — SO_Drawings-AreaHatchFill … SO_Messages (13 types)
- `swconst_Examples_008_SO_Miscellaneous-ViewUserInterface.pdf` — SO_Miscellaneous … ViewUserInterface (22 types)

---

## swdimxpertapi

SOLIDWORKS DimXpert API (.NET)


**Reference** (8 files):

- `swdimxpertapi_Reference_001__namespace_hierarchy-IDimXpertChamferDimTol.pdf` — _namespace_hierarchy … IDimXpertChamferDimTol (8 types)
- `swdimxpertapi_Reference_002_IDimXpertChamferFeature-IDimXpertCompositeSurfaceProfileToleranc.pdf` — IDimXpertChamferFeature … IDimXpertCompositeSurfaceProfileTolerance (5 types)
- `swdimxpertapi_Reference_003_IDimXpertCompoundClosedSlot3DFeature-IDimXpertCounterBoreDimTol.pdf` — IDimXpertCompoundClosedSlot3DFeature … IDimXpertCounterBoreDimTol (7 types)
- `swdimxpertapi_Reference_004_IDimXpertCounterSinkAngleDimTol-IDimXpertDistanceBetweenDimTol.pdf` — IDimXpertCounterSinkAngleDimTol … IDimXpertDistanceBetweenDimTol (9 types)
- `swdimxpertapi_Reference_005_IDimXpertExtrudeFeature-IDimXpertIntersectPlaneFeature.pdf` — IDimXpertExtrudeFeature … IDimXpertIntersectPlaneFeature (8 types)
- `swdimxpertapi_Reference_006_IDimXpertIntersectPointFeature-IDimXpertPatternFeature.pdf` — IDimXpertIntersectPointFeature … IDimXpertPatternFeature (5 types)
- `swdimxpertapi_Reference_007_IDimXpertPlaneFeature-IDimXpertTangencyTolerance.pdf` — IDimXpertPlaneFeature … IDimXpertTangencyTolerance (9 types)
- `swdimxpertapi_Reference_008_IDimXpertTolerance-swDimXpertStraightnessZoneType_e.pdf` — IDimXpertTolerance … swDimXpertStraightnessZoneType_e (25 types)

**Examples** (7 files):

- `swdimxpertapi_Examples_001_Auto_Dimension_Scheme_Example_CSharp-Get_and_Set_Location_Dimension_Example_V.pdf` — Auto_Dimension_Scheme_Example_CSharp … Get_and_Set_Location_Dimension_Example_VB (14 types)
- `swdimxpertapi_Examples_002_Get_and_Set_Location_Dimension_Example_V-Get_DimXpert_Datum_Example_VB.pdf` — Get_and_Set_Location_Dimension_Example_VBNET … Get_DimXpert_Datum_Example_VB (14 types)
- `swdimxpertapi_Examples_003_Get_DimXpert_Datum_Example_VBNET-Get_DimXpert_Sphere_Feature_Example_VBNE.pdf` — Get_DimXpert_Datum_Example_VBNET … Get_DimXpert_Sphere_Feature_Example_VBNET (15 types)
- `swdimxpertapi_Examples_004_Get_DimXpert_Tolerance1_Example_VB-Get_DimXpert_Tolerance2_Example_VBNET.pdf` — Get_DimXpert_Tolerance1_Example_VB … Get_DimXpert_Tolerance2_Example_VBNET (4 types)
- `swdimxpertapi_Examples_005_Get_DimXpert_Tolerance3_Example_VB-Get_DimXpert_Tolerance4_Example_VBNET.pdf` — Get_DimXpert_Tolerance3_Example_VB … Get_DimXpert_Tolerance4_Example_VBNET (4 types)
- `swdimxpertapi_Examples_006_Get_DimXpert_Tolerance5_Example_VB-Get_DimXpert_Tolerance6_Example_VBNET.pdf` — Get_DimXpert_Tolerance5_Example_VB … Get_DimXpert_Tolerance6_Example_VBNET (4 types)
- `swdimxpertapi_Examples_007_Get_DimXpert_Tolerance_Example_VB-SolidWorks.Interop.swdimxpert.pdf` — Get_DimXpert_Tolerance_Example_VB … SolidWorks.Interop.swdimxpert (5 types)

---

## swdimxpertapivb6

SOLIDWORKS DimXpert API (VB6 / COM)


**Reference** (3 files):

- `swdimxpertapivb6_Reference_001_DimXpertAngleBetweenCircularDimTol-DimXpertDimensionOption.pdf` — DimXpertAngleBetweenCircularDimTol … DimXpertDimensionOption (26 types)
- `swdimxpertapivb6_Reference_002_DimXpertDimensionTolerance-swDimXpertDimensionPositionOption_e.pdf` — DimXpertDimensionTolerance … swDimXpertDimensionPositionOption_e (36 types)
- `swdimxpertapivb6_Reference_003_swDimXpertDimensionToleranceType_e-swDimXpertStraightnessZoneType_e.pdf` — swDimXpertDimensionToleranceType_e … swDimXpertStraightnessZoneType_e (12 types)

**Examples** (1 files):

- `swdimxpertapivb6_Examples_001_SwDimXpert_gettingstarted-SwDimXpert_references.pdf` — SwDimXpert_gettingstarted … SwDimXpert_references (4 types)

---

## swdocmgrapi

SOLIDWORKS Document Manager API (.NET) — read files without launching SOLIDWORKS


**Reference** (19 files):

- `swdocmgrapi_Reference_001__namespace_hierarchy-ISwDMComponent2.pdf` — _namespace_hierarchy … ISwDMComponent2 (12 types)
- `swdocmgrapi_Reference_002_ISwDMComponent3-ISwDMConfiguration.pdf` — ISwDMComponent3 … ISwDMConfiguration (8 types)
- `swdocmgrapi_Reference_003_ISwDMConfiguration10-ISwDMConfiguration18.pdf` — ISwDMConfiguration10 … ISwDMConfiguration18 (9 types)
- `swdocmgrapi_Reference_004_ISwDMConfiguration2-ISwDMConfiguration9.pdf` — ISwDMConfiguration2 … ISwDMConfiguration9 (8 types)
- `swdocmgrapi_Reference_005_ISwDMConfigurationMgr-ISwDMDimXpertAngularityGeoTol.pdf` — ISwDMConfigurationMgr … ISwDMDimXpertAngularityGeoTol (8 types)
- `swdocmgrapi_Reference_006_ISwDMDimXpertAnnotation-ISwDMDimXpertCompositeDistanceBetweenDim.pdf` — ISwDMDimXpertAnnotation … ISwDMDimXpertCompositeDistanceBetweenDimTol (8 types)
- `swdocmgrapi_Reference_007_ISwDMDimXpertCompositeLineProfileGeoTol-ISwDMDimXpertCompoundHoleFeature.pdf` — ISwDMDimXpertCompositeLineProfileGeoTol … ISwDMDimXpertCompoundHoleFeature (5 types)
- `swdocmgrapi_Reference_008_ISwDMDimXpertCompoundNotchFeature-ISwDMDimXpertDatum.pdf` — ISwDMDimXpertCompoundNotchFeature … ISwDMDimXpertDatum (11 types)
- `swdocmgrapi_Reference_009_ISwDMDimXpertDepthDimTol-ISwDMDimXpertFlatnessGeoTol.pdf` — ISwDMDimXpertDepthDimTol … ISwDMDimXpertFlatnessGeoTol (8 types)
- `swdocmgrapi_Reference_010_ISwDMDimXpertGeometricTolerance-ISwDMDimXpertOrientationGeoTol.pdf` — ISwDMDimXpertGeometricTolerance … ISwDMDimXpertOrientationGeoTol (8 types)
- `swdocmgrapi_Reference_011_ISwDMDimXpertParallelismGeoTol-ISwDMDimXpertSurfaceProfileGeoTol.pdf` — ISwDMDimXpertParallelismGeoTol … ISwDMDimXpertSurfaceProfileGeoTol (12 types)
- `swdocmgrapi_Reference_012_ISwDMDimXpertSymmetryGeoTol-ISwDMDocument.pdf` — ISwDMDimXpertSymmetryGeoTol … ISwDMDocument (5 types)
- `swdocmgrapi_Reference_013_ISwDMDocument10-ISwDMDocument17.pdf` — ISwDMDocument10 … ISwDMDocument17 (8 types)
- `swdocmgrapi_Reference_014_ISwDMDocument18-ISwDMDocument26.pdf` — ISwDMDocument18 … ISwDMDocument26 (9 types)
- `swdocmgrapi_Reference_015_ISwDMDocument27-ISwDMDocument6.pdf` — ISwDMDocument27 … ISwDMDocument6 (9 types)
- `swdocmgrapi_Reference_016_ISwDMDocument7-ISwDMSheet4.pdf` — ISwDMDocument7 … ISwDMSheet4 (9 types)
- `swdocmgrapi_Reference_017_ISwDMTable-ISwDMTable6.pdf` — ISwDMTable … ISwDMTable6 (6 types)
- `swdocmgrapi_Reference_018_ISwDMView-swDmShowChildComponentsInBOMResult.pdf` — ISwDMView … swDmShowChildComponentsInBOMResult (50 types)
- `swdocmgrapi_Reference_019_swDmTableCellHorzAlignType-swSheetPropertiesResult.pdf` — swDmTableCellHorzAlignType … swSheetPropertiesResult (16 types)

**Examples** (4 files):

- `swdocmgrapi_Examples_001_Code_Example_CSharp.pdf` — Code_Example_CSharp
- `swdocmgrapi_Examples_002_Code_Example_VBNET-Get_Configuration-Specific_Custom_Proper.pdf` — Code_Example_VBNET … Get_Configuration-Specific_Custom_Properties_Example_CSharp (14 types)
- `swdocmgrapi_Examples_003_Get_Configuration-Specific_Custom_Proper-Get_Persistent_IDs_Example_VBNET.pdf` — Get_Configuration-Specific_Custom_Properties_Example_VBNET … Get_Persistent_IDs_Example_VBNET (60 types)
- `swdocmgrapi_Examples_004_Get_PNG_Preview_Bitmap_and_Stream_for_Co-Write_Parasolid_Partition_Stream_to_File.pdf` — Get_PNG_Preview_Bitmap_and_Stream_for_Configuration_Example_CSharp … Write_Parasolid_Partition_Stream_to_File_Example_VBNET (26 types)

---

## swdocmgrapivb6

SOLIDWORKS Document Manager API (VB6 / COM)


**Reference** (8 files):

- `swdocmgrapivb6_Reference_001_ISwDMApplication3-ISwDMDimXpertCompoundHoleFeature.pdf` — ISwDMApplication3 … ISwDMDimXpertCompoundHoleFeature (35 types)
- `swdocmgrapivb6_Reference_002_ISwDMDimXpertCompoundNotchFeature-ISwDMDimXpertPlaneFeature.pdf` — ISwDMDimXpertCompoundNotchFeature … ISwDMDimXpertPlaneFeature (33 types)
- `swdocmgrapivb6_Reference_003_ISwDMDimXpertPositionGeoTol-SwDMApplication3.pdf` — ISwDMDimXpertPositionGeoTol … SwDMApplication3 (46 types)
- `swdocmgrapivb6_Reference_004_SwDMApplication4-SwDMCutListItem.pdf` — SwDMApplication4 … SwDMCutListItem (45 types)
- `swdocmgrapivb6_Reference_005_SwDMCutListItem2-SwDMDimXpertCylinderFeature.pdf` — SwDMCutListItem2 … SwDMDimXpertCylinderFeature (34 types)
- `swdocmgrapivb6_Reference_006_SwDMDimXpertCylindricityGeoTol-SwDMDimXpertWidthDimTol.pdf` — SwDMDimXpertCylindricityGeoTol … SwDMDimXpertWidthDimTol (43 types)
- `swdocmgrapivb6_Reference_007_SwDMDocument-swDmExcludeFromBOMResult.pdf` — SwDMDocument … swDmExcludeFromBOMResult (40 types)
- `swdocmgrapivb6_Reference_008_SwDMExternalReferenceOption-swSheetPropertiesResult.pdf` — SwDMExternalReferenceOption … swSheetPropertiesResult (42 types)

**Examples** (1 files):

- `swdocmgrapivb6_Examples_001_SwDocumentMgr_gettingstarted-SwDocumentMgr_references.pdf` — SwDocumentMgr_gettingstarted … SwDocumentMgr_references (4 types)

---

## swhtmlcontrolapi

SOLIDWORKS HTML Control API (.NET)


**Reference** (1 files):

- `swhtmlcontrolapi_Reference_001__namespace_hierarchy-SolidWorks.Interop.swhtmlcontrol.pdf` — _namespace_hierarchy … SolidWorks.Interop.swhtmlcontrol (3 types)

**Examples** (1 files):

- `swhtmlcontrolapi_Examples_001_Flash_an_Add-in_s_Toolbar_Button_Example-SolidWorks.Interop.swhtmlcontrol.pdf` — Flash_an_Add-in_s_Toolbar_Button_Example_VB … SolidWorks.Interop.swhtmlcontrol (3 types)

---

## swhtmlcontrolapivb6

SOLIDWORKS HTML Control API (VB6 / COM)


**Reference** (1 files):

- `swhtmlcontrolapivb6_Reference_001_SwHtmlInterface.pdf` — SwHtmlInterface

**Examples** (1 files):

- `swhtmlcontrolapivb6_Examples_001_SWHTMLCONTROLLib_gettingstarted-SWHTMLCONTROLLib_references.pdf` — SWHTMLCONTROLLib_gettingstarted … SWHTMLCONTROLLib_references (4 types)

---

## swinspectionapi

SOLIDWORKS Inspection Add-in API (.NET) — DIRECTLY RELEVANT to this project (Binspection)


**Reference** (7 files):

- `swinspectionapi_Reference_001__namespace_hierarchy-CharacteristicsData.pdf` — _namespace_hierarchy … CharacteristicsData (4 types)
- `swinspectionapi_Reference_002_CharacteristicsDataClass-IBalloonSettings.pdf` — CharacteristicsDataClass … IBalloonSettings (2 types)
- `swinspectionapi_Reference_003_ICharacteristicsData-IInspectionAddinMgr.pdf` — ICharacteristicsData … IInspectionAddinMgr (2 types)
- `swinspectionapi_Reference_004_IInspectionProject-IInspectionProjectData.pdf` — IInspectionProject … IInspectionProjectData (2 types)
- `swinspectionapi_Reference_005_InspectionAddinMgr-InspectionProject.pdf` — InspectionAddinMgr … InspectionProject (3 types)
- `swinspectionapi_Reference_006_InspectionProjectClass-InspectionProjectData.pdf` — InspectionProjectClass … InspectionProjectData (2 types)
- `swinspectionapi_Reference_007_InspectionProjectDataClass-swiVendorsInspectionType_e.pdf` — InspectionProjectDataClass … swiVendorsInspectionType_e (14 types)

**Examples** (1 files):

- `swinspectionapi_Examples_001_Getting Started-Welcome.pdf` — Getting Started … Welcome (4 types)

---

## swinspectionapivb6

SOLIDWORKS Inspection Add-in API (VB6 / COM) — DIRECTLY RELEVANT to this project (Binspection)


**Reference** (1 files):

- `swinspectionapivb6_Reference_001_BalloonSettings-swiVendorsInspectionType_e.pdf` — BalloonSettings … swiVendorsInspectionType_e (17 types)

**Examples** (1 files):

- `swinspectionapivb6_Examples_001_Getting Started-SWInspectionAddin_references.pdf` — Getting Started … SWInspectionAddin_references (7 types)

---
