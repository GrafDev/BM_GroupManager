using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using BM_GroupManager.Models;
using BM_GroupManager.Services;

namespace BM_GroupManager.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class UnwrapGroupCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document   doc   = uidoc.Document;

            // Проверяем наличие параметра
            if (!SharedParameterService.IsParameterBound(doc))
            {
                TaskDialog dlg = new TaskDialog("Параметр не найден");
                dlg.MainContent =
                    "Параметр 'Original_Group_GUID' не найден в этом проекте.\n\n" +
                    "Сначала выполните команду «Настройка проекта».";
                dlg.Show();
                return Result.Failed;
            }

            // ── 1. Получаем выбранную группу ────────────────────────────────────
            Group selectedGroup = TryGetSelectedGroup(uidoc, doc);

            if (selectedGroup == null)
            {
                // Предлагаем выбрать интерактивно
                try
                {
                    Reference pickedRef = uidoc.Selection.PickObject(
                        ObjectType.Element,
                        new GroupSelectionFilter(),
                        "Выберите группу для разгруппировки");

                    selectedGroup = doc.GetElement(pickedRef) as Group;
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }
            }

            if (selectedGroup == null)
            {
                message = "Выбранный элемент не является группой.";
                return Result.Failed;
            }

            // ── 2. Разгруппировываем через сервис (Весь тип) ─────────────────────────────
            string errMessage;
            Result result = GroupService.UnwrapAll(doc, selectedGroup.GroupType, out errMessage);
            
            if (result != Result.Succeeded && !string.IsNullOrEmpty(errMessage))
                message = errMessage;

            return result;
        }

        // ── Вспомогательный метод ────────────────────────────────────────────────

        private static Group TryGetSelectedGroup(UIDocument uidoc, Document doc)
        {
            ICollection<ElementId> selIds = uidoc.Selection.GetElementIds();
            foreach (ElementId id in selIds)
            {
                if (doc.GetElement(id) is Group grp)
                    return grp;
            }
            return null;
        }
    }

    /// <summary>Фильтр выбора: только экземпляры групп.</summary>
    public class GroupSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)    => elem is Group;
        public bool AllowReference(Reference r, XYZ p) => false;
    }
}
