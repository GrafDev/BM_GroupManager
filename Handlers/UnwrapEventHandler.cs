using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BM_GroupManager.Services;

namespace BM_GroupManager.Handlers
{
    /// <summary>
    /// Обработчик внешнего события — разборка группы из WPF-панели.
    /// </summary>
    public class UnwrapEventHandler : IExternalEventHandler
    {
        public ElementId TargetTypeId { get; set; }

        public void Execute(UIApplication app)
        {
            if (TargetTypeId == null || TargetTypeId == ElementId.InvalidElementId) return;

            Document doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;

            // Обновляем контекст
            App.CurrentUIApp = app;

            GroupType gType = doc.GetElement(TargetTypeId) as GroupType;
            if (gType == null)
            {
                TaskDialog.Show("Ошибка", "Тип группы не найден.");
                return;
            }

            string message = string.Empty;
            Result result = GroupService.UnwrapAll(doc, gType, out message);

            if (result != Result.Succeeded && !string.IsNullOrEmpty(message))
            {
                TaskDialog.Show("Ошибка разборки", message);
            }
        }

        public string GetName() => "SGM: Unwrap Group";
    }
}
