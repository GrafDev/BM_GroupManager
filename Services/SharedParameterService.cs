using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BM_GroupManager.Services
{
    /// <summary>
    /// Создаёт и привязывает общий параметр Original_Group_GUID ко всем категориям модели.
    /// </summary>
    public static class SharedParameterService
    {
        public const string ParameterNameType = "BM_GroupManager_Group_ID";
        public const string ParameterNameInstance = "BM_GroupManager_Instance_ID";
        
        // Для обратной совместимости
        public static string ParameterName => ParameterNameType;

        private const string GroupName = "BM_GroupManager";

        /// <summary>
        /// Создаёт параметр в проекте, если он ещё не существует.
        /// </summary>
        public static Result SetupSharedParameter(Document doc)
        {
            Autodesk.Revit.ApplicationServices.Application app = doc.Application;

            string tempFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BM_GroupManager",
                "SharedParams.txt");

            Directory.CreateDirectory(Path.GetDirectoryName(tempFile));
            if (!File.Exists(tempFile))
                File.WriteAllText(tempFile, "# BM Group Manager Shared Parameters v1.1.51\r\n");

            string previousFile = app.SharedParametersFilename;
            app.SharedParametersFilename = tempFile;

            try
            {
                DefinitionFile defFile = app.OpenSharedParameterFile();
                if (defFile == null) return Result.Failed;

                DefinitionGroup defGroup = defFile.Groups.get_Item(GroupName)
                                        ?? defFile.Groups.Create(GroupName);

                CategorySet categorySet = app.Create.NewCategorySet();
                
                // Добавляем все моделируемые категории
                foreach (Category cat in doc.Settings.Categories)
                {
                    if (cat.CategoryType == CategoryType.Model && cat.AllowsBoundParameters && cat.Id.IntegerValue != (int)BuiltInCategory.OST_Lines)
                        categorySet.Insert(cat);
                }

                using (Transaction tx = new Transaction(doc, "Настройка параметров SGM v1.1.51"))
                {
                    tx.Start();

                    string[] pNames = { ParameterNameType, ParameterNameInstance };
                    foreach (string pName in pNames)
                    {
                        ExternalDefinition paramDef = defGroup.Definitions.get_Item(pName) as ExternalDefinition;
                        if (paramDef == null)
                        {
                            var options = new ExternalDefinitionCreationOptions(pName, SpecTypeId.String.Text)
                            {
                                Visible = true,
                                UserModifiable = false
                            };
                            paramDef = defGroup.Definitions.Create(options) as ExternalDefinition;
                        }

                        if (paramDef != null)
                        {
                            InstanceBinding binding = app.Create.NewInstanceBinding(categorySet);
#pragma warning disable CS0618
                            doc.ParameterBindings.Insert(paramDef, binding, BuiltInParameterGroup.PG_IDENTITY_DATA);
#pragma warning restore CS0618
                        }
                    }

                    tx.Commit();
                }

                return Result.Succeeded;
            }
            finally
            {
                app.SharedParametersFilename = previousFile;
            }
        }

        public static bool IsParameterBound(Document doc)
        {
            return IsBound(doc, ParameterNameType);
        }

        private static bool IsBound(Document doc, string name)
        {
            DefinitionBindingMapIterator it = doc.ParameterBindings.ForwardIterator();
            while (it.MoveNext())
            {
                if (it.Key.Name == name) return true;
            }
            return false;
        }
    }
}
