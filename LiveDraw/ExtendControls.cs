using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace AntFu7.LiveDraw {
	public class ActivatableButton : Button {
		public static readonly DependencyProperty IsActivatedProperty = DependencyProperty.Register(
			nameof(IsActivated),
			typeof(bool),
			typeof(ActivatableButton),
			new PropertyMetadata(default(bool))
		);

		public bool IsActivated {
			get => (bool)GetValue(IsActivatedProperty);
			set => SetValue(IsActivatedProperty, value);
		}
	}

	public enum ColorPickerButtonSize {
		Small,

		Middle,

		Large
	}

	public class ColorPicker : ActivatableButton {
		public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
			nameof(Size),
			typeof(ColorPickerButtonSize),
			typeof(ColorPicker),
			new PropertyMetadata(default(ColorPickerButtonSize), OnColorPickerSizeChanged)
		);

		public ColorPickerButtonSize Size {
			get => (ColorPickerButtonSize)GetValue(SizeProperty);
			set => SetValue(SizeProperty, value);
		}

		private static void OnColorPickerSizeChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs) {
			var v = (ColorPickerButtonSize)eventArgs.NewValue;
			if (dependencyObject is not ColorPicker obj)
				return;
			var width = (double)Application.Current.Resources[$"ColorPicker{Enum.GetName(v)}"];
			obj.BeginAnimation(WidthProperty, new DoubleAnimation(width, (Duration)Application.Current.Resources["Duration3"]));
		}
	}
}