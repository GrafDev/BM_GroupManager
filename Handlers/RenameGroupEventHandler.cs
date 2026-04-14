using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BM_GroupManager.Models;
using BM_GroupManager.Services;

namespace BM_GroupManager.Handlers
{
    /// <summary>
    /// Обработчик внешнего события — переименование группы в реестре из WPF-панели.
    /// </summary>
    public class RenameGroupEventHandler : IExternalEventHandler
    {
        public Guid   TargetGuid { get; set; }
        public string NewName    { get; set; }

        public void Execute(UIApplication app)
        {
            if (TargetGuid == Guid.Empty || string.IsNullOrWhiteSpace(NewName)) return;

            Document doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;

            App.CurrentUIApp = app;

            GroupRecord record = GroupRegistryService.GetAll(doc)
                .Find(r => r.TypeGuid == TargetGuid);

            if (record == null) return;

            record.TypeName = NewName;

            using (Transaction tx = new Transaction(doc, "SGM: Переименовать группу"))
            {
                tx.Start();
                GroupRegistryService.Update(doc, record);
                tx.Commit();
            }

            App.RefreshManagerPane();
        }

        public string GetName() => "SGM: Rename Group";
    }
}
