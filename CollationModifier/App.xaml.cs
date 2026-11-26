using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using CollationModifier.Services;

namespace CollationModifier
{
    public partial class App : Application
    {
        private readonly LoggerService _logger;
        private static bool _handlersRegistered;
        private static readonly object _syncLock = new object();

        public static readonly DependencyProperty TrackEditableFocusProperty =
            DependencyProperty.RegisterAttached(
                "TrackEditableFocus",
                typeof(bool),
                typeof(App),
                new PropertyMetadata(false, OnTrackEditableFocusChanged));

        private static readonly DependencyProperty ActiveFocusAdornerProperty =
            DependencyProperty.RegisterAttached(
                "ActiveFocusAdorner",
                typeof(EditableComboBoxFocusAdorner),
                typeof(App),
                new PropertyMetadata(null));

        public static bool GetTrackEditableFocus(DependencyObject obj)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            return (bool)obj.GetValue(TrackEditableFocusProperty);
        }

        public static void SetTrackEditableFocus(DependencyObject obj, bool value)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            obj.SetValue(TrackEditableFocusProperty, value);
        }

        public LoggerService Logger => _logger;

        public static Func<string, string, bool>? InteractiveRecoveryPromptHandler { get; set; }
        public static Action? ShutdownHandler { get; set; }

        public App() : this(new LoggerService())
        {
        }

        public App(LoggerService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            RegisterExceptionHandlers();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("en-US")));
            RegisterExceptionHandlers();
        }

        private static void OnTrackEditableFocusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ComboBox comboBox)
            {
                return;
            }

            comboBox.Loaded -= OnComboBoxLoaded;
            comboBox.Unloaded -= OnComboBoxUnloaded;
            comboBox.IsKeyboardFocusWithinChanged -= OnComboBoxIsKeyboardFocusWithinChanged;
            comboBox.IsEnabledChanged -= OnComboBoxIsEnabledChanged;

            if (e.NewValue is true)
            {
                comboBox.Loaded += OnComboBoxLoaded;
                comboBox.Unloaded += OnComboBoxUnloaded;

                if (comboBox.IsEditable)
                {
                    comboBox.IsKeyboardFocusWithinChanged += OnComboBoxIsKeyboardFocusWithinChanged;
                    comboBox.IsEnabledChanged += OnComboBoxIsEnabledChanged;
                    UpdateEditableComboBoxFocusAdorner(comboBox);
                }
            }
            else
            {
                RemoveEditableComboBoxFocusAdorner(comboBox);
            }
        }

        private static void OnComboBoxLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ComboBox comboBox)
            {
                return;
            }

            comboBox.IsKeyboardFocusWithinChanged -= OnComboBoxIsKeyboardFocusWithinChanged;
            comboBox.IsEnabledChanged -= OnComboBoxIsEnabledChanged;

            if (comboBox.IsEditable)
            {
                comboBox.IsKeyboardFocusWithinChanged += OnComboBoxIsKeyboardFocusWithinChanged;
                comboBox.IsEnabledChanged += OnComboBoxIsEnabledChanged;
                UpdateEditableComboBoxFocusAdorner(comboBox);
            }
            else
            {
                RemoveEditableComboBoxFocusAdorner(comboBox);
            }
        }

        private static void OnComboBoxUnloaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ComboBox comboBox)
            {
                return;
            }

            comboBox.IsKeyboardFocusWithinChanged -= OnComboBoxIsKeyboardFocusWithinChanged;
            comboBox.IsEnabledChanged -= OnComboBoxIsEnabledChanged;
            RemoveEditableComboBoxFocusAdorner(comboBox);
        }

        private static void OnComboBoxIsKeyboardFocusWithinChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is ComboBox comboBox)
            {
                UpdateEditableComboBoxFocusAdorner(comboBox);
            }
        }

        private static void OnComboBoxIsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is ComboBox comboBox)
            {
                UpdateEditableComboBoxFocusAdorner(comboBox);
            }
        }

        private static void UpdateEditableComboBoxFocusAdorner(ComboBox comboBox)
        {
            if (!comboBox.IsEditable || !comboBox.IsEnabled || !comboBox.IsKeyboardFocusWithin)
            {
                RemoveEditableComboBoxFocusAdorner(comboBox);
                return;
            }

            var adornerLayer = AdornerLayer.GetAdornerLayer(comboBox);
            if (adornerLayer == null)
            {
                RemoveEditableComboBoxFocusAdorner(comboBox);
                return;
            }

            var existingAdorner = comboBox.GetValue(ActiveFocusAdornerProperty) as EditableComboBoxFocusAdorner;
            if (existingAdorner != null)
            {
                var layerAdorners = adornerLayer.GetAdorners(comboBox);
                if (ReferenceEquals(existingAdorner.OwnerLayer, adornerLayer) &&
                    layerAdorners != null &&
                    Array.IndexOf(layerAdorners, existingAdorner) >= 0)
                {
                    existingAdorner.InvalidateMeasure();
                    return;
                }

                RemoveEditableComboBoxFocusAdorner(comboBox);
            }

            var strayAdorners = adornerLayer.GetAdorners(comboBox);
            if (strayAdorners != null)
            {
                foreach (var stray in strayAdorners)
                {
                    if (stray is EditableComboBoxFocusAdorner strayFocusAdorner)
                    {
                        strayFocusAdorner.Detach();
                    }
                }
            }

            var focusVisualStyle = (comboBox.TryFindResource("FocusVisualStyle") ?? Current?.TryFindResource("FocusVisualStyle")) as Style;
            if (focusVisualStyle == null)
            {
                return;
            }

            var adorner = new EditableComboBoxFocusAdorner(comboBox, adornerLayer, focusVisualStyle);
            adornerLayer.Add(adorner);
            comboBox.SetValue(ActiveFocusAdornerProperty, adorner);
        }

        private static void RemoveEditableComboBoxFocusAdorner(ComboBox comboBox)
        {
            if (comboBox.GetValue(ActiveFocusAdornerProperty) is EditableComboBoxFocusAdorner activeAdorner)
            {
                comboBox.ClearValue(ActiveFocusAdornerProperty);
                activeAdorner.Detach();
            }

            var adornerLayer = AdornerLayer.GetAdornerLayer(comboBox);
            var adorners = adornerLayer?.GetAdorners(comboBox);
            if (adorners != null)
            {
                foreach (var adorner in adorners)
                {
                    if (adorner is EditableComboBoxFocusAdorner focusAdorner)
                    {
                        focusAdorner.Detach();
                    }
                }
            }
        }

        private sealed class EditableComboBoxFocusAdorner : Adorner
        {
            private readonly AdornerLayer _ownerLayer;
            private readonly Control _child;

            public AdornerLayer OwnerLayer => _ownerLayer;

            public EditableComboBoxFocusAdorner(UIElement adornedElement, AdornerLayer ownerLayer, Style focusVisualStyle)
                : base(adornedElement)
            {
                _ownerLayer = ownerLayer ?? throw new ArgumentNullException(nameof(ownerLayer));
                IsHitTestVisible = false;
                Focusable = false;

                _child = new Control
                {
                    IsHitTestVisible = false,
                    Focusable = false,
                    IsTabStop = false,
                    Style = focusVisualStyle ?? throw new ArgumentNullException(nameof(focusVisualStyle))
                };

                AddVisualChild(_child);
            }

            protected override int VisualChildrenCount => 1;

            protected override Visual GetVisualChild(int index)
            {
                if (index != 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return _child;
            }

            protected override Size MeasureOverride(Size constraint)
            {
                Size renderSize = AdornedElement.RenderSize;
                _child.Measure(renderSize);
                return renderSize;
            }

            protected override Size ArrangeOverride(Size finalSize)
            {
                Size renderSize = AdornedElement.RenderSize;
                _child.Arrange(new Rect(new Point(0, 0), renderSize));
                return renderSize;
            }

            public void Detach()
            {
                _ownerLayer.Remove(this);
                if (VisualTreeHelper.GetParent(this) is AdornerLayer parentLayer &&
                    !ReferenceEquals(parentLayer, _ownerLayer))
                {
                    parentLayer.Remove(this);
                }
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                _logger?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during application exit cleanup: {ex.Message}");
            }
            base.OnExit(e);
        }

        public void RegisterExceptionHandlers()
        {
            lock (_syncLock)
            {
                if (_handlersRegistered) return;

                this.DispatcherUnhandledException += OnDispatcherUnhandledException;
                AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
                TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

                _handlersRegistered = true;
            }
        }

        public void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = GlobalExceptionHandler.HandleDispatcherException(
                e.Exception,
                _logger,
                InteractiveRecoveryPromptHandler ?? DefaultPromptHandler,
                ShutdownHandler ?? (() => Shutdown()));
        }

        public void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            GlobalExceptionHandler.HandleAppDomainException(e.ExceptionObject, e.IsTerminating, _logger);
        }

        public void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            GlobalExceptionHandler.HandleUnobservedTaskException(e.Exception, () => e.SetObserved(), _logger);
        }

        private static bool DefaultPromptHandler(string message, string caption)
        {
            var result = MessageBox.Show(message, caption, MessageBoxButton.YesNo, MessageBoxImage.Warning);
            return result == MessageBoxResult.Yes;
        }
    }
}
