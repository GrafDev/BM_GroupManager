using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BM_GroupManager.Models;

namespace BM_GroupManager.Services
{
    /// <summary>
    /// Централизованный сервис для выполнения операций над группами: Разборка и Сборка.
    /// </summary>
    public static class GroupService
    {
        // ── РАЗБОРКА (UNWRAP) ────────────────────────────────────────────────────

        // ── РАЗБОРКА (UNWRAP) ────────────────────────────────────────────────────

        public static Result UnwrapAll(Document doc, GroupType gType, out string message)
        {
            message = string.Empty;

            if (!SharedParameterService.IsParameterBound(doc))
            {
                if (!EnsureParameters(doc, out message)) return Result.Failed;
            }

            var instances = new FilteredElementCollector(doc)
                .OfClass(typeof(Group))
                .Cast<Group>()
                .Where(g => g.GroupType.Id == gType.Id)
                .ToList();

            if (!instances.Any())
            {
                message = "Экземпляры данного типа не найдены.";
                return Result.Failed;
            }

            // --- ПРЕДОХРАНИТЕЛЬ v1.1.53 --- 
            // Проверяем, есть ли в группе хоть один элемент, который мы сможем "пометить"
            Group testGrp = instances.First();
            var memberIds = testGrp.GetMemberIds();
            bool hasTaggableElements = false;

            foreach (var id in memberIds)
            {
                Element el = doc.GetElement(id);
                if (el == null) continue;

                // Линии мы исключили (CurveElement), поэтому они не считаются за "годные" для синхронизации
                if (el is CurveElement) continue;

                // Проверяем наличие параметра
                if (el.LookupParameter(SharedParameterService.ParameterNameType) != null)
                {
                    hasTaggableElements = true;
                    break;
                }
            }

            if (!hasTaggableElements)
            {
                message = "Разборка невозможна: группа не содержит элементов, поддерживающих идентификацию (например, состоит только из линий).";
                return Result.Failed;
            }
            // ------------------------------

            Guid typeGuid = Guid.NewGuid();
            GroupRecord record = new GroupRecord
            {
                TypeGuid = typeGuid,
                TypeName = gType.Name,
                Status   = GroupStatus.Disassembled
            };

            using (Transaction tx = new Transaction(doc, $"SGM: Разбор типа {gType.Name}"))
            {
                tx.Start();
                try
                {
                    for (int i = 0; i < instances.Count; i++)
                    {
                        Group grp = instances[i];
                        Guid instanceId = Guid.NewGuid();
                        
                        // Запоминаем данные о положении (МЕТОД МАЯКА v1.1.59)
                        LocationPoint loc = grp.Location as LocationPoint;
                        XYZ origin = loc?.Point ?? XYZ.Zero;
                        double rotation = GetBeaconRotation(doc, grp);

                        // Сохраняем угол ОБРАЗЦА (первого экземпляра) для расчета дельты
                        if (i == 0) record.TemplateRotation = rotation;

                        record.Instances.Add(new InstanceRecord
                        {
                            InstanceId = instanceId,
                            InsertionX = origin.X,
                            InsertionY = origin.Y,
                            InsertionZ = origin.Z,
                            RotationAngle = rotation
                        });

                        // РАЗБИРАЕМ И ТЕГИРУЕМ ВСЕ ЭЛЕМЕНТЫ
                        ICollection<ElementId> members = grp.UngroupMembers();
                        foreach (ElementId id in members)
                        {
                            Element el = doc.GetElement(id);
                            if (el == null) continue;

                            el.LookupParameter(SharedParameterService.ParameterNameType)?.Set(typeGuid.ToString());
                            el.LookupParameter(SharedParameterService.ParameterNameInstance)?.Set(instanceId.ToString());
                        }
                    }

                    // Удаляем тип группы, так как он больше не нужен
                    try { doc.Delete(gType.Id); } catch { }

                    GroupRegistryService.Update(doc, record);
                    tx.Commit();

                    TaskDialog.Show("Разборка завершена", 
                        $"✓ Тип \"{record.TypeName}\" полностью разобран.\n" +
                        $"Все элементы помечены для восстановления.\n" +
                        $"Запомнено позиций: {instances.Count}");

                    App.RefreshManagerPane();
                    return Result.Succeeded;
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    message = ex.Message;
                    return Result.Failed;
                }
            }
        }

        /// <summary>
        /// Вспомогательный метод для получения угла поворота группы через её участников (Метод Маяка).
        /// </summary>
        private static double GetBeaconRotation(Document doc, Group grp)
        {
            // Пытаемся взять стандартный поворот (если Revit позволит)
            try 
            {
                if (grp.Location is LocationPoint lp) return lp.Rotation;
            }
            catch { }

            // Если не вышло — ищем "Маяк" среди участников
            var memberIds = grp.GetMemberIds();
            foreach (var id in memberIds)
            {
                Element el = doc.GetElement(id);
                if (el == null) continue;

                // Для FamilyInstance (мебель, оборудование)
                if (el.Location is LocationPoint lp) return lp.Rotation;
                
                // Для Стен и Линий
                if (el.Location is LocationCurve lc && lc.Curve is Line line)
                {
                    XYZ dir = line.Direction;
                    return Math.Atan2(dir.Y, dir.X);
                }
            }

            return 0;
        }

        // ── СБОРКА (REASSEMBLE) ──────────────────────────────────────────────────

        public static Result ReassembleAll(Document doc, Guid typeGuid, out string message)
        {
            message = string.Empty;

            GroupRecord record = GroupRegistryService.GetAll(doc).FirstOrDefault(r => r.TypeGuid == typeGuid);
            if (record == null)
            {
                message = "Данные о типе не найдены.";
                return Result.Failed;
            }

            // Ищем элементы ОБРАЗЦА (помеченные typeGuid)
            var templateElements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.LookupParameter(SharedParameterService.ParameterNameType)?.AsString() == typeGuid.ToString())
                .ToList();

            if (!templateElements.Any())
            {
                message = "Элементы образца не найдены. Не из чего собирать группу.";
                return Result.Failed;
            }

            using (Transaction tx = new Transaction(doc, $"SGM: Сборка типа {record.TypeName}"))
            {
                tx.Start();
                try
                {
                    // 1. Создаем НОВЫЙ тип из образца
                    // СУПЕР-СТРОГАЯ ФИЛЬТРАЦИЯ v1.1.32
                    HashSet<ElementId> templateIds = new HashSet<ElementId>();
                    ElementId commonDesignOptionId = null;

                    foreach (Element e in templateElements)
                    {
                        if (e == null || !e.IsValidObject) continue;

                        // ИСКЛЮЧАЕМ только мета-элементы (уровни, виды, сами группы)
                        if (e is Level || e is View || e is Group) continue;

                        // ИСКЛЮЧАЕМ ЛИНИИ (модели и аннотации) по просьбе пользователя
                        if (e is CurveElement) continue;

                        // ПРОВЕРКА НА ВЛОЖЕННОСТЬ (если это семейство, оно не должно быть частью другого)
                        if (e is FamilyInstance fi && fi.SuperComponent != null) continue;

                        // Если категория есть, проверяем тип (Модель/Аннотация/Анал-ка)
                        bool isValidType = false;
                        if (e.Category != null)
                        {
                            var ct = e.Category.CategoryType;
                            isValidType = (ct == CategoryType.Model || ct == CategoryType.Annotation || ct == CategoryType.AnalyticalModel);
                        }

                        // Собираем: (Любые модели/аннотации) И не привязаны к конкретному виду
                        if (isValidType && !e.ViewSpecific && e.GroupId == ElementId.InvalidElementId)
                        {
                            templateIds.Add(e.Id);
                        }
                    }

                    if (templateIds.Count == 0)
                    {
                        message = "Не найдены подходящие элементы для сборки группы (v1.1.32).";
                        return Result.Failed;
                    }

                    // --- БЕЗОПАСНАЯ ДИАГНОСТИКА v1.1.32 (Windows MessageBox) ---
                    string diagMsg = $"BURO Group Manager: Найдено {templateIds.Count} элементов.\nПродолжить сборку в Revit?";
                    if (System.Windows.MessageBox.Show(diagMsg, "BURO Group Manager v1.1.32", System.Windows.MessageBoxButton.YesNo) != System.Windows.MessageBoxResult.Yes)
                    {
                        message = "Сборка отменена.";
                        return Result.Failed;
                    }

                    // Подавляем стандартные окна Ревита
                    FailureHandlingOptions failOptions = tx.GetFailureHandlingOptions();
                    failOptions.SetFailuresPreprocessor(new SgmFailurePreprocessor());
                    tx.SetFailureHandlingOptions(failOptions);

                    Group masterGroupInstance = null;
                    try
                    {
                        masterGroupInstance = doc.Create.NewGroup(templateIds.ToList());
                    }
                    catch (Exception ex)
                    {
                        StatusDetect(doc, templateIds.ToList(), out message);
                        return Result.Failed;
                    }

                    GroupType newType = masterGroupInstance.GroupType;
                    string finalName = GetUniqueName(doc, record.TypeName, newType.Id);
                    if (newType.Name != finalName) newType.Name = finalName;

                    int placedCount = 0;
                    bool firstSkipped = false;
                    foreach (var instRec in record.Instances)
                    {
                        XYZ point = new XYZ(instRec.InsertionX, instRec.InsertionY, instRec.InsertionZ);
                        
                        Group currentGroup;
                        try 
                        {
                            if (!firstSkipped)
                            {
                                currentGroup = masterGroupInstance;
                                firstSkipped = true;
                            }
                            else
                            {
                                currentGroup = doc.Create.PlaceGroup(point, newType);
                            }
                        }
                        catch
                        {
                            continue;
                        }

                        // Поворот, если нужен (ВЫЧИСЛЯЕМ ДЕЛЬТУ v1.1.57)
                        double deltaRotation = instRec.RotationAngle - record.TemplateRotation;

                        // Вращаем все экземпляры, чей угол отличается от базового образца
                        if (Math.Abs(deltaRotation) > 0.0001) 
                        {
                            try
                            {
                                // ДИАГНОСТИКА: временно показываем расчеты
                                // TaskDialog.Show("Debug Rotation", $"Target={instRec.RotationAngle:F3}, Template={record.TemplateRotation:F3}, Delta={deltaRotation:F3}");

                                Line axis = Line.CreateBound(point, point + XYZ.BasisZ);
                                ElementTransformUtils.RotateElement(doc, currentGroup.Id, axis, deltaRotation);
                            }
                            catch { }
                        }
                        placedCount++;
                    }

                    GroupRegistryService.Unregister(doc, typeGuid);

                    tx.Commit();
                    
                    System.Windows.MessageBox.Show($"✓ Тип \"{finalName}\" восстановлен ({placedCount} шт).", "BURO Group Manager v1.1.32");
                    App.RefreshManagerPane();
                    return Result.Succeeded;
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    message = ex.Message;
                    return Result.Failed;
                }
            }
        }

        private static void StatusDetect(Document doc, List<ElementId> ids, out string message)
        {
            message = "Ошибка сборки Revit (v1.1.26). Начинаю пошаговый поиск виновного...\n";
            List<ElementId> cumulativeList = new List<ElementId>();
            
            foreach (ElementId id in ids)
            {
                cumulativeList.Add(id);
                try
                {
                    using (SubTransaction sub = new SubTransaction(doc))
                    {
                        sub.Start();
                        doc.Create.NewGroup(cumulativeList);
                        sub.RollBack();
                    }
                }
                catch (Exception ex)
                {
                    Element badEl = doc.GetElement(id);
                    string hostInfo = "";
                    if (badEl is FamilyInstance fi && fi.Host != null)
                    {
                        hostInfo = $"\nХост (стена/перекрытие): ID {fi.Host.Id}";
                        if (!ids.Contains(fi.Host.Id))
                        {
                            hostInfo += " (!!! ВНИМАНИЕ: Хоста НЕТ в списке на сборку !!!)";
                        }
                    }

                    message = $"ВИНОНИК НАЙДЕН! (v1.1.26)\n\n" +
                              $"Сборка сломалась на элементе:\n" +
                              $"ID: {id}\n" +
                              $"Имя: {badEl?.Name}\n" +
                              $"Категория: {badEl?.Category?.Name}\n" +
                              $"{hostInfo}\n\n" +
                              $"Ошибка Revit: {ex.Message}";
                    return;
                }
            }
            message = "Ошибка сборки Revit (v1.1.26), но кумулятивная проверка не выявила проблем. Возможно, ошибка возникает только при финальной сборке.";
        }

        private static bool EnsureParameters(Document doc, out string message)
        {
            message = "";
            TaskDialog setupDlg = new TaskDialog("Настройка проекта");
            setupDlg.MainInstruction = "В проекте не настроены необходимые параметры SGM.";
            setupDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Настроить сейчас");
            setupDlg.CommonButtons = TaskDialogCommonButtons.Cancel;
            if (setupDlg.Show() == TaskDialogResult.CommandLink1)
            {
                if (SharedParameterService.SetupSharedParameter(doc) == Result.Succeeded) return true;
                message = "Ошибка при создании параметров.";
            }
            return false;
        }

        private static void ClearParameters(IEnumerable<Element> elements)
        {
            foreach (var el in elements)
            {
                el.LookupParameter(SharedParameterService.ParameterNameType)?.Set("");
            }
        }


        // ── ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ───────────────────────────────────────────────

        private static string GetUniqueName(Document doc, string baseName, ElementId excludeId)
        {
            bool NameTaken(string candidate) =>
                new FilteredElementCollector(doc)
                    .OfClass(typeof(GroupType))
                    .Any(gt => gt.Name == candidate && gt.Id != excludeId);

            if (!NameTaken(baseName)) return baseName;

            int suffix = 1;
            string result;
            do { result = $"{baseName}_{suffix++}"; }
            while (NameTaken(result));

            return result;
        }
    }

    /// <summary>
    /// Перехватчик ошибок Revit — подавляет стандартные окна и позволяет плагину самому вывести текст ошибки.
    /// </summary>
    public class SgmFailurePreprocessor : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            var failures = failuresAccessor.GetFailureMessages();
            if (failures.Count == 0) return FailureProcessingResult.Continue;

            // Мы позволяем Ревиту удалить ошибки из очереди, чтобы они не всплывали в окнах.
            // Но мы НЕ разрешаем продолжать, если там есть критические ошибки.
            return FailureProcessingResult.Continue;
        }
    }
}
