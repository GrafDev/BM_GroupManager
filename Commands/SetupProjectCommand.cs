using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BM_GroupManager.Services;

namespace BM_GroupManager.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SetupProjectCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            // Уже настроен?
            if (SharedParameterService.IsParameterBound(doc))
            {
                TaskDialog.Show("Настройка проекта",
                    "✓ Параметр 'Original_Group_GUID' уже присутствует в этом проекте.\n\n" +
                    "Повторная настройка не требуется.");
                return Result.Succeeded;
            }

            Result result = SharedParameterService.SetupSharedParameter(doc);

            if (result == Result.Succeeded)
            {
                TaskDialog.Show("Настройка проекта",
                    "✓ Параметр 'Original_Group_GUID' успешно создан и привязан\n" +
                    "ко всем категориям модели.\n\n" +
                    "Проект готов к работе с Smart Group Manager.");
            }
            else
            {
                TaskDialog.Show("Ошибка настройки",
                    "Не удалось создать параметр.\n\n" +
                    "Убедитесь, что активный документ является проектом (не семейством),\n" +
                    "и повторите попытку.");
            }

            return result;
        }
    }
}
