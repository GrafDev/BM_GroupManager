using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BM_GroupManager.Handlers;
using BM_GroupManager.UI;
using System.Windows.Media.Imaging;
using System.Reflection;

namespace BM_GroupManager
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class App : IExternalApplication
    {
        // ── Статические ресурсы ──────────────────────────────────────────────────

        public static readonly DockablePaneId PaneId =
            new DockablePaneId(new Guid("F9A8B7C6-D5E4-43A2-B1B0-9A8B7C6D5E4F"));

        /// <summary>Текущий UIApplication — обновляется при каждой команде.</summary>
        public static UIApplication CurrentUIApp { get; set; }

        // ExternalEvents для WPF → Revit API
        public static ReassembleEventHandler ReassembleHandler;
        public static ExternalEvent          ReassembleEvent;

        public static UnwrapEventHandler UnwrapHandler;
        public static ExternalEvent     UnwrapEvent;

        public static RenameGroupEventHandler RenameHandler;
        public static ExternalEvent           RenameEvent;


        private static GroupManagerPane _pane;

        // ── IExternalApplication ─────────────────────────────────────────────────

        public Result OnStartup(UIControlledApplication application)
        {
            // Создаём панель
            _pane = new GroupManagerPane();

            application.RegisterDockablePane(PaneId, "BURO Group Manager v1.1.62", _pane);

            // Подписываемся на Idling, чтобы подхватить UIApplication автоматически
            application.Idling += OnIdling;

            // Создаём ExternalEvents
            ReassembleHandler = new ReassembleEventHandler();
            ReassembleEvent   = ExternalEvent.Create(ReassembleHandler);

            UnwrapHandler = new UnwrapEventHandler();
            UnwrapEvent   = ExternalEvent.Create(UnwrapHandler);

            RenameHandler = new RenameGroupEventHandler();
            RenameEvent   = ExternalEvent.Create(RenameHandler);


            // Лента
            CreateRibbon(application);

            // Подписываемся на открытие документа
            application.ControlledApplication.DocumentOpened += OnDocumentOpened;

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            application.Idling -= OnIdling;
            application.ControlledApplication.DocumentOpened -= OnDocumentOpened;
            return Result.Succeeded;
        }

        private void OnIdling(object sender, Autodesk.Revit.UI.Events.IdlingEventArgs e)
        {
            // Пытаемся захватить UIApplication, если он еще не задан
            if (CurrentUIApp == null)
            {
                UIApplication uiApp = sender as UIApplication;
                if (uiApp != null && uiApp.ActiveUIDocument != null)
                {
                    CurrentUIApp = uiApp;
                    RefreshManagerPane();
                }
            }
        }

        // ── Лента ────────────────────────────────────────────────────────────────

        private void CreateRibbon(UIControlledApplication app)
        {
            const string tabName   = "BM Plugins";
            const string panelName = "BM Plugins";

            try { app.CreateRibbonTab(tabName); } catch { /* уже существует */ }

            RibbonPanel panel = null;
            foreach (RibbonPanel p in app.GetRibbonPanels(tabName))
                if (p.Name == panelName) { panel = p; break; }
            if (panel == null)
                panel = app.CreateRibbonPanel(tabName, panelName);

            string dll = typeof(App).Assembly.Location;

            // Оставляем только ОДНУ КНОПКУ Менеджер Групп
            PushButtonData btnManager = new PushButtonData(
                "SGM_Manager",
                "BURO Group\nManager",
                dll,
                "BM_GroupManager.Commands.ShowManagerCommand")
            {
                ToolTip = "Открыть панель управления синхронизацией групп (v1.1.62)",
                LargeImage = LoadResourceImage("iconGroupManager.png")
            };

            panel.AddItem(btnManager);
        }

        private BitmapSource LoadResourceImage(string name)
        {
            try
            {
                string path = $"pack://application:,,,/{Assembly.GetExecutingAssembly().GetName().Name};component/BM_RevitPlugin_SharedIcons/{name}";
                BitmapImage image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(path);
                image.DecodePixelWidth = 32; // Оптимизирует размер (от белого квадрата)
                image.EndInit();
                return image;
            }
            catch { return null; }
        }

        // ── Обработчик событий документа ────────────────────────────────────────

        private void OnDocumentOpened(object sender,
            Autodesk.Revit.DB.Events.DocumentOpenedEventArgs e)
        {
            // UIApplication недоступен в этом событии напрямую,
            // поэтому обновляем панель при следующей команде пользователя.
        }

        // ── Публичные методы ─────────────────────────────────────────────────────

        /// <summary>Показать панель и обновить данные.</summary>
        public static void ShowManagerPane(UIApplication uiApp)
        {
            CurrentUIApp = uiApp;
            DockablePane pane = uiApp.GetDockablePane(PaneId);
            pane?.Show();
            _pane?.Refresh();
        }

        /// <summary>Обновить панель без открытия.</summary>
        public static void RefreshManagerPane()
        {
            _pane?.Dispatcher.BeginInvoke(new Action(() => _pane.Refresh()));
        }
    }
}
