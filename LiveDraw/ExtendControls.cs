using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace AntFu7.LiveDraw {
	internal class ActivatableButton : Button {
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

	internal enum ColorPickerButtonSize {
		Small,

		Middle,

		Large
	}

	internal class ColorPicker : ActivatableButton {
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
			double w = v switch {
				ColorPickerButtonSize.Small  => (double)Application.Current.Resources["ColorPickerSmall"],
				ColorPickerButtonSize.Middle => (double)Application.Current.Resources["ColorPickerMiddle"],
				_                            => (double)Application.Current.Resources["ColorPickerLarge"]
			};
			obj.BeginAnimation(WidthProperty, new DoubleAnimation(w, (Duration)Application.Current.Resources["Duration3"]));
		}
	}
}