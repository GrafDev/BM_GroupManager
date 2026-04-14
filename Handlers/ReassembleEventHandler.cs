using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BM_GroupManager.Services;
using BM_GroupManager.Commands;

namespace BM_GroupManager.Handlers
{
    /// <summary>
    /// Обработчик внешнего события — сборка группы из WPF-панели.
    /// ExternalEvent необходим, т.к. транзакции нельзя открывать напрямую из WPF-обработчиков.
    /// </summary>
    public class ReassembleEventHandler : IExternalEventHandler
    {
        public Guid TargetGuid { get; set; }

        public void Execute(UIApplication app)
        {
            if (TargetGuid == Guid.Empty) return;

            Autodesk.Revit.DB.Document doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;

            // Обновляем актуальный UIApplication в App
            App.CurrentUIApp = app;

            string message = string.Empty;
            Result result = GroupService.ReassembleAll(doc, TargetGuid, out message);

            if (result != Result.Succeeded && !string.IsNullOrEmpty(message))
                Autodesk.Revit.UI.TaskDialog.Show("Ошибка сборки", message);

            // RefreshManagerPane уже вызван внутри Reassemble при успехе
        }

        public string GetName() => "SGM: Reassemble Group";
    }
}
