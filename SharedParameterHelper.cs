using System;
using System.IO;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;

namespace RevitToSupabasePlugin
{
    public static class SharedParameterHelper
    {
        public static void AddBrickSharedParameters(Document familyDoc, Autodesk.Revit.ApplicationServices.Application app, double lFt, double wFt, double hFt, Extrusion solid)
        {
            string originalSpFile = app.SharedParametersFilename;
            string spFileName = Path.Combine(Path.GetTempPath(), "BrickSharedParameters.txt");
            
            try
            {
                if (File.Exists(spFileName)) File.Delete(spFileName);
                
                using (StreamWriter sw = File.CreateText(spFileName))
                {
                    sw.WriteLine("# This is a Revit shared parameter file.");
                    sw.WriteLine("*META	VERSION	MINVERSION");
                    sw.WriteLine("META	2	1");
                    sw.WriteLine("*GROUP	ID	NAME");
                    sw.WriteLine("GROUP	1	BrickDimensions");
                    sw.WriteLine("*PARAM	GUID	NAME	DATATYPE	DATACATEGORY	GROUP	VISIBLE	DESCRIPTION	USERMODIFIABLE	HIDEWHENNOVALUE");
                    sw.WriteLine($"PARAM	{Guid.NewGuid()}	Largo	LENGTH		1	1		1	0");
                    sw.WriteLine($"PARAM	{Guid.NewGuid()}	Alto	LENGTH		1	1		1	0");
                    sw.WriteLine($"PARAM	{Guid.NewGuid()}	Profundidad	LENGTH		1	1		1	0");
                }

                app.SharedParametersFilename = spFileName;
                DefinitionFile defFile = app.OpenSharedParameterFile();
                if (defFile != null)
                {
                    DefinitionGroup group = defFile.Groups.get_Item("BrickDimensions");
                    if (group != null)
                    {
                        ExternalDefinition defLargo = group.Definitions.get_Item("Largo") as ExternalDefinition;
                        ExternalDefinition defAlto = group.Definitions.get_Item("Alto") as ExternalDefinition;
                        ExternalDefinition defProfundidad = group.Definitions.get_Item("Profundidad") as ExternalDefinition;

                        if (defLargo != null)
                        {
                            FamilyParameter paramLargo = familyDoc.FamilyManager.AddParameter(defLargo, GroupTypeId.Geometry, true);
                            familyDoc.FamilyManager.Set(paramLargo, lFt);
                            
                            // Asociar Largo al Extrusion End
                            if (solid != null)
                            {
                                Parameter pEnd = solid.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM);
                                if (pEnd != null) familyDoc.FamilyManager.AssociateElementParameterToFamilyParameter(pEnd, paramLargo);
                            }
                        }
                        if (defAlto != null)
                        {
                            FamilyParameter paramAlto = familyDoc.FamilyManager.AddParameter(defAlto, GroupTypeId.Geometry, true);
                            familyDoc.FamilyManager.Set(paramAlto, hFt);
                        }
                        if (defProfundidad != null)
                        {
                            FamilyParameter paramProfundidad = familyDoc.FamilyManager.AddParameter(defProfundidad, GroupTypeId.Geometry, true);
                            familyDoc.FamilyManager.Set(paramProfundidad, wFt);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Ignorar error
            }
            finally
            {
                try { app.SharedParametersFilename = originalSpFile; } catch { }
            }
        }
    }
}
