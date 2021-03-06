﻿using Miriot.Services;
using Miriot.Core.ViewModels.Widgets;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Miriot.Win10.Controls
{
    public class WidgetBase : UserControl
    {
        public WidgetBase()
        {
        }

        public WidgetBase(WidgetModel widget)
        {
            Margin = new Thickness(20);

            if (widget != null)
                SetPosition(widget.X, widget.Y);
        }

        private WidgetStates _state;
        public WidgetStates State
        {
            get => _state;
            set
            {
                _state = value;
                OnStateChanged();
            }
        }

        public virtual void OnStateChanged() { }

        public virtual void SetPosition(int? x, int? y)
        {
            if (x != null)
                Grid.SetColumn(this, x.Value);

            if (y != null)
                Grid.SetRow(this, y.Value);
        }
    }
}
