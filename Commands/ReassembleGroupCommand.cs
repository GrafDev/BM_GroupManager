using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BM_GroupManager.Models;
using BM_GroupManager.Services;

namespace BM_GroupManager.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ReassembleGroupCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            // Резервная точка входа (если вызывается напрямую без GUI)
            message = "Используйте кнопку «Собрать» в панели Smart Group Manager.";
            return Result.Failed;
        }

        // ── Основная логика сборки ───────────────────────────────────────────────

        /// <summary>
        /// Собирает группу по GUID. Вызывается из ReassembleEventHandler и тестов.
        /// </summary>
        public static Result Reassemble(Document doc, Guid typeGuid, ref string message)
        {
            string errMessage;
            Result result = GroupService.ReassembleAll(doc, typeGuid, out errMessage);
            
            if (result != Result.Succeeded && !string.IsNullOrEmpty(errMessage))
                message = errMessage;

            return result;
        }

        // ── Вспомогательные методы ───────────────────────────────────────────────

        /// <summary>
        /// Возвращает имя, не конфликтующее с уже существующими типами групп.
        /// </summary>
        private static string GetUniqueName(Document doc, string baseName, ElementId excludeId)
        {
            bool NameTaken(string candidate) =>
                new FilteredElementCollector(doc)
                    .OfClass(typeof(GroupType))
                    .Any(gt => gt.Name == candidate && gt.Id != excludeId);

            if (!NameTaken(baseName))
                return baseName;

            int suffix = 1;
            string result;
            do { result = $"{baseName}_{suffix++}"; }
            while (NameTaken(result));

            return result;
        }
    }
}
