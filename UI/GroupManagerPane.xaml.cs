using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.UI;
using BM_GroupManager.Models;
using BM_GroupManager.Services;

namespace BM_GroupManager.UI
{
    public partial class GroupManagerPane : Page, IDockablePaneProvider
    {
        // ── IDockablePaneProvider ────────────────────────────────────────────────

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement = this;
            data.InitialState = new DockablePaneState
            {
                DockPosition = DockPosition.Tabbed,
                TabBehind    = DockablePanes.BuiltInDockablePanes.ProjectBrowser
            };
        }

        // ── Конструктор ──────────────────────────────────────────────────────────

        public GroupManagerPane()
        {
            InitializeComponent();
        }

        // ── Обновление данных ────────────────────────────────────────────────────

        public void Refresh()
        {
            UIApplication uiApp = App.CurrentUIApp;
            if (uiApp?.ActiveUIDocument?.Document == null)
            {
                StatusText.Text = "Нет открытого документа";
                GroupsGrid.ItemsSource = null;
                return;
            }

            Autodesk.Revit.DB.Document doc = uiApp.ActiveUIDocument.Document;

            try
            {
                // 1. Собираем только ТИПЫ ГРУПП МОДЕЛИ (исключаем Группы Узлов)
                var groupTypes = new Autodesk.Revit.DB.FilteredElementCollector(doc)
                    .OfClass(typeof(Autodesk.Revit.DB.GroupType))
                    .Cast<Autodesk.Revit.DB.GroupType>()
                    .Where(gt => gt.Category?.Id.IntegerValue == (int)Autodesk.Revit.DB.BuiltInCategory.OST_IOSModelGroups)
                    .ToList();

                var assembledByType = groupTypes
                    .Select(gt => new GroupViewModel(gt, gt.Groups.Size))
                    .ToList();

                // 2. Получаем РАЗОБРАННЫЕ типы из реестра
                List<GroupRecord> records = GroupRegistryService.GetAll(doc);
                var disassembled = records.Select(r => new GroupViewModel(r)).ToList();

                // 3. Объединяем
                var allTypes = assembledByType.Concat(disassembled).OrderBy(v => v.TypeName).ToList();

                GroupsGrid.ItemsSource = allTypes;
                StatusText.Text = $"Типов групп: {allTypes.Count} (Разобрано: {disassembled.Count})";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка: {ex.Message}";
            }
        }

        // ── Обработчики UI ───────────────────────────────────────────────────────

        private void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            Refresh();
        }

        private void OnActionClick(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn)) return;
            if (!(btn.DataContext is GroupViewModel vm)) return;

            if (vm.IsDisassembled)
            {
                App.ReassembleHandler.TargetGuid = vm.TypeGuid;
                App.ReassembleEvent.Raise();
            }
            else
            {
                // ПРОВЕРКА: Если экземпляров 0 — разбирать нельзя
                if (vm.InstanceCount == 0)
                {
                    TaskDialog.Show("BM Group Manager", "Этой группы нет в проекте. \nРазборка невозможна. Сначала разместите экземпляр группы.");
                    return;
                }

                App.UnwrapHandler.TargetTypeId = vm.TypeId;
                App.UnwrapEvent.Raise();
            }
        }


        private void OnCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            if (!(e.Row.Item is GroupViewModel vm)) return;
            if (vm.IsAssembled) return; // Переименование через таблицу пока только для разобранных

            string newName = (e.EditingElement as System.Windows.Controls.TextBox)?.Text?.Trim();
            if (string.IsNullOrEmpty(newName) || newName == vm.TypeName) return;

            App.RenameHandler.TargetGuid = vm.TypeGuid;
            App.RenameHandler.NewName    = newName;
            App.RenameEvent.Raise();
        }
    }

    // ── ViewModel строки таблицы ─────────────────────────────────────────────────

    public class GroupViewModel
    {
        private readonly GroupRecord _record;
        private readonly Autodesk.Revit.DB.GroupType _groupType;
        private readonly int _instanceCount;

        public int InstanceCount => _instanceCount;

        public bool IsDisassembled => _record != null;
        public bool IsAssembled    => _groupType != null;

        // Для разобранных
        public GroupViewModel(GroupRecord record)
        {
            _record = record;
            _instanceCount = record.Instances.Count;
        }

        // Для собранных
        public GroupViewModel(Autodesk.Revit.DB.GroupType groupType, int count)
        {
            _groupType = groupType;
            _instanceCount = count;
        }

        public Guid TypeGuid => _record?.TypeGuid ?? Guid.Empty;
        public Autodesk.Revit.DB.ElementId TypeId => _groupType?.Id;

        public string TypeName
        {
            get 
            {
                string name = _record?.TypeName ?? _groupType?.Name ?? "Неизвестно";
                return $"{name} ({_instanceCount} шт)";
            }
            set 
            {
                if (_record != null) _record.TypeName = value;
            }
        }

        public string StatusDisplay 
        {
            get
            {
                if (_instanceCount == 0) return "❌ Не размещена";
                return IsDisassembled ? "🔓 Разобрана" : "🔒 Собрана";
            }
        }

        public string ActionButtonText => IsDisassembled ? "Собрать" : "Разбор";

        // Цветовая индикация статуса
        public Brush StatusBrush
        {
            get
            {
                if (IsDisassembled) return Brushes.DodgerBlue; // Синий
                if (_instanceCount == 0) return Brushes.Crimson; // Красный
                return Brushes.ForestGreen; // Зеленый
            }
        }

        public bool CanDelete => true;

        public bool CanRename => IsDisassembled; 
    }
}
