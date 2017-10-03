﻿using GalaSoft.MvvmLight.Messaging;
using Microsoft.Practices.ServiceLocation;
using Microsoft.ProjectOxford.Common;
using Miriot.Common;
using Miriot.Controls;
using Miriot.Core.Messages;
using Miriot.Core.Services.Interfaces;
using Miriot.Core.ViewModels;
using Miriot.Core.ViewModels.Widgets;
using Miriot.Utils;
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Gpio;
using Windows.Foundation;
using Windows.Media.SpeechSynthesis;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace Miriot
{
    public sealed partial class MainPage
    {
        private bool _isProcessing;
        private CoreDispatcher _dispatcher;
        private ColorBloomTransitionHelper _transition;
        private readonly IFrameAnalyzer<ServiceResponse> _frameAnalyzer;
        private int _noFaceDetectedCount;

        private MainViewModel Vm => DataContext as MainViewModel;

        #region Ctor
        public MainPage()
        {
            InitializeComponent();
            InitializeTransitionHelper();

            Loaded += MainPage_Loaded;
            _frameAnalyzer = ServiceLocator.Current.GetInstance<IFrameAnalyzer<ServiceResponse>>();
        }

        #endregion

        private void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            Bloomer();

            _dispatcher = CoreWindow.GetForCurrentThread().Dispatcher;

            Task.Run(async () => await _frameAnalyzer.AttachAsync(Camera));
            _frameAnalyzer.AnalysisFunction = Vm.IdentifyFaces;
            _frameAnalyzer.UsersIdentified += OnUsersIdentified;
            _frameAnalyzer.NoFaceDetected += OnNoFaceDetected;
            _frameAnalyzer.OnPreAnalysis += OnStartingIdentification;

            Vm.ActionCallback = OnAction;

            Vm.PropertyChanged += VmOnPropertyChanged;
        }

        private void TurnOnLeds()
        {
            var gpio = GpioController.GetDefault();

            if (gpio == null) return;

            var pin = gpio.OpenPin(2);
            pin.SetDriveMode(GpioPinDriveMode.Output);
            pin.Write(GpioPinValue.High);
        }

        private void OnAction(ActionMessage msg)
        {
            DoAction(msg.Intent);
        }

        private void VmOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Vm.Widgets))
            {
                if (Vm.Widgets != null)
                {
                    Vm.Widgets.CollectionChanged -= WidgetsChanged;
                    Vm.Widgets.CollectionChanged += WidgetsChanged;
                }
            }

            if (e.PropertyName == nameof(Vm.SpeakStream))
            {
                if (Vm.SpeakStream != null)
                {
                    MediaElementCtrl.SetSource(Vm.SpeakStream, ((SpeechSynthesisStream)Vm.SpeakStream).ContentType);
                    MediaElementCtrl.Play();
                }
            }
        }

        private void WidgetsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                WidgetZone.Children.Clear();
            }
            else if (e.NewItems.Count > 0)
            {
                LoadWidget((WidgetModel)e.NewItems[0]);
            }
        }

        private async void OnStartingIdentification(object sender, EventArgs eventArgs)
        {
            await RunOnUiThread(() =>
            {
                if (Vm.CurrentState == States.Active)
                {
                    return;
                }

                Vm.IsLoading = true;
                Vm.StateChangedCommand.Execute(States.Active);
            });
        }

        private async void OnNoFaceDetected(object sender, EventArgs e)
        {
            await RunOnUiThread(() =>
            {
                _noFaceDetectedCount++;

                if (_noFaceDetectedCount < 15) return;

                Vm.StateChangedCommand.Execute(States.Inactive);
                CleanUi();
            });
        }

        private async void OnUsersIdentified(object sender, ServiceResponse response)
        {
            await RunOnUiThread(() =>
            {
                _noFaceDetectedCount = 0;

                if (Vm.User != null)
                {
                    if (Vm.IsToothbrushing)
                    {
                        var user = response.Users.FirstOrDefault();
                        if (user != null)
                            SetToothZone(user.FaceRectangle);
                    }
                    return;
                }

                CleanUi();
                Vm.UsersIdentifiedCommand.Execute(response);
            });
        }

        private void SetToothZone(Rectangle userFaceRectangle)
        {
            Canvas.SetTop(ToothIndicator, userFaceRectangle.Top);
            Canvas.SetLeft(ToothIndicator, userFaceRectangle.Left);
        }

        /// <summary>
        /// All of the Color Bloom transition functionality is encapsulated in this handy helper
        /// which we will init once
        /// </summary>
        private void InitializeTransitionHelper()
        {
            // we pass in the UIElement that will host our Visuals
            _transition = new ColorBloomTransitionHelper(HostForVisual);

            // when the transition completes, we need to know so we can update other property values
            _transition.ColorBloomTransitionCompleted += ColorBloomTransitionCompleted;
        }

        private void Bloomer()
        {
            var initialBounds = new Rect  // maps to a rectangle the size of the header
            {
                Width = 110,
                Height = 110,
                X = 0,
                Y = 0
            };

            var finalBounds = Window.Current.Bounds;  // maps to the bounds of the current window

            _transition.Start(Colors.Black, initialBounds, finalBounds);
        }

        private WidgetBase GetWidgetInstance(Type t)
        {
            var w = WidgetZone.Children.FirstOrDefault(e => e.GetType() == t) as WidgetBase;
            if (w == null)
            {
                w = (WidgetBase)Activator.CreateInstance(t);
                WidgetZone.Children.Add(w);
            }

            return w;
        }

        private void DoAction(IntentResponse intent)
        {
            TurnOff();

            var t = intent.GetIntentType();

            var w = GetWidgetInstance(t);

            if (w is IWidgetAction)
                ((IWidgetAction)w).DoAction(intent);

            if (w == null)
            {
                // Generic actions
                switch (intent.Intent)
                {
                    case "TakePhoto":
                        TakePhoto();
                        break;
                    case "None":
                        if (Vm.IsListeningFirstName)
                            Vm.Repeat();
                        break;
                }
            }
        }

        private void TurnOff()
        {
            var w = WidgetZone.Children.FirstOrDefault(e => e is WidgetTv);
            if (w != null)
            {
                WidgetZone.Children.Remove(w);
            }

            w = WidgetZone.Children.FirstOrDefault(e => e is WidgetRadio);
            if (w != null)
            {
                WidgetZone.Children.Remove(w);
            }

            w = WidgetZone.Children.FirstOrDefault(e => e is WidgetDeezer);
            if (w != null)
            {
                WidgetZone.Children.Remove(w);
            }
        }

        private async Task RunOnUiThread(DispatchedHandler action)
        {
            await _dispatcher.RunAsync(CoreDispatcherPriority.Normal, action);
        }

        private void LoadWidget(WidgetModel model)
        {
            var t = model.GetWidgetType();
            var w = (WidgetBase)Activator.CreateInstance(t, model);

            if (w is IWidgetListener)
                ((IWidgetListener)w).OnInfosChanged += WidgetInfosChanged;

            // Add widget to grid
            WidgetZone.Children.Add(w);
        }

        private async void WidgetInfosChanged(object sender, EventArgs e)
        {
            var w = sender as WidgetBase;

            //if (w.OriginalWidget.Infos == null)
            //    w.OriginalWidget.Infos = new List<string>();
            //else
            //{
            //    var entry = w.OriginalWidget.Infos.FirstOrDefault(s => JsonConvert.DeserializeObject<OAuthWidgetInfo>(s)?.Token != null);

            //    if (entry != null)
            //        w.OriginalWidget.Infos.Remove(entry);
            //}

            //if (((IWidgetOAuth)w).Token != null)
            //{
            //    w.OriginalWidget.Infos.Add(JsonConvert.SerializeObject(new OAuthWidgetInfo { Token = ((IWidgetOAuth)w).Token }));
            //}

            await Vm.UpdateUserAsync();
        }

        private void CleanUi()
        {
            Vm.ResetCommand.Execute(null);
            // Force delete transition
            WidgetZone.Children.Clear();
            InfoUnknownPanel.Opacity = 0;
            Img.Source = null;
            MediaElementCtrl.Stop();
        }

        private void TakePhoto()
        {
            if (!_isProcessing)
            {
                _isProcessing = true;
                Task.Run(async () => await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
                {
                    var path = await Camera.TakePhotoAsync();
                    if (path != null)
                        Img.Source = new BitmapImage(new Uri(path));
                }));

                _isProcessing = false;
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            Vm.Initialize();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            _frameAnalyzer.Cleanup();
            Camera.Cleanup();
            Vm.Cleanup();
            Messenger.Default.Unregister(this);

            base.OnNavigatedFrom(e);
        }

        /// <summary>
        /// Updates the background of the layout panel to the same color whose transition animation just completed.
        /// </summary>
        private void ColorBloomTransitionCompleted(object sender, EventArgs e)
        {
            //// Grab an item off the pending transitions queue
            //var item = pendingTransitions.Dequeue();

            //// now remember, that bloom animation was just transitional
            //// so we need to explicitly set the correct color as background of the layout panel
            //var header = (AppBarButton)item.Header;
            MainGrid.Background = new SolidColorBrush(Colors.Black);
        }

        /// <summary>
        /// In response to a XAML layout event on the Grid (named UICanvas) we will apply a clip
        /// to ensure all Visual animations stay within the bounds of the Grid, and doesn't bleed into
        /// the top level Frame belonging to the Sample Gallery. Probably not a factor in most other cases.
        /// </summary>
        private void MainGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var uiCanvasLocation = MainGrid.TransformToVisual(MainGrid).TransformPoint(new Point(0d, 0d));
            var clip = new RectangleGeometry
            {
                Rect = new Rect(uiCanvasLocation, e.NewSize)
            };
            MainGrid.Clip = clip;
        }
    }
}
